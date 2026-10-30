#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Domain;
using Core.Domain.Financials;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>Phase 2b-2 of #455: PPA HTTP flow and ClosingEntries system-only.</summary>
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task ClosingEntries_SaveJournalEntry_Returns400()
        {
            var draft = new Dto.Financial.JournalEntry
            {
                JournalDate = DateTime.UtcNow.Date,
                VoucherType = (int)JournalVoucherTypes.ClosingEntries,
                Memo = "Manual close attempt",
                JournalEntryLines =
                {
                    new Dto.Financial.JournalEntryLine { AccountId = AccountId("10111"), DrCr = 1, Amount = 10m },
                    new Dto.Financial.JournalEntryLine { AccountId = AccountId("30100"), DrCr = 2, Amount = 10m }
                }
            };
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft);
            Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
            Assert.Contains("Closing entries are created by the year-end close.",
                await save.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task PriorPeriodAdjustment_AfterClose_ShowsOnBalanceSheet()
        {
            var fyEnd = DateTime.UtcNow.Date.AddDays(-1);
            var fyStart = new DateTime(fyEnd.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var fyCode = "FY" + fyEnd.Year.ToString(CultureInfo.InvariantCulture);
            var openStart = fyEnd.AddDays(1);
            var openEnd = openStart.AddYears(1);
            var ppaDate = DateTime.UtcNow.Date;

            WithDb(db =>
            {
                var fy = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode);
                fy.EndDate = fyEnd;
                db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = _companyId,
                    FiscalYearCode = "FYOPEN",
                    FiscalYearName = "Open after close",
                    StartDate = openStart,
                    EndDate = openEnd,
                    IsActive = true
                });
                db.SaveChanges();
            });

            var invoice = await NewInvoice();
            invoice.InvoiceDate = fyStart.AddMonths(2);
            invoice.Status = "Open";
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice)).IsSuccessStatusCode);

            await CloseYearWithWarnings(fyCode);

            int closedFyId = 0;
            int ppaId = 0;
            int arId = 0;
            WithDb(db =>
            {
                closedFyId = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode).Id;
                ppaId = db.Accounts.Single(a => a.AccountCode == "30820").Id;
                arId = db.Accounts.Single(a => a.AccountCode == "10120").Id;
            });

            var ppa = new Dto.Financial.JournalEntry
            {
                JournalDate = ppaDate,
                VoucherType = (int)JournalVoucherTypes.PriorPeriodAdjustment,
                AdjustsFiscalYearId = closedFyId,
                Memo = "Material prior-year receivable correction",
                JournalEntryLines =
                {
                    new Dto.Financial.JournalEntryLine { AccountId = arId, DrCr = 1, Amount = 55m },
                    new Dto.Financial.JournalEntryLine { AccountId = ppaId, DrCr = 2, Amount = 55m }
                }
            };
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", ppa);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var saved = await save.Content.ReadFromJsonAsync<JsonElement>();
            ppa.Id = saved.GetProperty("id").GetInt32();

            var post = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", ppa);
            Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());

            var bs = await Send(HttpMethod.Get,
                $"/api/Financials/BalanceSheet?to={ppaDate:yyyy-MM-dd}T23:59:59.9999999");
            Assert.True(bs.IsSuccessStatusCode, await bs.Content.ReadAsStringAsync());
            var rows = await bs.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Array, rows.ValueKind);
            var ppaRow = rows.EnumerateArray()
                .FirstOrDefault(r => r.GetProperty("accountCode").GetString() == "30820");
            Assert.Equal(JsonValueKind.Object, ppaRow.ValueKind);
            Assert.Equal("Prior period adjustments", ppaRow.GetProperty("accountName").GetString());
            Assert.Equal(55m, ppaRow.GetProperty("amount").GetDecimal());
        }

        [Fact]
        public async Task PriorPeriodAdjustment_LockedPeriod_Returns400()
        {
            var fyEnd = DateTime.UtcNow.Date.AddDays(-1);
            var fyCode = "FY" + fyEnd.Year.ToString(CultureInfo.InvariantCulture);

            WithDb(db =>
            {
                var fy = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode);
                fy.EndDate = fyEnd;
                db.SaveChanges();
            });

            await CloseYearWithWarnings(fyCode);

            int closedFyId = 0;
            int ppaId = 0;
            int arId = 0;
            WithDb(db =>
            {
                closedFyId = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode).Id;
                ppaId = db.Accounts.Single(a => a.AccountCode == "30820").Id;
                arId = db.Accounts.Single(a => a.AccountCode == "10120").Id;
            });

            var lockedDate = fyEnd.AddDays(-10);
            var ppa = new Dto.Financial.JournalEntry
            {
                JournalDate = lockedDate,
                VoucherType = (int)JournalVoucherTypes.PriorPeriodAdjustment,
                AdjustsFiscalYearId = closedFyId,
                Memo = "Should be rejected — locked period",
                JournalEntryLines =
                {
                    new Dto.Financial.JournalEntryLine { AccountId = arId, DrCr = 1, Amount = 10m },
                    new Dto.Financial.JournalEntryLine { AccountId = ppaId, DrCr = 2, Amount = 10m }
                }
            };
            var save = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", ppa);
            Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
            Assert.Contains("locked", await save.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
