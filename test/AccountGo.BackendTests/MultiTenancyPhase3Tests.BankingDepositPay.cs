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
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    // #413 1C: Deposit-to / Pay-from resolve on RecordPayment and PayBill.
    public abstract partial class MultiTenancyPhase3TestBase
    {

        protected async Task<BankAccountDto> CreateBankOk(CreateBankAccountRequest request, string token = null)
        {
            var response = await Send(HttpMethod.Post, "/api/Banking/Accounts", request, token);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await ReadDto<BankAccountDto>(response);
        }
    }

    public sealed class MultiTenancyPhase3BankingDepositPayTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3BankingDepositPayTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task RecordPayment_WithBankAccountId_DebitsThatBanksAccount()
        {
            var first = await CreateBankOk(new CreateBankAccountRequest { Name = "Operating", Type = 1 });
            var second = await CreateBankOk(new CreateBankAccountRequest { Name = "Savings", Type = 1 });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = IntId<Core.Domain.Sales.SalesInvoiceHeader>((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());

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
            var archive = await Send(HttpMethod.Post, $"/api/Banking/Accounts/{PubId<Core.Domain.Financials.Bank>(gone.Id)}/Archive?confirm=true");
            Assert.True(archive.IsSuccessStatusCode, await archive.Content.ReadAsStringAsync());

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = IntId<Core.Domain.Sales.SalesInvoiceHeader>((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());

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
            var createOtherOrg = await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Other Co Bank Org", slug = "other-bank-org" }, token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.Created, createOtherOrg.StatusCode);
            var otherCompanyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(_factory.Services, (await Json(createOtherOrg)).GetProperty("id").GetString());
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var otherMemberId = db.CompanyMembers.Single(m => m.CompanyId == otherCompanyId && m.UserId == _otherId).Id;
                var accountantRoleId = db.SecurityRoles.Single(r => r.Name == "Accountant").Id;
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = otherMemberId, SecurityRoleId = accountantRoleId });
                await db.SaveChangesAsync();
            }
            _slug = "other-bank-org";
            var otherBankResp = await Send(HttpMethod.Post, "/api/Banking/Accounts", new CreateBankAccountRequest { Name = "Other Co Bank", Type = 1 }, _otherToken);
            Assert.True(otherBankResp.IsSuccessStatusCode, await otherBankResp.Content.ReadAsStringAsync());
            var otherBankPub = (await Json(otherBankResp)).GetProperty("id").GetString();
            _slug = ownerSlug;

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoicePub = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

            var refused = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new
                {
                    invoiceId = invoicePub,
                    amount = 10,
                    paymentDate = DateTime.UtcNow,
                    bankAccountId = otherBankPub
                });
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal("unknown_id", (await Json(refused)).GetProperty("code").GetString());
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
    }
}
