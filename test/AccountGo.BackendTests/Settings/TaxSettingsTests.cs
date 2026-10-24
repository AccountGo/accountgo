#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Settings
{
    public class TaxSettingsTests : IClassFixture<TaxSettingsTests.Fixture>
    {
        private readonly Fixture _fixture;
        public TaxSettingsTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public async Task CreateListEditArchiveAndDelete()
        {
            var owner = await _fixture.CreateUser("tax-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Tax Co", "tax-co");

            var created = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "VAT", taxCode = "vat", rate = 7 }, owner.Token, "tax-co");
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var tax = await Json(created);
            Assert.Equal("VAT", tax.GetProperty("taxCode").GetString());
            Assert.Equal(7, tax.GetProperty("rate").GetDecimal());
            Assert.True(tax.GetProperty("isActive").GetBoolean());
            Assert.False(tax.GetProperty("inUse").GetBoolean());
            Assert.Equal("20300", tax.GetProperty("salesAccountCode").GetString());
            Assert.Equal("10160", tax.GetProperty("purchaseAccountCode").GetString());
            var id = tax.GetProperty("id").GetInt32();

            var list = await Json(await _fixture.Send(HttpMethod.Get, "/api/Tax", token: owner.Token, org: "tax-co"));
            Assert.Contains(list.EnumerateArray(), row => row.GetProperty("taxCode").GetString() == "VAT");
            Assert.Contains(list.EnumerateArray(), row => row.GetProperty("isSystem").GetBoolean() && row.GetProperty("taxCode").GetString() == "NONE");

            var edited = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "VAT", taxCode = "VAT", rate = 7.5, isActive = true }, owner.Token, "tax-co");
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal(7.5m, (await Json(edited)).GetProperty("rate").GetDecimal());

            var archived = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "VAT", taxCode = "VAT", rate = 7.5, isActive = false }, owner.Token, "tax-co");
            Assert.False((await Json(archived)).GetProperty("isActive").GetBoolean());
            var legacy = await Json(await _fixture.Send(HttpMethod.Get, "/api/Tax/Taxes", token: owner.Token, org: "tax-co"));
            var legacyRow = legacy.GetProperty("taxes").EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == id);
            Assert.False(legacyRow.GetProperty("isActive").GetBoolean());

            var restored = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "VAT", taxCode = "VAT", rate = 7.5, isActive = true }, owner.Token, "tax-co");
            Assert.True((await Json(restored)).GetProperty("isActive").GetBoolean());
            Assert.Equal(HttpStatusCode.NoContent, (await _fixture.Send(HttpMethod.Delete, "/api/Tax/" + id, token: owner.Token, org: "tax-co")).StatusCode);
            var after = await Json(await _fixture.Send(HttpMethod.Get, "/api/Tax", token: owner.Token, org: "tax-co"));
            Assert.DoesNotContain(after.EnumerateArray(), row => row.GetProperty("id").GetInt32() == id);
        }

        [Fact]
        public async Task OtherWorkspaceCannotSeeOrChangeTheTax_AndCannotUseItsAccount()
        {
            var owner = await _fixture.CreateUser("iso-owner@example.com");
            var companyA = await _fixture.CreateWorkspace(owner.Token, "Alpha", "tax-alpha");
            await _fixture.CreateWorkspace(owner.Token, "Beta", "tax-beta");
            var created = await Json(await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Alpha VAT", taxCode = "AVAT", rate = 10 }, owner.Token, "tax-alpha"));
            var id = created.GetProperty("id").GetInt32();
            int foreignAccount;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                foreignAccount = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == companyA && a.AccountCode == "20300").Id;
            }

            var listed = await Json(await _fixture.Send(HttpMethod.Get, "/api/Tax", token: owner.Token, org: "tax-beta"));
            Assert.DoesNotContain(listed.EnumerateArray(), row => row.GetProperty("id").GetInt32() == id);
            Assert.Equal(HttpStatusCode.NotFound, (await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "Alpha VAT", taxCode = "AVAT", rate = 11, isActive = true }, owner.Token, "tax-beta")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _fixture.Send(HttpMethod.Delete, "/api/Tax/" + id, token: owner.Token, org: "tax-beta")).StatusCode);

            var rejected = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Beta VAT", taxCode = "BVAT", rate = 10, salesAccountId = foreignAccount }, owner.Token, "tax-beta");
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("not in this workspace", (await Json(rejected)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task DuplicateNameOrCodeIsRejected_AndTheSameCodeCanBeKeptOnEdit()
        {
            var owner = await _fixture.CreateUser("dup-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Dup", "tax-dup");
            var created = await Json(await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Goods Tax", taxCode = "gst", rate = 5 }, owner.Token, "tax-dup"));
            var id = created.GetProperty("id").GetInt32();

            var sameName = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "goods tax", taxCode = "GST2", rate = 5 }, owner.Token, "tax-dup");
            Assert.Equal(HttpStatusCode.BadRequest, sameName.StatusCode);
            Assert.Contains("already exists", (await Json(sameName)).GetProperty("message").GetString());
            var sameCode = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Other", taxCode = "GST", rate = 5 }, owner.Token, "tax-dup");
            Assert.Equal(HttpStatusCode.BadRequest, sameCode.StatusCode);

            var kept = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "Goods Tax", taxCode = "gst", rate = 6, isActive = true }, owner.Token, "tax-dup");
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
            Assert.Equal(6, (await Json(kept)).GetProperty("rate").GetDecimal());
        }

        [Fact]
        public async Task SalesAccountMustBeALiability_AndPurchaseAccountAnAssetOrExpense()
        {
            var owner = await _fixture.CreateUser("acct-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Accounts", "tax-acct");
            int revenue, liabilityHeader, assetHeader, expense;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                int Id(string code) => db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == companyId && a.AccountCode == code).Id;
                revenue = Id("40100");
                liabilityHeader = Id("20100");
                assetHeader = Id("10100");
                expense = Id("50700");
            }

            var sales = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Bad sales", taxCode = "BADS", rate = 1, salesAccountId = revenue }, owner.Token, "tax-acct");
            Assert.Equal(HttpStatusCode.BadRequest, sales.StatusCode);
            Assert.Contains("liability", (await Json(sales)).GetProperty("message").GetString());
            var header = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Header sales", taxCode = "HEADS", rate = 1, salesAccountId = liabilityHeader }, owner.Token, "tax-acct");
            Assert.Equal(HttpStatusCode.BadRequest, header.StatusCode);
            Assert.Contains("posting account", (await Json(header)).GetProperty("message").GetString());
            var purchase = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Bad purchase", taxCode = "BADP", rate = 1, purchaseAccountId = liabilityHeader }, owner.Token, "tax-acct");
            Assert.Equal(HttpStatusCode.BadRequest, purchase.StatusCode);
            Assert.Contains("asset or an expense", (await Json(purchase)).GetProperty("message").GetString());
            var purchaseHeader = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Header purchase", taxCode = "HEADP", rate = 1, purchaseAccountId = assetHeader }, owner.Token, "tax-acct");
            Assert.Equal(HttpStatusCode.BadRequest, purchaseHeader.StatusCode);

            var ok = await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Expense purchase", taxCode = "EXPP", rate = 1, purchaseAccountId = expense }, owner.Token, "tax-acct");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("50700", (await Json(ok)).GetProperty("purchaseAccountCode").GetString());
        }

        [Fact]
        public async Task InUseTaxCannotBeDeleted_ArchiveWorks_SystemTaxIsReadOnly()
        {
            var owner = await _fixture.CreateUser("use-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "In Use", "tax-use");
            var created = await Json(await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Used", taxCode = "USED", rate = 12 }, owner.Token, "tax-use"));
            var id = created.GetProperty("id").GetInt32();
            var invoice = await NewInvoice(companyId, "tax-use");
            invoice.SalesInvoiceLines.Single().TaxId = id;
            invoice.Status = "Open";
            var saved = await _fixture.Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice, owner.Token, "tax-use");
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var issued = await saved.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            var taxAmount = issued.SalesInvoiceLines.Single().TaxAmount;
            var total = issued.TotalAmountAfterTax;
            Assert.Equal(12m, taxAmount);

            var blocked = await _fixture.Send(HttpMethod.Delete, "/api/Tax/" + id, token: owner.Token, org: "tax-use");
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("This tax is used on invoices or bills. Archive it instead.", (await Json(blocked)).GetProperty("message").GetString());

            var archived = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "Used", taxCode = "USED", rate = 20, isActive = false }, owner.Token, "tax-use");
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
            var again = await _fixture.Send(HttpMethod.Get, "/api/Sales/SalesInvoice?id=" + issued.Id, token: owner.Token, org: "tax-use");
            var reread = await again.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            Assert.Equal(taxAmount, reread.SalesInvoiceLines.Single().TaxAmount);
            Assert.Equal(total, reread.TotalAmountAfterTax);
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.Equal(20m, db.Taxes.IgnoreQueryFilters().Single(t => t.Id == id).Rate);
                Assert.Equal(taxAmount, db.SalesInvoiceLines.IgnoreQueryFilters().Single(l => l.TaxId == id).TaxAmount);
            }

            var systemId = (await Json(await _fixture.Send(HttpMethod.Get, "/api/Tax", token: owner.Token, org: "tax-use")))
                .EnumerateArray().Single(row => row.GetProperty("taxCode").GetString() == "NONE").GetProperty("id").GetInt32();
            var editSystem = await _fixture.Send(HttpMethod.Put, "/api/Tax/" + systemId, new { taxName = "Changed", taxCode = "NONE", rate = 1, isActive = true }, owner.Token, "tax-use");
            var deleteSystem = await _fixture.Send(HttpMethod.Delete, "/api/Tax/" + systemId, token: owner.Token, org: "tax-use");
            Assert.Equal(HttpStatusCode.BadRequest, editSystem.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, deleteSystem.StatusCode);
            Assert.Equal("The system 'No tax' rate can't be changed.", (await Json(editSystem)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task MemberWithoutPermissionIsForbidden_AccountantHasItByDefault()
        {
            var owner = await _fixture.CreateUser("perm-owner@example.com");
            var member = await _fixture.CreateUser("perm-member@example.com");
            var accountant = await _fixture.CreateUser("perm-accountant@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Perm", "tax-perm");
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var roleId = db.SecurityRoles.Single(r => r.Name == "Accountant").Id;
                Assert.Contains(db.SecurityRolePermissions.Where(rp => rp.SecurityRoleId == roleId).Select(rp => rp.SecurityPermission.Name), name => name == SystemPermissions.AdminTaxesManage);
                db.CompanyMembers.Add(new CompanyMember { CompanyId = companyId, UserId = member.Id, Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow });
                var accountantMember = new CompanyMember { CompanyId = companyId, UserId = accountant.Id, Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow };
                accountantMember.SecurityRoles.Add(new CompanyMemberSecurityRole { SecurityRoleId = roleId });
                db.CompanyMembers.Add(accountantMember);
                db.SaveChanges();
            }

            Assert.Equal(HttpStatusCode.OK, (await _fixture.Send(HttpMethod.Get, "/api/Tax", token: member.Token, org: "tax-perm")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Nope", taxCode = "NOPE", rate = 1 }, member.Token, "tax-perm")).StatusCode);
            var created = await Json(await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "Books", taxCode = "BOOK", rate = 3 }, accountant.Token, "tax-perm"));
            var id = created.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Put, "/api/Tax/" + id, new { taxName = "Books", taxCode = "BOOK", rate = 4, isActive = true }, member.Token, "tax-perm")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Delete, "/api/Tax/" + id, token: member.Token, org: "tax-perm")).StatusCode);
        }

        [Fact]
        public async Task NewWorkspaceSeedsInputTaxAndPointsNoTaxAtIt()
        {
            var owner = await _fixture.CreateUser("seed-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Seed", "tax-seed");
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var input = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == companyId && a.AccountCode == "10160");
            Assert.Equal("Input Tax Receivable", input.AccountName);
            Assert.Equal((int)AccountClasses.Assets, input.AccountClassId);
            var none = db.Taxes.IgnoreQueryFilters().Single(t => t.CompanyId == companyId && t.TaxCode == "NONE");
            Assert.Equal(input.Id, none.PurchasingAccountId);
            Assert.Equal("20300", db.Accounts.IgnoreQueryFilters().Single(a => a.Id == none.SalesAccountId).AccountCode);
            Assert.Contains(db.Accounts.IgnoreQueryFilters(), a => a.AccountCode == "50700");
        }

        [Fact]
        public void Migration_AddsTheAccountPermissionAndRepointsNoTax_AndIsIdempotent()
        {
            using var source = new SqliteConnection("Data Source=" + _fixture.Path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
            using var command = replica.CreateCommand();
            command.CommandText = @"
                UPDATE ""Tax"" SET ""PurchasingAccountId"" = (
                    SELECT old.""Id"" FROM ""Account"" old
                    WHERE old.""CompanyId"" = ""Tax"".""CompanyId"" AND old.""AccountCode"" = '50700'
                ) WHERE ""PurchasingAccountId"" IN (SELECT ""Id"" FROM ""Account"" WHERE ""AccountCode"" = '10160');
                DELETE FROM ""Account"" WHERE ""AccountCode"" = '10160';
                DELETE FROM ""SecurityRolePermission"" WHERE ""SecurityPermissionId"" IN
                    (SELECT ""Id"" FROM ""SecurityPermission"" WHERE ""Name"" = 'admin.taxes.manage');
                DELETE FROM ""SecurityPermission"" WHERE ""Name"" = 'admin.taxes.manage';";
            command.ExecuteNonQuery();

            var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0020_tax_settings.sql"));
            command.CommandText = migration;
            command.ExecuteNonQuery();
            AssertMigrated(command);
            command.CommandText = migration;
            command.ExecuteNonQuery();
            AssertMigrated(command);
        }

        private static void AssertMigrated(SqliteCommand command)
        {
            command.CommandText = @"SELECT COUNT(*) FROM ""Company"" c
                WHERE NOT EXISTS (SELECT 1 FROM ""Account"" a WHERE a.""CompanyId"" = c.""Id"" AND a.""AccountCode"" = '10160')";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = @"SELECT COUNT(*) FROM ""Account"" WHERE ""AccountCode"" = '10160' GROUP BY ""CompanyId"" HAVING COUNT(*) > 1";
            Assert.Null(command.ExecuteScalar());
            command.CommandText = @"SELECT COUNT(*) FROM ""Tax"" t
                WHERE t.""TaxCode"" = 'NONE' AND t.""PurchasingAccountId"" != (
                    SELECT a.""Id"" FROM ""Account"" a WHERE a.""CompanyId"" = t.""CompanyId"" AND a.""AccountCode"" = '10160')";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = @"SELECT COUNT(*) FROM ""SecurityPermission"" WHERE ""Name"" = 'admin.taxes.manage' AND ""DisplayName"" = 'Manage Taxes'";
            Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = @"SELECT COUNT(*) FROM ""SecurityRole"" r
                JOIN ""SecurityRolePermission"" hold ON hold.""SecurityRoleId"" = r.""Id""
                JOIN ""SecurityPermission"" accounts ON accounts.""Id"" = hold.""SecurityPermissionId"" AND accounts.""Name"" = 'financials.accounts.manage'
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""SecurityRolePermission"" grant
                    JOIN ""SecurityPermission"" tax ON tax.""Id"" = grant.""SecurityPermissionId""
                    WHERE grant.""SecurityRoleId"" = r.""Id"" AND tax.""Name"" = 'admin.taxes.manage')";
            Assert.Equal(0L, command.ExecuteScalar());
        }

        private async Task<Dto.Sales.SalesInvoice> NewInvoice(int companyId, string slug)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, slug, CompanyMemberRole.Owner))
            {
                var sales = db.Accounts.Single(a => a.AccountCode == "40100");
                var customer = new Customer
                {
                    No = Guid.NewGuid().ToString("N"),
                    Party = new Party { Name = "Tax customer", PartyType = PartyTypes.Customer },
                    PrimaryContact = new Contact { FirstName = "Tax", LastName = "Customer", Party = new Party { Name = "Tax Customer", PartyType = PartyTypes.Contact } },
                    AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"),
                    SalesAccount = sales
                };
                var item = new Item
                {
                    No = Guid.NewGuid().ToString("N"), Code = "TAXED", Description = "Taxed service", Price = 100,
                    SmallestMeasurementId = db.Measurements.First().Id, SalesAccount = sales
                };
                db.Customers.Add(customer);
                db.Items.Add(item);
                await db.SaveChangesAsync();
                return new Dto.Sales.SalesInvoice
                {
                    CustomerId = customer.Id,
                    InvoiceDate = DateTime.Parse("2026-10-24", CultureInfo.InvariantCulture),
                    Status = "Open",
                    SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
                    {
                        new() { ItemId = item.Id, Quantity = 1, Amount = 100, MeasurementId = item.SmallestMeasurementId.Value }
                    }
                };
            }
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Api", "d1")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        public class Fixture : IAsyncLifetime
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "accountgo_tax_" + Guid.NewGuid().ToString("N") + ".db");
            public WebApplicationFactory<Program> Factory { get; private set; }
            private HttpClient _client;

            public async Task InitializeAsync()
            {
                Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting("DatabaseProvider", "Sqlite");
                    builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + Path);
                    builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                    builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                });
                _client = Factory.CreateClient();
                await Task.CompletedTask;
            }

            public Task DisposeAsync()
            {
                _client.Dispose();
                Factory.Dispose();
                foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
                    if (File.Exists(file)) File.Delete(file);
                return Task.CompletedTask;
            }

            public async Task<UserSession> CreateUser(string email)
            {
                using var scope = Factory.Services.CreateScope();
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                Assert.True((await manager.CreateAsync(user)).Succeeded, email);
                var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
                return new UserSession(token.AccessToken, user.Id);
            }

            public async Task<int> CreateWorkspace(string token, string name, string slug)
            {
                var response = await Send(HttpMethod.Post, "/api/organizations", new { name, slug }, token, scoped: false);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                return int.Parse((await Json(response)).GetProperty("id").GetString(), CultureInfo.InvariantCulture);
            }

            public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object body = null, string token = null, string org = null, bool scoped = true)
            {
                using var request = new HttpRequestMessage(method, path);
                if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (scoped) request.Headers.Add("X-AccountGo-Org", org ?? "tax-co");
                if (body != null) request.Content = JsonContent.Create(body);
                return await _client.SendAsync(request);
            }
        }

        public sealed record UserSession(string Token, string Id);
    }
}
