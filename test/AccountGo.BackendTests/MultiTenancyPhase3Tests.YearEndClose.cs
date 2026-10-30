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
    /// <summary>Phase 2b-1 of #455: year-end close HTTP flow (real SQLite).</summary>
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task YearEndClose_PreviewCloseLockReopen_AndEntityTypeBlockedWhileClosed()
        {
            var fyEnd = DateTime.UtcNow.Date.AddDays(-1);
            var fyStart = new DateTime(fyEnd.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var fyCode = "FY" + fyEnd.Year.ToString(CultureInfo.InvariantCulture);
            var midYear = fyStart.AddMonths(5);

            WithDb(db =>
            {
                var fy = db.FinancialYears.Single(y => y.FiscalYearCode == fyCode);
                fy.EndDate = fyEnd;
                db.SaveChanges();
            });

            var invoice = await NewInvoice();
            invoice.InvoiceDate = midYear;
            invoice.Status = "Open";
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice)).IsSuccessStatusCode);

            var preview = await Send(HttpMethod.Get, $"/api/Financials/YearEnd/{fyCode}");
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
            var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, previewBody.GetProperty("status").GetInt32()); // Ended

            var closeRefused = await Send(HttpMethod.Post, $"/api/Financials/YearEnd/{fyCode}/Close",
                new { acknowledgeWarnings = false });
            if (closeRefused.StatusCode == HttpStatusCode.BadRequest)
            {
                var msg = await closeRefused.Content.ReadAsStringAsync();
                if (msg.Contains("Acknowledge", StringComparison.OrdinalIgnoreCase))
                {
                    closeRefused = await Send(HttpMethod.Post, $"/api/Financials/YearEnd/{fyCode}/Close",
                        new { acknowledgeWarnings = true });
                }
            }

            Assert.True(closeRefused.IsSuccessStatusCode, await closeRefused.Content.ReadAsStringAsync());
            var closedBody = await closeRefused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, closedBody.GetProperty("status").GetInt32()); // Closed

            WithDb(db =>
            {
                Assert.Equal(fyEnd, db.CompanySettings.Single().BooksLockedThrough!.Value.Date);
                Assert.NotNull(db.FinancialYears.Single(fy => fy.FiscalYearCode == fyCode).ClosedAt);
            });

            var lockedInvoice = await NewInvoice();
            lockedInvoice.InvoiceDate = midYear;
            lockedInvoice.Status = "Open";
            var refused = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", lockedInvoice);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(LockMessage(fyEnd), await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var entityChange = await Send(HttpMethod.Put, "/api/organizations/current/accounting-policy",
                new { entityType = EntityType.Partnership });
            Assert.Equal(HttpStatusCode.Conflict, entityChange.StatusCode);
            Assert.Contains("entity type", await entityChange.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            var reopen = await Send(HttpMethod.Post, $"/api/Financials/YearEnd/{fyCode}/Reopen",
                new { reason = "Adjust year-end accrual" });
            Assert.True(reopen.IsSuccessStatusCode, await reopen.Content.ReadAsStringAsync());

            WithDb(db =>
            {
                Assert.Null(db.CompanySettings.Single().BooksLockedThrough);
                Assert.Null(db.FinancialYears.Single(fy => fy.FiscalYearCode == fyCode).ClosedAt);
            });

            var afterReopen = await NewInvoice();
            afterReopen.InvoiceDate = midYear;
            afterReopen.Status = "Open";
            var ok = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", afterReopen);
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        }
    }
}
