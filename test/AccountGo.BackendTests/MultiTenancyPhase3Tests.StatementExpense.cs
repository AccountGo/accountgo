#nullable disable
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    // Recording an expense from a statement line, against the real database (foreign keys enforced).
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task RecordExpense_FromStatementLine_BooksGeneralItem_AndCreatesVendorOnce()
        {
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Wise", Type = 1 });
            var first = AddStatementPayment(bank.Id, -38.71m, "Porkbun", DateTime.UtcNow.Date);
            var second = AddStatementPayment(bank.Id, -12.00m, "PORKBUN", DateTime.UtcNow.Date);

            var categories = await Send(HttpMethod.Get, "/api/Banking/StatementExpenseCategories");
            Assert.True(categories.IsSuccessStatusCode, await categories.Content.ReadAsStringAsync());
            var catalog = await categories.Content.ReadFromJsonAsync<StatementExpenseCategoriesDto>();
            var expenseAccountId = catalog.Curated.Concat(catalog.More).First().AccountId;

            foreach (var (lineId, name) in new[] { (first, "Porkbun"), (second, "PORKBUN") })
            {
                var recorded = await Send(HttpMethod.Post, $"/api/Banking/StatementLines/{lineId}/RecordExpense",
                    new RecordExpenseFromStatementLineRequest
                    {
                        NewVendor = new QuickVendorFromStatementRequest { Name = name },
                        ExpenseAccountId = expenseAccountId
                    });
                Assert.True(recorded.IsSuccessStatusCode, await recorded.Content.ReadAsStringAsync());
            }

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var vendor = db.Vendors.Include(v => v.Party).Include(v => v.VendorContact)
                    .Single(v => v.Party.Name.ToLower() == "porkbun");
                Assert.Equal("Porkbun", vendor.Party.Name);
                Assert.Contains(vendor.VendorContact, vc => vc.ContactId == vendor.PrimaryContactId);

                var bills = db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).ThenInclude(l => l.Item).ToList();
                Assert.Equal(2, bills.Count);
                Assert.All(bills, b => Assert.Equal(vendor.Id, b.VendorId));
                Assert.All(bills.SelectMany(b => b.PurchaseInvoiceLines), l =>
                {
                    Assert.Equal("Service / Product", l.Item.Description);
                    Assert.Equal(expenseAccountId, l.AccountId);
                });
                Assert.Equal(1, db.Items.Count(i => i.Description == "Service / Product"));
                Assert.All(db.BankStatementLines.Where(l => l.Id == first || l.Id == second),
                    l => Assert.NotNull(l.MatchedGeneralLedgerLineId));
            }
        }

        private int AddStatementPayment(int bankId, decimal amount, string payee, DateTime posted)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var import = new BankStatementImport
                {
                    BankId = bankId,
                    Source = BankStatementSources.Csv,
                    FileName = "wise.csv",
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "test"
                };
                db.Add(import);
                db.SaveChanges();
                var line = new BankStatementLine
                {
                    BankId = bankId,
                    BankStatementImportId = import.Id,
                    Source = BankStatementSources.Csv,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    PostedDate = posted,
                    Amount = amount,
                    Payee = payee
                };
                db.Add(line);
                db.SaveChanges();
                return line.Id;
            }
        }
    }
}
