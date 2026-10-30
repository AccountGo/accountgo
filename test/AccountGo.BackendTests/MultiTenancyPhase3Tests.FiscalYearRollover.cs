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
    }
}
