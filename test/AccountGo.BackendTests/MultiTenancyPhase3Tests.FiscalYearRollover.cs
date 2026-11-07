#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dto.Banking;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>Phase 1 of #455: fiscal years roll forward on posting (real SQLite).</summary>
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Invoice_DatedInNextFiscalYear_PostsAndCreatesTheYear()
        {
            DateTime nextYearStart = default;
            WithDb(db =>
            {
                var current = db.FinancialYears.OrderByDescending(y => y.StartDate).First(y => y.IsActive);
                nextYearStart = current.EndDate.Date.AddDays(1);
                db.FinancialYears.RemoveRange(db.FinancialYears.Where(y => y.StartDate.Date >= nextYearStart));
                db.SaveChanges();
            });

            var invoice = await NewInvoice();
            invoice.InvoiceDate = nextYearStart;
            invoice.Status = "Open";
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(
                !saved.TryGetProperty("postingError", out var postingError)
                || postingError.ValueKind == JsonValueKind.Null
                || string.IsNullOrEmpty(postingError.GetString()),
                postingError.ValueKind == JsonValueKind.String ? postingError.GetString() : "posting failed");

            WithDb(db =>
            {
                Assert.Contains(db.FinancialYears, y => y.StartDate.Date == nextYearStart && y.IsActive);
                Assert.Equal(1, db.FinancialYears.Count(y => y.StartDate.Date == nextYearStart));
                Assert.NotNull(db.SalesInvoiceHeaders.Single(i => i.Id == saved.GetProperty("id").GetInt32()).GeneralLedgerHeaderId);
            });
        }

        [Fact]
        public async Task OpeningBalance_DatedInLaterOpenYear_IsAccepted()
        {
            var later = DateTime.Parse("2027-06-01", CultureInfo.InvariantCulture);

            var created = await CreateBankOk(new CreateBankAccountRequest
            {
                Name = "Later Year OB",
                Type = 1,
                OpeningBalance = 250m,
                OpeningBalanceDate = later
            });

            Assert.Equal(250m, created.OpeningBalance);
            Assert.Equal(later.Date, created.OpeningBalanceDate!.Value.Date);
            WithDb(db => Assert.Contains(db.FinancialYears, y =>
                y.IsActive && y.StartDate.Date <= later.Date && y.EndDate.Date >= later.Date));
        }

        [Fact]
        public async Task EnsureYearEndYears_BackfillsThroughNextYear_Idempotently_WithoutReopeningClosedYears()
        {
            var today = DateTime.UtcNow.Date;
            string currentCode = null;
            WithDb(db =>
            {
                var setting = db.CompanySettings.Single();
                setting.BooksStartDate = new DateTime(today.Year - 3, 3, 1, 0, 0, 0, DateTimeKind.Utc);
                setting.FiscalYearPattern = Core.Domain.FiscalYearPattern.Calendar;
                var years = db.FinancialYears.ToList();
                db.FinancialYears.RemoveRange(years);
                currentCode = Core.Domain.FiscalYearCalculator.Containing(today, setting.FiscalYearPattern, null).Code;
                db.FinancialYears.Add(new Core.Domain.Financials.FinancialYear {
                    CompanyId = _companyId, FiscalYearCode = currentCode, FiscalYearName = currentCode,
                    StartDate = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(today.Year, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                    IsActive = false, ClosedAt = DateTime.UtcNow, ClosedBy = "reviewer"
                });
                db.SaveChanges();
            });
            var first = await Send(HttpMethod.Post, "/api/Financials/YearEnd/EnsureYears", new { });
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
            var second = await Send(HttpMethod.Post, "/api/Financials/YearEnd/EnsureYears", new { });
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            WithDb(db =>
            {
                var years = db.FinancialYears.OrderBy(y => y.StartDate).ToList();
                Assert.Equal(5, years.Count);
                Assert.Equal(today.Year - 3, years.First().StartDate.Year);
                Assert.Equal(today.Year + 1, years.Last().StartDate.Year);
                var closed = Assert.Single(years, y => y.FiscalYearCode == currentCode);
                Assert.False(closed.IsActive);
                Assert.NotNull(closed.ClosedAt);
            });
        }

        [Fact]
        public async Task EnsureYearEndYears_ConcurrentRequestsCreateEachYearOnce()
        {
            WithDb(db =>
            {
                var setting = db.CompanySettings.Single();
                setting.BooksStartDate = new DateTime(DateTime.UtcNow.Year - 2, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                setting.FiscalYearPattern = Core.Domain.FiscalYearPattern.Calendar;
                db.FinancialYears.RemoveRange(db.FinancialYears.ToList());
                db.SaveChanges();
            });
            var responses = await Task.WhenAll(
                Send(HttpMethod.Post, "/api/Financials/YearEnd/EnsureYears", new { }),
                Send(HttpMethod.Post, "/api/Financials/YearEnd/EnsureYears", new { }));
            foreach (var response in responses)
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            WithDb(db =>
            {
                var years = db.FinancialYears.ToList();
                Assert.Equal(4, years.Count);
                Assert.Equal(4, years.Select(y => y.FiscalYearCode).Distinct().Count());
            });
        }

        [Fact]
        public async Task FinancialYearsAndCurrentOrg_OnMissingCurrentYear_DoNotInsertRows()
        {
            var today = DateTime.UtcNow.Date;
            int countBefore = 0;
            string expectedCode = null;
            WithDb(db =>
            {
                var setting = db.CompanySettings.Single();
                var computed = Core.Domain.FiscalYearCalculator.Containing(
                    today, setting.FiscalYearPattern, setting.FiscalYearStartMonth);
                expectedCode = computed.Code;
                db.FinancialYears.RemoveRange(db.FinancialYears.Where(y => y.FiscalYearCode == expectedCode));
                db.SaveChanges();
                countBefore = db.FinancialYears.Count();
            });

            var yearsResponse = await Send(HttpMethod.Get, "/api/Administration/financialyears");
            Assert.True(yearsResponse.IsSuccessStatusCode, await yearsResponse.Content.ReadAsStringAsync());
            var years = await yearsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Array, years.ValueKind);
            Assert.Contains(years.EnumerateArray(), y =>
                y.GetProperty("fiscalYearCode").GetString() == expectedCode
                && y.GetProperty("isActive").GetBoolean());

            var currentResponse = await Send(HttpMethod.Get, "/api/organizations/current");
            Assert.True(currentResponse.IsSuccessStatusCode, await currentResponse.Content.ReadAsStringAsync());
            var current = await currentResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(current.TryGetProperty("fiscalYearEnd", out var fyEnd));
            Assert.False(string.IsNullOrEmpty(fyEnd.GetString()));

            WithDb(db => Assert.Equal(countBefore, db.FinancialYears.Count()));
        }
    }
}
