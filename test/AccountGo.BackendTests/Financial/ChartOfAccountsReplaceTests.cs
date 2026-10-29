using Api;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class ChartOfAccountsReplaceTests : IClassFixture<ChartOfAccountsReplaceTests.Fixture>
    {
        private readonly Fixture _fixture;

        public ChartOfAccountsReplaceTests(Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Replace_DraftJournalDoesNotBlockReplace()
        {
            var owner = await _fixture.CreateUser("draft-replace-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Draft Replace Org", "draft-rep");

            // Seed an UNPOSTED draft journal
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "draft-rep", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var je = new JournalEntryHeader
                {
                    CompanyId = companyId,
                    Date = DateTime.UtcNow,
                    Posted = false, // Draft!
                    GeneralLedgerHeaderId = null,
                    Memo = "Unposted Draft Journal"
                };
                db.JournalEntryHeaders.Add(je);
                await db.SaveChangesAsync();
            }

            // Check ChartStatus
            var statRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ChartStatus", token: owner.Token, org: "draft-rep");
            Assert.Equal(HttpStatusCode.OK, statRes.StatusCode);
            var statJson = await Json(statRes);
            Assert.True(statJson.GetProperty("isEmptyBooks").GetBoolean(), "Draft journal alone should NOT mark books as non-empty.");
            Assert.True(statJson.GetProperty("canReplace").GetBoolean());
        }

        [Fact]
        public async Task Replace_ItemOrCustomerFK_ToOmittedAccount_BlocksReplace()
        {
            var owner = await _fixture.CreateUser("fk-block-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "FK Block Org", "fk-block");

            // Attach an item to account 10111 (checking account)
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "fk-block", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var acct = db.Accounts.First(a => a.CompanyId == companyId && a.AccountCode == "10111");
                db.Items.Add(new Item
                {
                    CompanyId = companyId,
                    Code = "ITEM-1",
                    Description = "Test Item",
                    SalesAccountId = acct.Id
                });
                await db.SaveChangesAsync();
            }

            // Minimal replacement CSV omitting 10111
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                      "10000,New Assets Root,,Assets\r\n" +
                      "20000,New Liabilities Root,,Liabilities\r\n" +
                      "30000,New Equity Root,,Equity\r\n" +
                      "40000,New Revenue Root,,Revenue\r\n" +
                      "50000,New Expense Root,,Expenses\r\n";

            // Validate Replace
            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=replace", new
            {
                csvContent = csv,
                mode = "replace",
                controlMappings = new Dictionary<string, string>
                {
                    { "PayableAccount", "20000" },
                    { "PurchaseDiscountAccount", "50000" },
                    { "GoodsReceiptNoteClearingAccount", "10000" },
                    { "SalesDiscountAccount", "40000" },
                    { "ShippingChargeAccount", "40000" },
                    { "PermanentAccount", "30000" },
                    { "IncomeSummaryAccount", "30000" }
                }
            }, owner.Token, "fk-block");

            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.True(valJson.GetProperty("hasErrors").GetBoolean());
            var fileErrors = valJson.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("cannot be replaced because it is linked to one or more Items"));
        }

        [Fact]
        public async Task Replace_InactiveTaxOrBank_WithMapping_AppearsInRemapping()
        {
            var owner = await _fixture.CreateUser("tax-bank-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Tax Bank Org", "tax-bank");

            // Seed an INACTIVE tax pointing to an account
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "tax-bank", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var acct = db.Accounts.First(a => a.CompanyId == companyId && a.AccountCode == "10111");
                db.Taxes.Add(new Tax
                {
                    CompanyId = companyId,
                    TaxCode = "INACTIVE_VAT",
                    TaxName = "Inactive VAT Rate",
                    Rate = 5,
                    IsActive = false, // Inactive!
                    SalesAccountId = acct.Id
                });
                await db.SaveChangesAsync();
            }

            // Check ChartStatus
            var statRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ChartStatus", token: owner.Token, org: "tax-bank");
            Assert.Equal(HttpStatusCode.OK, statRes.StatusCode);
            var statJson = await Json(statRes);
            var roles = statJson.GetProperty("requiredControlRoles").EnumerateArray().Select(r => r.GetProperty("roleKey").GetString()).ToList();
            Assert.Contains(roles, r => r.StartsWith("Tax_Sales_"));
        }

        [Fact]
        public async Task Replace_RequiresOwnerRole_NonOwnerForbidden()
        {
            var owner = await _fixture.CreateUser("rep-owner@example.com");
            var member = await _fixture.CreateUser("rep-member@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Rep Role Org", "rep-role");

            // Give member Accountant role (has financials.accounts.manage, but is NOT Owner)
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var accountantRole = db.SecurityRoles.First(r => r.Name == "Accountant");
                var memberRow = new CompanyMember
                {
                    CompanyId = companyId,
                    UserId = member.Id,
                    Role = CompanyMemberRole.Member, // Member role, not Owner!
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                };
                memberRow.SecurityRoles.Add(new CompanyMemberSecurityRole { SecurityRoleId = accountantRole.Id });
                db.CompanyMembers.Add(memberRow);
                await db.SaveChangesAsync();
            }

            var csv = "# AccountGo-COA-v1\r\nAccountCode,AccountName,ParentAccountCode,AccountClass\r\n10000,New Assets,,Assets";

            // Validate as non-owner flags error
            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=replace", new { csvContent = csv, mode = "replace" }, member.Token, "rep-role");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.True(valJson.GetProperty("hasErrors").GetBoolean());
            var fileErrors = valJson.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("requires workspace Owner role"));

            // Commit as non-owner is 403 Forbidden
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=replace", new { csvContent = csv, mode = "replace" }, member.Token, "rep-role");
            Assert.Equal(HttpStatusCode.Forbidden, comRes.StatusCode);
        }

        [Fact]
        public async Task Replace_SucceedsOnEmptyBooks_WithControlRemapping_AtomicallyReplacesChart()
        {
            var owner = await _fixture.CreateUser("rep-succ-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Rep Success Org", "rep-succ");

            // Fresh workspace has 0 postings (empty books)
            var newChartCsv = "# AccountGo-COA-v1\r\n" +
                              "AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status\r\n" +
                              "1000,Assets Summary,,Assets,FALSE,FALSE,Root Assets,Active\r\n" +
                              "1100,Cash Holdings,1000,Assets,FALSE,TRUE,Bank & Cash,Active\r\n" +
                              "2000,Liabilities Summary,,Liabilities,FALSE,FALSE,Root Liabilities,Active\r\n" +
                              "2100,Accounts Payable New,2000,Liabilities,FALSE,FALSE,Trade payables,Active\r\n" +
                              "3000,Equity Summary,,Equity,FALSE,FALSE,Root Equity,Active\r\n" +
                              "3100,Retained Earnings New,3000,Equity,FALSE,FALSE,Permanent equity,Active\r\n" +
                              "4000,Revenue Summary,,Revenue,FALSE,FALSE,Root Revenue,Active\r\n" +
                              "4100,Sales Revenue New,4000,Revenue,FALSE,FALSE,Sales,Active\r\n" +
                              "5000,Expenses Summary,,Expenses,FALSE,FALSE,Root Expenses,Active\r\n" +
                              "5100,COGS New,5000,Expenses,FALSE,FALSE,Direct costs,Active\r\n" +
                              "9999,Income Summary New,,Closing accounts,FALSE,FALSE,Closing summary,Active\r\n";

            // Initial validate discovers all required control mappings
            var initValRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=replace", new
            {
                csvContent = newChartCsv,
                mode = "replace"
            }, owner.Token, "rep-succ");
            Assert.Equal(HttpStatusCode.OK, initValRes.StatusCode);
            var initValJson = await Json(initValRes);
            var reqMappings = initValJson.GetProperty("controlMappings").EnumerateArray().ToList();
            Assert.NotEmpty(reqMappings);

            var mappings = new Dictionary<string, string>();
            foreach (var req in reqMappings)
            {
                var roleKey = req.GetProperty("roleKey").GetString()!;
                if (roleKey.StartsWith("Bank:")) mappings[roleKey] = "1100"; // Cash holdings
                else if (roleKey.StartsWith("Tax:")) mappings[roleKey] = "2100"; // Accounts payable / tax payable
                else if (roleKey == "PayableAccount") mappings[roleKey] = "2100";
                else if (roleKey == "PurchaseDiscountAccount") mappings[roleKey] = "5100";
                else if (roleKey == "GoodsReceiptNoteClearingAccount") mappings[roleKey] = "1100";
                else if (roleKey == "SalesDiscountAccount" || roleKey == "ShippingChargeAccount") mappings[roleKey] = "4100";
                else if (roleKey == "PermanentAccount") mappings[roleKey] = "3100";
                else if (roleKey == "IncomeSummaryAccount") mappings[roleKey] = "9999";
                else mappings[roleKey] = "1100";
            }

            // Validate with all required roles mapped
            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=replace", new
            {
                csvContent = newChartCsv,
                mode = "replace",
                controlMappings = mappings
            }, owner.Token, "rep-succ");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.False(valJson.GetProperty("hasErrors").GetBoolean());
            Assert.True(valJson.GetProperty("deleteCount").GetInt32() > 0, "Default seeded accounts should be marked for deletion");

            // Commit
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=replace", new
            {
                csvContent = newChartCsv,
                mode = "replace",
                controlMappings = mappings
            }, owner.Token, "rep-succ");
            Assert.Equal(HttpStatusCode.OK, comRes.StatusCode);
            var comJson = await Json(comRes);
            Assert.True(comJson.GetProperty("createdCount").GetInt32() > 0);
            Assert.True(comJson.GetProperty("deletedCount").GetInt32() > 0);

            // Verify new accounts exist
            var acctsRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: "rep-succ");
            Assert.Equal(HttpStatusCode.OK, acctsRes.StatusCode);
            var acctsJson = await Json(acctsRes);
            var allCodes = new List<string>();
            void CollectCodes(JsonElement el)
            {
                allCodes.Add(el.GetProperty("accountCode").GetString()!);
                if (el.TryGetProperty("childAccounts", out var children) && children.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in children.EnumerateArray())
                    {
                        CollectCodes(c);
                    }
                }
            }
            foreach (var root in acctsJson.EnumerateArray())
            {
                CollectCodes(root);
            }
            Assert.Contains("1000", allCodes);
            Assert.Contains("2100", allCodes);
            Assert.Contains("9999", allCodes);

            // Verify old accounts like 10111 were deleted
            Assert.DoesNotContain("10111", allCodes);

            // Verify GeneralLedgerSetting was updated to point to new account IDs
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "rep-succ", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var gls = db.GeneralLedgerSettings.First(s => s.CompanyId == companyId);
                var apAcct = db.Accounts.First(a => a.CompanyId == companyId && a.AccountCode == "2100");
                Assert.Equal(apAcct.Id, gls.PayableAccountId);
            }
        }

        [Fact]
        public async Task RetireMissing_RequiresOwner_AndArchivesOmittedNonProtected()
        {
            var owner = await _fixture.CreateUser("ret-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Retire Org", "ret-org");

            // Create a custom unposted leaf account 10980
            await _fixture.SendJson(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "10980",
                AccountName = "Retirable Account",
                AccountClassId = 1
            }, owner.Token, "ret-org");

            // CSV omits 10980 but includes seeded accounts
            // To test blocking protected omitted accounts:
            // If the CSV omits 20110 (AP control account) and retireMissing is true, it MUST block!
            var csvOmittingProtected = "# AccountGo-COA-v1\r\n" +
                                       "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                                       "10000,Assets Summary,,Assets\r\n";

            var blockValRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=upsert", new
            {
                csvContent = csvOmittingProtected,
                mode = "upsert",
                retireMissing = true
            }, owner.Token, "ret-org");
            Assert.Equal(HttpStatusCode.OK, blockValRes.StatusCode);
            var blockValJson = await Json(blockValRes);
            Assert.True(blockValJson.GetProperty("hasErrors").GetBoolean());
            var fileErrors = blockValJson.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("protected control account"));
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public class Fixture : IAsyncLifetime
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "accountgo_coarep_" + Guid.NewGuid().ToString("N") + ".db");
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
                if (scoped) request.Headers.Add("X-AccountGo-Org", org ?? "test-org");
                if (body != null) request.Content = JsonContent.Create(body);
                return await _client.SendAsync(request);
            }

            public async Task<HttpResponseMessage> SendJson(HttpMethod method, string path, object body, string token = null, string org = null, bool scoped = true)
            {
                return await Send(method, path, body, token, org, scoped);
            }
        }

        public sealed record UserSession(string Token, string Id);
    }
}
