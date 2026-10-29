#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Dto.Banking;
using Xunit;

namespace AccountGo.BackendTests
{
    // #413 1D: register and opening-balance posting.
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Register_ShowsReceiptAndBillPayment_WithRunningBalance()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest
            {
                Name = "Register Bank",
                Type = 1,
                OpeningBalance = 1000m,
                OpeningBalanceDate = DateTime.Parse("2026-01-01", CultureInfo.InvariantCulture)
            });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto
                {
                    InvoiceId = invoiceId,
                    Amount = 100m,
                    PaymentDate = DateTime.Parse("2026-01-10", CultureInfo.InvariantCulture),
                    BankAccountId = bank.Id
                })).IsSuccessStatusCode);

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Register Vendor" });
            var bill = NewBill(vendor.Id, ("Widget", 1, 40m, null));
            bill.Status = "Open";
            // Bill date is "today"; pay on/after that date (InvariantCulture fixed dates can precede today).
            var payDate = DateTime.UtcNow.Date;
            var approved = await SaveBillOk(bill);
            await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill
                {
                    BillId = approved.Id,
                    Amount = 40m,
                    Date = payDate,
                    BankAccountId = bank.Id
                }));

            var register = await (await Send(HttpMethod.Get,
                $"/api/Banking/Accounts/{bank.Id}/Register?from=2026-01-01&page=1&pageSize=50"))
                .Content.ReadFromJsonAsync<BankRegisterDto>();

            Assert.Equal(0m, register.BroughtForward);
            Assert.True(register.TotalCount >= 3, $"expected OB + receipt + payment, got {register.TotalCount}");
            Assert.Contains(register.Lines, l => l.Type == "Opening balance" && l.In == 1000m);
            Assert.Contains(register.Lines, l => l.Type == "Receipt" && l.In == 100m);
            Assert.Contains(register.Lines, l => l.Type == "Bill payment" && l.Out == 40m);
            Assert.Equal(1060m, register.Lines.Last().Balance);
        }

        [Fact]
        public async Task OpeningBalance_BeforeBooksStart_IsRefused()
        {
            DateTime booksStart = default;
            WithDb(db => booksStart = db.CompanySettings.Single().BooksStartDate.Date);

            var refused = await Send(HttpMethod.Post, "/api/Banking/Accounts", new CreateBankAccountRequest
            {
                Name = "Too Early",
                Type = 1,
                OpeningBalance = 10m,
                OpeningBalanceDate = booksStart.AddDays(-1)
            });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("books start", await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

    }
}
