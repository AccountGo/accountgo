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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class ChartOfAccountsImportTests : IClassFixture<ChartOfAccountsImportTests.Fixture>
    {
        private readonly Fixture _fixture;
        public ChartOfAccountsImportTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public async Task Validate_RejectsSnapshotMarker_WithConversionMessage()
        {
            var owner = await _fixture.CreateUser("snap-rej-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Snap Rej", "snap-rej");

            var csv = "# AccountGo-COA-Snapshot-v1\r\nAccountCode,AccountName,ParentAccountCode,AccountClass,Balance\r\n10111,Checking,,Assets,1000.00";
            var res = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "snap-rej");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var preview = await Json(res);
            Assert.True(preview.GetProperty("hasErrors").GetBoolean());
            var fileErrors = preview.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("Snapshot files containing balances cannot be imported") && e.Contains("conversion journal"));
        }

        [Fact]
        public async Task Validate_RejectsBalanceColumn_WithConversionMessage()
        {
            var owner = await _fixture.CreateUser("bal-rej-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Bal Rej", "bal-rej");

            var csv = "# AccountGo-COA-v1\r\nAccountCode,AccountName,ParentAccountCode,AccountClass,OpeningBalance\r\n10111,Checking,,Assets,500.00";
            var res = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "bal-rej");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var preview = await Json(res);
            Assert.True(preview.GetProperty("hasErrors").GetBoolean());
            var fileErrors = preview.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("balance column") && e.Contains("conversion journal"));
        }

        [Fact]
        public async Task Validate_RejectsUnknownFormatMarker()
        {
            var owner = await _fixture.CreateUser("fmt-rej-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Fmt Rej", "fmt-rej");

            var csv = "# SomeOtherApp-v2\r\nAccountCode,AccountName,ParentAccountCode,AccountClass\r\n10111,Checking,,Assets";
            var res = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "fmt-rej");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var preview = await Json(res);
            Assert.True(preview.GetProperty("hasErrors").GetBoolean());
            var fileErrors = preview.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileErrors, e => e.Contains("Unsupported file format marker"));
        }

        [Fact]
        public async Task Validate_IgnoresReservedColumns_WithSoftWarning()
        {
            var owner = await _fixture.CreateUser("res-col-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Res Col", "res-col");

            var csv = "# AccountGo-COA-v1\r\nAccountCode,AccountName,ParentAccountCode,AccountClass,DefaultTaxCode,Currency\r\n10910,Regional Cash,,Assets,TAX1,USD";
            var res = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "res-col");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var preview = await Json(res);
            Assert.False(preview.GetProperty("hasErrors").GetBoolean());
            var fileWarnings = preview.GetProperty("fileWarnings").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(fileWarnings, w => w.Contains("DefaultTaxCode") && w.Contains("reserved"));
            Assert.Contains(fileWarnings, w => w.Contains("Currency") && w.Contains("reserved"));
        }

        [Fact]
        public async Task AppendMode_CreatesMissingAndSkipsExisting_WithWarningNotes()
        {
            var owner = await _fixture.CreateUser("app-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Append Org", "app-org");

            // 10111 already exists in seeded chart
            // 10980 (parent) and 10981 (child) are new
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status\r\n" +
                      "10111,Existing Checking,,Assets,FALSE,FALSE,,Active\r\n" +
                      "10980,Regional Bank Group,,Assets,FALSE,TRUE,Group for regional banks,Active\r\n" +
                      "10981,Regional Branch 1,10980,Assets,FALSE,TRUE,Branch 1 cash,Active\r\n";

            // 1. Validate
            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "app-org");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);

            var valJson = await Json(valRes);
            Assert.False(valJson.GetProperty("hasErrors").GetBoolean());
            Assert.Equal(2, valJson.GetProperty("createCount").GetInt32());
            Assert.Equal(1, valJson.GetProperty("skipCount").GetInt32());
            Assert.Equal(0, valJson.GetProperty("updateCount").GetInt32());

            var rows = valJson.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal("skip", rows[0].GetProperty("action").GetString());
            Assert.Contains("already exists", rows[0].GetProperty("warnings")[0].GetString());
            Assert.Equal("create", rows[1].GetProperty("action").GetString());
            Assert.Equal("create", rows[2].GetProperty("action").GetString());

            // 2. Commit
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "app-org");
            Assert.Equal(HttpStatusCode.OK, comRes.StatusCode);

            var comJson = await Json(comRes);
            Assert.Equal(2, comJson.GetProperty("createdCount").GetInt32());
            Assert.Equal(1, comJson.GetProperty("skippedCount").GetInt32());
            Assert.Equal(0, comJson.GetProperty("updatedCount").GetInt32());

            // 3. Verify in Accounts endpoint
            var treeRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: "app-org");
            var treeStr = await treeRes.Content.ReadAsStringAsync();
            Assert.Contains("10980", treeStr);
            Assert.Contains("Regional Bank Group", treeStr);
            Assert.Contains("10981", treeStr);
            Assert.Contains("Regional Branch 1", treeStr);
        }

        [Fact]
        public async Task UpsertMode_UpdatesExistingAttributes_AndCreatesNew()
        {
            var owner = await _fixture.CreateUser("ups-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Upsert Org", "ups-org");

            // Seed a custom account first to update
            await _fixture.SendJson(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "10970",
                AccountName = "Initial Project Cash",
                AccountClassId = 1,
                Description = "Initial notes",
                IsCash = false,
                IsContraAccount = false
            }, owner.Token, "ups-org");

            // CSV updates 10970 and creates 10971
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status\r\n" +
                      "10970,Renamed Project Cash,,Assets,FALSE,TRUE,Updated notes via Upsert,Active\r\n" +
                      "10971,Newly Added Cash,10970,Assets,FALSE,TRUE,Sub account,Active\r\n";

            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=upsert", new { csvContent = csv, mode = "upsert" }, owner.Token, "ups-org");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.False(valJson.GetProperty("hasErrors").GetBoolean());
            Assert.Equal(1, valJson.GetProperty("updateCount").GetInt32());
            Assert.Equal(1, valJson.GetProperty("createCount").GetInt32());

            // Commit
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=upsert", new { csvContent = csv, mode = "upsert" }, owner.Token, "ups-org");
            Assert.Equal(HttpStatusCode.OK, comRes.StatusCode);
            var comJson = await Json(comRes);
            Assert.Equal(1, comJson.GetProperty("updatedCount").GetInt32());
            Assert.Equal(1, comJson.GetProperty("createdCount").GetInt32());

            // Verify updated account
            var acctRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/GetAccount/10970", token: owner.Token, org: "ups-org");
            var acctJson = await Json(acctRes);
            Assert.Equal("Renamed Project Cash", acctJson.GetProperty("accountName").GetString());
            Assert.Equal("Updated notes via Upsert", acctJson.GetProperty("description").GetString());
            Assert.True(acctJson.GetProperty("isCash").GetBoolean());
            Assert.False(acctJson.GetProperty("archived").GetBoolean());
        }

        [Fact]
        public async Task UpsertMode_BlocksClassChange_WhenAccountHasPostingHistory()
        {
            var owner = await _fixture.CreateUser("cls-block-owner@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Class Block Org", "cls-block");

            // Seed a GL line pointing to account 10111
            using (var scope = _fixture.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(companyId, "cls-block", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var acct = db.Accounts.First(a => a.CompanyId == companyId && a.AccountCode == "10111");
                var header = new GeneralLedgerHeader
                {
                    CompanyId = companyId,
                    Date = DateTime.UtcNow,
                    DocumentType = DocumentTypes.JournalEntry,
                    Description = "Posting history test"
                };
                db.GeneralLedgerHeaders.Add(header);
                await db.SaveChangesAsync();

                db.GeneralLedgerLines.Add(new GeneralLedgerLine
                {
                    CompanyId = companyId,
                    GeneralLedgerHeaderId = header.Id,
                    AccountId = acct.Id,
                    DrCr = DrOrCrSide.Dr,
                    Amount = 100m
                });
                await db.SaveChangesAsync();
            }

            // Attempt to change 10111 class from Assets (1) to Revenue (4)
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                      "10111,Checking Account,,Revenue\r\n";

            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=upsert", new { csvContent = csv, mode = "upsert" }, owner.Token, "cls-block");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.True(valJson.GetProperty("hasErrors").GetBoolean());
            var err = valJson.GetProperty("rows")[0].GetProperty("errors")[0].GetString();
            Assert.Contains("Cannot change account class", err);
            Assert.Contains("transaction history", err);

            // Attempt commit - fails closed
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=upsert", new { csvContent = csv, mode = "upsert" }, owner.Token, "cls-block");
            Assert.Equal(HttpStatusCode.BadRequest, comRes.StatusCode);
        }

        [Fact]
        public async Task UpsertMode_BlocksArchivingProtectedControlAccount()
        {
            var owner = await _fixture.CreateUser("prot-arch-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Prot Arch Org", "prot-arch");

            // 20110 is AP account protected by GeneralLedgerSetting
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass,Status\r\n" +
                      "20110,Accounts Payable,,Liabilities,Archived\r\n";

            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=upsert", new { csvContent = csv, mode = "upsert" }, owner.Token, "prot-arch");
            Assert.Equal(HttpStatusCode.OK, valRes.StatusCode);
            var valJson = await Json(valRes);
            Assert.True(valJson.GetProperty("hasErrors").GetBoolean());
            var err = valJson.GetProperty("rows")[0].GetProperty("errors")[0].GetString();
            Assert.Contains("Cannot archive protected account", err);
            Assert.Contains("General Ledger Settings", err);
        }

        [Fact]
        public async Task Hierarchy_DetectsCyclesAndMissingParents()
        {
            var owner = await _fixture.CreateUser("hier-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Hier Org", "hier-org");

            // 1. Missing parent
            var missingParentCsv = "# AccountGo-COA-v1\r\n" +
                                   "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                                   "10995,Orphan Account,88888,Assets\r\n";
            var resMissing = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = missingParentCsv, mode = "append" }, owner.Token, "hier-org");
            var jsonMissing = await Json(resMissing);
            Assert.True(jsonMissing.GetProperty("hasErrors").GetBoolean());
            var err1 = jsonMissing.GetProperty("rows")[0].GetProperty("errors")[0].GetString();
            Assert.Contains("Parent account '88888' does not exist", err1);

            // 2. Self parent
            var selfParentCsv = "# AccountGo-COA-v1\r\n" +
                                "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                                "10996,Self Parent,10996,Assets\r\n";
            var resSelf = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = selfParentCsv, mode = "append" }, owner.Token, "hier-org");
            var jsonSelf = await Json(resSelf);
            Assert.True(jsonSelf.GetProperty("hasErrors").GetBoolean());
            var err2 = jsonSelf.GetProperty("rows")[0].GetProperty("errors")[0].GetString();
            Assert.Contains("Account cannot be its own parent", err2);

            // 3. Circular hierarchy between two accounts
            var circularCsv = "# AccountGo-COA-v1\r\n" +
                              "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                              "10997,Cycle Account A,10998,Assets\r\n" +
                              "10998,Cycle Account B,10997,Assets\r\n";
            var resCirc = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = circularCsv, mode = "append" }, owner.Token, "hier-org");
            var jsonCirc = await Json(resCirc);
            Assert.True(jsonCirc.GetProperty("hasErrors").GetBoolean());
            var rowsCirc = jsonCirc.GetProperty("rows").EnumerateArray().ToList();
            Assert.Contains(rowsCirc, r => r.GetProperty("errors").EnumerateArray().Any(e => e.GetString().Contains("Circular hierarchy detected")));
        }

        [Fact]
        public async Task Commit_IsAtomic_RollsBackAllChangesOnBatchError()
        {
            var owner = await _fixture.CreateUser("atomic-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Atomic Org", "atomic-org");

            // Row 1 is valid (10950), Row 2 has an unknown parent (10951 -> 987654321)
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                      "10950,Valid New Account,,Assets\r\n" +
                      "10951,Invalid Child Account,987654321,Assets\r\n";

            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=append", new { csvContent = csv, mode = "append" }, owner.Token, "atomic-org");
            Assert.Equal(HttpStatusCode.BadRequest, comRes.StatusCode);

            // Verify that 10950 was NOT persisted to DB (atomic rollback)
            var acctRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/GetAccount/10950", token: owner.Token, org: "atomic-org");
            Assert.Equal(HttpStatusCode.NotFound, acctRes.StatusCode);
        }

        [Fact]
        public async Task Permissions_RequireFinancialsAccountsManage()
        {
            var owner = await _fixture.CreateUser("perm-imp-owner@example.com");
            var member = await _fixture.CreateUser("perm-imp-member@example.com");
            var companyId = await _fixture.CreateWorkspace(owner.Token, "Perm Imp", "perm-imp");

            // Member has Auditor / Viewer role (View only)
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

            var csv = "# AccountGo-COA-v1\r\nAccountCode,AccountName,ParentAccountCode,AccountClass\r\n10999,Denied Cash,,Assets";

            // Validate is Forbidden
            var valRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append", new { csvContent = csv, mode = "append" }, member.Token, "perm-imp");
            Assert.Equal(HttpStatusCode.Forbidden, valRes.StatusCode);

            // Commit is Forbidden
            var comRes = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Commit?mode=append", new { csvContent = csv, mode = "append" }, member.Token, "perm-imp");
            Assert.Equal(HttpStatusCode.Forbidden, comRes.StatusCode);
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        public class Fixture : IAsyncLifetime
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "accountgo_coaimp_" + Guid.NewGuid().ToString("N") + ".db");
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
