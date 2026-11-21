#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Domain.Sales;
using Dto.Banking;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    public sealed class MultiTenancyPhase3FiscalYearRolloverTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3FiscalYearRolloverTests(ITestOutputHelper output) : base(output) { }

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
                Assert.NotNull(db.SalesInvoiceHeaders.Single(i => i.Id == IntId<SalesInvoiceHeader>(saved.GetProperty("id").GetString())).GeneralLedgerHeaderId);
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

        [Fact]
        public async Task FinancialYears_ExposeCloseStatusWithoutInsertingAYear()
        {
            var today = DateTime.UtcNow.Date;
            int countBefore = 0;
            string currentCode = null;
            WithDb(db =>
            {
                var setting = db.CompanySettings.Single();
                setting.Timezone = "UTC";
                var computed = Core.Domain.FiscalYearCalculator.Containing(
                    today, setting.FiscalYearPattern, setting.FiscalYearStartMonth);
                currentCode = computed.Code;
                db.FinancialYears.RemoveRange(db.FinancialYears.Where(y => y.FiscalYearCode == currentCode));
                db.FinancialYears.Add(new Core.Domain.Financials.FinancialYear
                {
                    FiscalYearCode = "FY-CLOSED0",
                    FiscalYearName = "Zero activity",
                    StartDate = new DateTime(2019, 1, 1),
                    EndDate = new DateTime(2019, 12, 31),
                    IsActive = false,
                    ClosedAt = new DateTime(2020, 1, 15, 18, 30, 0, DateTimeKind.Utc),
                    ClosedBy = "ada@example.test",
                    ClosingGeneralLedgerHeaderId = null
                });
                db.FinancialYears.Add(new Core.Domain.Financials.FinancialYear
                {
                    FiscalYearCode = "FY21-22",
                    FiscalYearName = "Partial year",
                    StartDate = new DateTime(2021, 4, 1),
                    EndDate = new DateTime(2022, 3, 31),
                    IsActive = true
                });
                db.SaveChanges();
                countBefore = db.FinancialYears.Count();
            });

            var response = await Send(HttpMethod.Get, "/api/Administration/financialyears");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var years = await response.Content.ReadFromJsonAsync<JsonElement>();
            var closed = Row(years, "FY-CLOSED0");
            Assert.Equal("Closed", closed.GetProperty("status").GetString());
            Assert.True(closed.GetProperty("isPersisted").GetBoolean());
            Assert.False(closed.GetProperty("isActive").GetBoolean());
            Assert.Equal("2019-01-01", closed.GetProperty("startDate").GetString());
            Assert.Equal("2019-12-31", closed.GetProperty("endDate").GetString());
            Assert.Equal("ada@example.test", closed.GetProperty("closedBy").GetString());
            Assert.Equal(JsonValueKind.Null, closed.GetProperty("closingGeneralLedgerHeaderId").ValueKind);
            Assert.EndsWith("+00:00", closed.GetProperty("closedAt").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("2020-01-15T18:30:00", closed.GetProperty("closedAt").GetString(), StringComparison.Ordinal);

            var partial = Row(years, "FY21-22");
            Assert.Equal("Ended", partial.GetProperty("status").GetString());
            Assert.True(partial.GetProperty("isActive").GetBoolean());
            Assert.Equal("2021-04-01", partial.GetProperty("startDate").GetString());

            var current = Row(years, currentCode);
            Assert.False(current.GetProperty("isPersisted").GetBoolean());
            Assert.Equal(JsonValueKind.Null, current.GetProperty("id").ValueKind);
            Assert.Equal("Open", current.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, current.GetProperty("closedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, current.GetProperty("closedBy").ValueKind);
            Assert.Equal(JsonValueKind.Null, current.GetProperty("closingGeneralLedgerHeaderId").ValueKind);

            var yearEnd = await Json(await Send(HttpMethod.Get, "/api/Financials/YearEnd"));
            Assert.Equal(3, Row(yearEnd, "FY-CLOSED0").GetProperty("status").GetInt32());
            Assert.Equal(2, Row(yearEnd, "FY21-22").GetProperty("status").GetInt32());
            Assert.DoesNotContain(yearEnd.EnumerateArray(), y => y.GetProperty("fiscalYearCode").GetString() == currentCode);

            var tool = await CallTool("get_fiscal_years", new { });
            Assert.Equal("Success", tool.GetProperty("status").GetString());
            Assert.True(JsonElement.DeepEquals(years, tool.GetProperty("data")));

            await JoinOther();
            var member = await CallTool("get_fiscal_years", new { }, _otherId);
            Assert.Equal("Success", member.GetProperty("status").GetString());
            Assert.True(JsonElement.DeepEquals(years, member.GetProperty("data")));

            WithDb(db => Assert.Equal(countBefore, db.FinancialYears.Count()));
        }

        private static JsonElement Row(JsonElement years, string code) =>
            Assert.Single(years.EnumerateArray(), y => y.GetProperty("fiscalYearCode").GetString() == code);
    }
}
