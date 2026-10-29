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
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class ChartOfAccountsLifecycleTests : IClassFixture<ChartOfAccountsLifecycleTests.Fixture>
    {
        private readonly Fixture _fixture;
        public ChartOfAccountsLifecycleTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public async Task ExportStructure_ReturnsAccountGoCoaV1_WithoutBalances()
        {
            var owner = await _fixture.CreateUser("exp-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Structure Org", "struct-org");

            var res = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportStructure", token: owner.Token, org: "struct-org");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);

            var csv = await res.Content.ReadAsStringAsync();
            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.True(lines.Length >= 2);
            // Must have marker
            Assert.Equal("# AccountGo-COA-v1", lines[0]);
            // Header row
            Assert.Equal("AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status", lines[1]);
            // MUST NOT contain Balance column
            Assert.DoesNotContain("Balance", lines[1]);

            // Contains seeded accounts
            Assert.Contains("10111", csv);
            Assert.Contains("Active", csv);
        }

        [Fact]
        public async Task ExportSnapshot_ReturnsAccountGoCoaSnapshotV1_WithBalances()
        {
            var owner = await _fixture.CreateUser("snap-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Snapshot Org", "snap-org");

            var res = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportSnapshot", token: owner.Token, org: "snap-org");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);

            var csv = await res.Content.ReadAsStringAsync();
            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.True(lines.Length >= 2);
            // Must have snapshot marker
            Assert.Equal("# AccountGo-COA-Snapshot-v1", lines[0]);
            // Header row must include Balance
            Assert.Contains("Balance", lines[1]);
            Assert.Contains("Level", lines[1]);
            Assert.Contains("NormalBalance", lines[1]);
            Assert.Contains("Header", lines[1]);
        }

        [Fact]
        public async Task ExportTemplate_ReturnsValidSampleStructure()
        {
            var owner = await _fixture.CreateUser("tmpl-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Template Org", "tmpl-org");

            var res = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportTemplate", token: owner.Token, org: "tmpl-org");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var csv = await res.Content.ReadAsStringAsync();
            Assert.StartsWith("# AccountGo-COA-v1", csv);
            Assert.Contains("10000,Assets,,Assets,FALSE,FALSE", csv);
            Assert.Contains("10121,Allowance for Doubtful Accounts,10100,Assets,TRUE,FALSE", csv);
            Assert.DoesNotContain("Balance", csv);
        }

        [Fact]
        public async Task AddUpdateArchiveUnarchive_AccountLifecycle()
        {
            var owner = await _fixture.CreateUser("life-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Lifecycle Org", "life-org");

            // 1. Add new account
            var addRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "10999",
                AccountName = "Special Project Cash",
                AccountClassId = 1,
                Description = "Temporary project funds",
                IsCash = true,
                IsContraAccount = false
            }, owner.Token, "life-org");
            Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);

            // Verify in Accounts tree
            var treeRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: "life-org");
            var treeJson = await treeRes.Content.ReadAsStringAsync();
            Assert.Contains("10999", treeJson);
            Assert.Contains("Special Project Cash", treeJson);

            // 2. Update account
            var updateRes = await _fixture.Send(HttpMethod.Put, "/api/Financials/UpdateAccount/10999", new Dto.Financial.Account
            {
                AccountCode = "10999",
                AccountName = "Renamed Project Cash",
                Description = "Updated description",
                IsCash = true
            }, owner.Token, "life-org");
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

            // 3. Archive account
            var archiveRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/ArchiveAccount/10999", token: owner.Token, org: "life-org");
            Assert.Equal(HttpStatusCode.OK, archiveRes.StatusCode);
            var archiveJson = await Json(archiveRes);
            Assert.True(archiveJson.GetProperty("archived").GetBoolean());

            // Check tree shows archived
            var treeAfterArchive = await (await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: "life-org")).Content.ReadAsStringAsync();
            Assert.Contains("\"archived\":true", treeAfterArchive);

            // 4. Unarchive account
            var unarchiveRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/UnarchiveAccount/10999", token: owner.Token, org: "life-org");
            Assert.Equal(HttpStatusCode.OK, unarchiveRes.StatusCode);
            var unarchiveJson = await Json(unarchiveRes);
            Assert.False(unarchiveJson.GetProperty("archived").GetBoolean());

            // 5. Delete unused leaf account
            var delRes = await _fixture.Send(HttpMethod.Delete, "/api/Financials/DeleteAccount/10999", token: owner.Token, org: "life-org");
            Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

            // Verify gone
            var afterDel = await _fixture.Send(HttpMethod.Get, "/api/Financials/GetAccount/10999", token: owner.Token, org: "life-org");
            Assert.Equal(HttpStatusCode.NotFound, afterDel.StatusCode);
        }

        [Fact]
        public async Task ProtectedAccount_BlocksArchiveAndBlocksDelete_WithActionableReason()
        {
            var owner = await _fixture.CreateUser("prot-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Protected Org", "prot-org");

            int apAccountId;
            int bankAccountId;
            int parentWithChildrenId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "prot-org", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var gls = db.GeneralLedgerSettings.Single(s => s.CompanyId == companyId);
                apAccountId = gls.PayableAccountId.Value;

                var bankAcct = db.Accounts.First(a => a.CompanyId == companyId && a.AccountCode == "10111");
                bankAccountId = bankAcct.Id;
                db.Banks.Add(new Bank
                {
                    CompanyId = companyId,
                    Name = "Operating Checking",
                    AccountId = bankAccountId,
                    BankName = "Test Bank",
                    Number = "12345678",
                    IsActive = true
                });
                await db.SaveChangesAsync();

                parentWithChildrenId = db.Accounts.First(a => a.CompanyId == companyId && a.ChildAccounts.Any()).Id;

                var apCode = db.Accounts.Single(a => a.Id == apAccountId).AccountCode;
                var bankCode = db.Accounts.Single(a => a.Id == bankAccountId).AccountCode;
                var parentCode = db.Accounts.Single(a => a.Id == parentWithChildrenId).AccountCode;

                // 1. Try to archive AP account (GLS protected)
                var archGls = await _fixture.Send(HttpMethod.Post, $"/api/Financials/ArchiveAccount/{apCode}", token: owner.Token, org: "prot-org");
                Assert.Equal(HttpStatusCode.BadRequest, archGls.StatusCode);
                var archGlsMsg = (await Json(archGls)).GetProperty("message").GetString();
                Assert.Contains("General Ledger Settings", archGlsMsg);

                // 2. Try to delete AP account (MUST NOT silently null PayableAccountId!)
                var delGls = await _fixture.Send(HttpMethod.Delete, $"/api/Financials/DeleteAccount/{apCode}", token: owner.Token, org: "prot-org");
                Assert.Equal(HttpStatusCode.BadRequest, delGls.StatusCode);
                var delGlsMsg = (await Json(delGls)).GetProperty("message").GetString();
                Assert.Contains("General Ledger Settings", delGlsMsg);

                // Verify GLS PayableAccountId is NOT nulled
                var glsAfter = db.GeneralLedgerSettings.IgnoreQueryFilters().AsNoTracking().Single(s => s.CompanyId == companyId);
                Assert.Equal(apAccountId, glsAfter.PayableAccountId);

                // 3. Try to archive Bank GL account
                var archBank = await _fixture.Send(HttpMethod.Post, $"/api/Financials/ArchiveAccount/{bankCode}", token: owner.Token, org: "prot-org");
                Assert.Equal(HttpStatusCode.BadRequest, archBank.StatusCode);
                Assert.Contains("Bank", (await Json(archBank)).GetProperty("message").GetString());

                // 4. Try to delete parent with children
                var delParent = await _fixture.Send(HttpMethod.Delete, $"/api/Financials/DeleteAccount/{parentCode}", token: owner.Token, org: "prot-org");
                Assert.Equal(HttpStatusCode.BadRequest, delParent.StatusCode);
                Assert.Contains("child accounts", (await Json(delParent)).GetProperty("message").GetString());
            }
        }

        [Fact]
        public async Task Pickers_ExcludeArchivedAccounts()
        {
            var owner = await _fixture.CreateUser("pck-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Picker Org", "pck-org");

            // Create a custom revenue account and expense account
            await _fixture.Send(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "49999",
                AccountName = "Archived Consulting Revenue",
                AccountClassId = 4
            }, owner.Token, "pck-org");

            await _fixture.Send(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "59999",
                AccountName = "Archived Office Expense",
                AccountClassId = 5
            }, owner.Token, "pck-org");

            // Verify they appear before archive
            var salesBefore = await (await _fixture.Send(HttpMethod.Get, "/api/Catalog/SalesAccounts", token: owner.Token, org: "pck-org")).Content.ReadAsStringAsync();
            Assert.Contains("49999", salesBefore);

            var billsBefore = await (await _fixture.Send(HttpMethod.Get, "/api/Purchasing/BillAccounts", token: owner.Token, org: "pck-org")).Content.ReadAsStringAsync();
            Assert.Contains("59999", billsBefore);

            // Archive both
            var archRev = await _fixture.Send(HttpMethod.Post, "/api/Financials/ArchiveAccount/49999", token: owner.Token, org: "pck-org");
            Assert.Equal(HttpStatusCode.OK, archRev.StatusCode);

            var archExp = await _fixture.Send(HttpMethod.Post, "/api/Financials/ArchiveAccount/59999", token: owner.Token, org: "pck-org");
            Assert.Equal(HttpStatusCode.OK, archExp.StatusCode);

            // Verify they are HIDDEN from pickers
            var salesAfter = await (await _fixture.Send(HttpMethod.Get, "/api/Catalog/SalesAccounts", token: owner.Token, org: "pck-org")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("49999", salesAfter);

            var billsAfter = await (await _fixture.Send(HttpMethod.Get, "/api/Purchasing/BillAccounts", token: owner.Token, org: "pck-org")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("59999", billsAfter);
        }

        [Fact]
        public async Task Permissions_GateManageVsView()
        {
            var owner = await _fixture.CreateUser("permcoa-owner@example.com");
            var member = await _fixture.CreateUser("permcoa-member@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Perm COA", "perm-coa");

            // Member has Auditor / Viewer role (FinancialsAccountsView but not FinancialsAccountsManage)
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var roleId = db.SecurityRoles.Single(r => r.Name == "Auditor / Viewer").Id;
                var memberRow = new CompanyMember
                {
                    CompanyId = companyId,
                    UserId = member.Id,
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                };
                memberRow.SecurityRoles.Add(new CompanyMemberSecurityRole { SecurityRoleId = roleId });
                db.CompanyMembers.Add(memberRow);
                await db.SaveChangesAsync();
            }

            // Member CAN view accounts and snapshot
            var viewRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: member.Token, org: "perm-coa");
            Assert.Equal(HttpStatusCode.OK, viewRes.StatusCode);

            var snapRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportSnapshot", token: member.Token, org: "perm-coa");
            Assert.Equal(HttpStatusCode.OK, snapRes.StatusCode);

            // Member CANNOT add account (manage required)
            var addRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "18888",
                AccountName = "Unauthorized Add",
                AccountClassId = 1
            }, member.Token, "perm-coa");
            Assert.Equal(HttpStatusCode.Forbidden, addRes.StatusCode);

            // Member CANNOT archive account
            var archRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/ArchiveAccount/10111", token: member.Token, org: "perm-coa");
            Assert.Equal(HttpStatusCode.Forbidden, archRes.StatusCode);

            // Member CANNOT delete account
            var delRes = await _fixture.Send(HttpMethod.Delete, "/api/Financials/DeleteAccount/10111", token: member.Token, org: "perm-coa");
            Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);

            // Member CANNOT export structure (manage required)
            var structRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportStructure", token: member.Token, org: "perm-coa");
            Assert.Equal(HttpStatusCode.Forbidden, structRes.StatusCode);
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public class Fixture : IAsyncLifetime
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "accountgo_coa_" + Guid.NewGuid().ToString("N") + ".db");
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
                if (scoped) request.Headers.Add("X-AccountGo-Org", org ?? "life-org");
                if (body != null) request.Content = JsonContent.Create(body);
                return await _client.SendAsync(request);
            }
        }

        public sealed record UserSession(string Token, string Id);
    }
}
