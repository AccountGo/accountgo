#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Dto.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    // #413 1C: Deposit-to / Pay-from resolve on RecordPayment and PayBill.
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task RecordPayment_WithBankAccountId_DebitsThatBanksAccount()
        {
            var first = await CreateBankOk(new CreateBankAccountRequest { Name = "Operating", Type = 1 });
            var second = await CreateBankOk(new CreateBankAccountRequest { Name = "Savings", Type = 1 });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto
                {
                    InvoiceId = invoiceId,
                    Amount = 100,
                    PaymentDate = DateTime.UtcNow,
                    BankAccountId = second.Id
                });
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var receipt = db.SalesReceiptHeaders.Include(r => r.AccountToDebit).Single();
                    Assert.Equal(second.AccountId, receipt.AccountToDebitId);
                    Assert.NotEqual(first.AccountId, receipt.AccountToDebitId);
                }
            }
        }

        [Fact]
        public async Task RecordPayment_RefusesArchivedBankAccountId()
        {
            await CreateBankOk(new CreateBankAccountRequest { Name = "Keep", Type = 1 });
            var gone = await CreateBankOk(new CreateBankAccountRequest { Name = "Gone", Type = 1 });
            var archive = await Send(HttpMethod.Post, $"/api/Banking/Accounts/{gone.Id}/Archive?confirm=true");
            Assert.True(archive.IsSuccessStatusCode, await archive.Content.ReadAsStringAsync());

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var refused = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto
                {
                    InvoiceId = invoiceId,
                    Amount = 10,
                    PaymentDate = DateTime.UtcNow,
                    BankAccountId = gone.Id
                });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("archived", await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RecordPayment_RefusesCrossTenantBankAccountId()
        {
            var ownerSlug = _slug;
            Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Other Co Bank Org", slug = "other-bank-org" }, token: _otherToken, scoped: false)).StatusCode);
            _slug = "other-bank-org";
            var otherBank = await CreateBankOk(new CreateBankAccountRequest { Name = "Other Co Bank", Type = 1 }, _otherToken);
            _slug = ownerSlug;

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var refused = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto
                {
                    InvoiceId = invoiceId,
                    Amount = 10,
                    PaymentDate = DateTime.UtcNow,
                    BankAccountId = otherBank.Id
                });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("CROSS_TENANT_REFERENCE_DENIED", (await Json(refused)).GetProperty("code").GetString());
        }

        [Fact]
        public async Task PayBill_WithBankAccountId_CreditsThatBanksAccount()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Pay From", Type = 1 });
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Bank Pay Vendor" });
            var bill = NewBill(vendor.Id, ("Supplies", 1, 50m, null));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 50m, BankAccountId = bank.Id, Date = DateTime.UtcNow }));
            var payment = Assert.Single(paid.Payments);
            Assert.Equal(bank.AccountId, payment.AccountId);
        }

        private async Task<BankAccountDto> CreateBankOk(CreateBankAccountRequest request, string token = null)
        {
            var response = await Send(HttpMethod.Post, "/api/Banking/Accounts", request, token);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<BankAccountDto>();
        }
    }
}
