#nullable enable
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
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Domain.Financials;
using Dto.Subscription;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Organizations;

[Collection("WorkspaceSetupHttp")]
public sealed class PersonalBooksHttpTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"personal-books-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = "";
    private string _userId = "";

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_path}");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlanCatalog>();
                services.AddSingleton<IPlanCatalog, PersonalTestCatalog>();
            });
        });
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "personal@example.test", Email = "personal@example.test", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        _userId = user.Id;
        var security = sp.GetRequiredService<Services.Security.ISecurityService>();
        security.EnsureDefaultPermissionsAndRoles();
        security.AddUser(user.Email, user.Email, "Pat", "Owner", new List<int>());
        _token = (await sp.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, string? slug = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (slug != null) request.Headers.Add("X-AccountGo-Org", slug);
        if (body != null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Personal(string slug = "personal-books", int? link = null) =>
        Send(HttpMethod.Post, "/api/organizations/personal", new
        {
            name = "Personal books", slug, currency = "USD", linkWorkCompanyId = link,
            booksStartDate = "2026-01-01", fiscalYearPattern = "Calendar"
        });

    private async Task<int> CreateWork(string slug)
    {
        using var response = await Send(HttpMethod.Post, "/api/organizations", new { name = slug, slug, currency = "USD" });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return int.Parse(json.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("entityType", "SoleProprietor")]
    [InlineData("reportingFramework", "UsGaapPrivate")]
    [InlineData("taxRegime", "VatGst")]
    [InlineData("inventoryCostingMethod", "Fifo")]
    public async Task AccountingPolicy_PersonalBooksRejectsNatureChangesWithoutMutation(string field, string value)
    {
        using var created = await Personal();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var before = await Send(HttpMethod.Get, "/api/organizations/current/accounting-policy", slug: "personal-books");
        var original = await before.Content.ReadAsStringAsync();
        using var changed = await Send(HttpMethod.Put, "/api/organizations/current/accounting-policy", new Dictionary<string, string> { [field] = value }, "personal-books");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Contains("personal_books_policy_locked", await changed.Content.ReadAsStringAsync());
        using var after = await Send(HttpMethod.Get, "/api/organizations/current/accounting-policy", slug: "personal-books");
        Assert.Equal(original, await after.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(original);
        using var unchanged = await Send(HttpMethod.Put, "/api/organizations/current/accounting-policy", new Dictionary<string, string> { [field] = json.RootElement.GetProperty(field).GetString()! }, "personal-books");
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
    }

    [Fact]
    public async Task JournalApproval_PersonalBooksRejectsIndependentReview()
    {
        using var created = await Personal();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var response = await Send(HttpMethod.Post, "/api/organizations/current/operating-settings/journal-approval-policy", new { expectedPolicyRevision = 1, journalWorkflow = "IndependentReviewerRequired" }, "personal-books");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("personal_books_single_user", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("entityType", "SoleProprietor")]
    [InlineData("reportingFramework", "UsGaapPrivate")]
    [InlineData("taxRegime", "VatGst")]
    [InlineData("inventoryCostingMethod", "Fifo")]
    public async Task AccountingPolicy_WorkWorkspaceAllowsNatureChanges(string field, string value)
    {
        await CreateWork("work-books");
        var payload = new Dictionary<string, string> { [field] = value };
        if (field == "inventoryCostingMethod") payload["industryTemplate"] = "ProductRetail";
        using var response = await Send(HttpMethod.Put, "/api/organizations/current/accounting-policy", payload, "work-books");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CashMovement_ReadsPostedCashLinesWithInclusiveDatesAndTransfers()
    {
        using var created = await Personal();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var otherCompanyId = await CreateWork("other-books");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var companyId = (await db.Companies.SingleAsync(c => c.Slug == "personal-books")).Id;
            var cash = new Account { CompanyId = companyId, AccountClassId = 1, AccountCode = "19991", AccountName = "Bank A", IsCash = true };
            var cash2 = new Account { CompanyId = companyId, AccountClassId = 1, AccountCode = "19992", AccountName = "Bank B", IsCash = true };
            var other = new Account { CompanyId = companyId, AccountClassId = 3, AccountCode = "39991", AccountName = "Other" };
            var otherCash = new Account { CompanyId = otherCompanyId, AccountClassId = 1, AccountCode = "19993", AccountName = "Other tenant bank", IsCash = true };
            db.Accounts.AddRange(cash, cash2, other, otherCash);
            await db.SaveChangesAsync();
            var foreignHeader = new GeneralLedgerHeader { CompanyId = otherCompanyId, Date = new DateTime(2026, 2, 3), Description = "Other tenant" };
            foreignHeader.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = otherCompanyId, AccountId = otherCash.Id, DrCr = DrOrCrSide.Dr, Amount = 500m });
            db.GeneralLedgerHeaders.Add(foreignHeader);
            void Post(int day, string description, Account debit, Account credit, decimal amount)
            {
                var header = new GeneralLedgerHeader { CompanyId = companyId, Date = new DateTime(2026, 2, day), Description = description };
                header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = debit.Id, DrCr = DrOrCrSide.Dr, Amount = amount });
                header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = credit.Id, DrCr = DrOrCrSide.Cr, Amount = amount });
                db.GeneralLedgerHeaders.Add(header);
            }
            Post(1, "Opening", cash, other, 100m);
            Post(2, "Bill one", other, cash, 10m);
            Post(3, "Income", cash, other, 25m);
            Post(4, "Transfer", cash2, cash, 20m);
            var split = new GeneralLedgerHeader { CompanyId = companyId, Date = new DateTime(2026, 2, 4), Description = "Split entry" };
            split.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 4m });
            split.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = other.Id, DrCr = DrOrCrSide.Cr, Amount = 2m });
            split.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = other.Id, DrCr = DrOrCrSide.Cr, Amount = 2m });
            db.GeneralLedgerHeaders.Add(split);
            Post(5, "Bill two", other, cash2, 5m);
            Post(6, "Outside", other, cash, 2m);
            var draft = new JournalEntryHeader { CompanyId = companyId, Date = new DateTime(2026, 2, 3), Memo = "Draft", Posted = false };
            draft.JournalEntryLines.Add(new JournalEntryLine { CompanyId = companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 999m });
            db.JournalEntryHeaders.Add(draft);
            await db.SaveChangesAsync();
        }
        using var oldBefore = await Send(HttpMethod.Get, "/api/financials/cashflow?from=2026-02-02&to=2026-02-05", slug: "personal-books");
        Assert.Equal(HttpStatusCode.OK, oldBefore.StatusCode);
        var oldBody = await oldBefore.Content.ReadAsStringAsync();
        using var response = await Send(HttpMethod.Get, "/api/financials/cash-movement?from=2026-02-02&to=2026-02-05", slug: "personal-books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement;
        Assert.Equal(100m, data.GetProperty("openingCash").GetDecimal());
        Assert.Equal(49m, data.GetProperty("totalIn").GetDecimal());
        Assert.Equal(35m, data.GetProperty("totalOut").GetDecimal());
        Assert.Equal(114m, data.GetProperty("closingCash").GetDecimal());
        var lines = data.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(6, lines.Length);
        Assert.Equal(new[] { "Bill one", "Income", "Transfer", "Transfer", "Split entry", "Bill two" }, lines.Select(x => x.GetProperty("description").GetString()).ToArray());
        Assert.All(lines, x => Assert.InRange(DateTime.Parse(x.GetProperty("date").GetString()!), new DateTime(2026, 2, 2), new DateTime(2026, 2, 5, 23, 59, 59)));
        Assert.Equal(2, lines.Count(x => x.GetProperty("description").GetString() == "Transfer"));
        Assert.Contains(lines, x => x.GetProperty("counterpartAccountName").GetString() == "Bank B");
        Assert.Equal("Split", lines.Single(x => x.GetProperty("description").GetString() == "Split entry").GetProperty("counterpartAccountName").GetString());
        using var oldAfter = await Send(HttpMethod.Get, "/api/financials/cashflow?from=2026-02-02&to=2026-02-05", slug: "personal-books");
        Assert.Equal(oldBody, await oldAfter.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetupCapabilities_OffersPersonalBooksToNewOwner_UntilPersonalBooksExist()
    {
        using var before = await Send(HttpMethod.Get, "/api/organizations/setup-capabilities");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        Assert.True(beforeJson.RootElement.GetProperty("canAddPersonalBooks").GetBoolean());
        using var created = await Personal();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var after = await Send(HttpMethod.Get, "/api/organizations/setup-capabilities");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.False(afterJson.RootElement.GetProperty("canAddPersonalBooks").GetBoolean());
    }

    [Fact]
    public async Task PersonalCreate_InitializesIndividualSoloWithFullOwnerAccess_WithoutPurposeFlag()
    {
        using var response = await Personal();
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var company = await db.Companies.SingleAsync(c => c.Slug == "personal-books");
        Assert.Equal(PersonalTestCatalog.Key, company.Plan);
        Assert.Equal(CompanyActivationStates.Active, company.ActivationState);
        var settings = await db.CompanySettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == company.Id);
        Assert.Equal(EntityType.Individual, settings.EntityType);
        var operating = await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspacePurpose.Personal, operating.Purpose);
        Assert.Equal(WorkspaceCollaborationIntent.JustMe, operating.CollaborationIntent);
        Assert.Equal(WorkspaceCollaborationState.SoloActive, operating.CollaborationState);
        var journal = await db.WorkspaceApprovalRules.IgnoreQueryFilters().SingleAsync(r => r.CompanyId == company.Id
            && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
        Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, journal.RuleKind);
        var owner = await db.CompanyMembers.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == company.Id && m.UserId == _userId);
        Assert.Equal(CompanyMemberRole.Owner, owner.Role);
        Assert.True(await db.CompanyMemberSecurityRoles.IgnoreQueryFilters().AnyAsync(r => r.CompanyMemberId == owner.Id
            && r.SecurityRole!.Name == WorkspaceAccessProfileKeys.FullWorkspaceAccessV1));
    }

    [Fact]
    public async Task GeneralCreate_RejectsPersonalPlan_WithoutProvisioning()
    {
        using var response = await Send(HttpMethod.Post, "/api/organizations", new
        { name = "Bypass", slug = "bypass", plan = PersonalTestCatalog.Key, workspacePurpose = "Business" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("use_personal_books_setup", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        Assert.False(await db.Companies.AnyAsync(c => c.Slug == "bypass"));
    }

    [Fact]
    public async Task SecondPersonalCreate_Conflicts_AndDeleteAllowsReplacement()
    {
        using var first = await Personal();
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await Personal("other-personal");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("personal_workspace_exists", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var deleted = await Send(HttpMethod.Delete, "/api/organizations/current", new { confirmSlug = "personal-books" }, "personal-books");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var replacement = await Personal("replacement");
        Assert.True(replacement.StatusCode == HttpStatusCode.Created, await replacement.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ParallelPersonalCreates_CommitExactlyOneCompanyAndRegistryRow()
    {
        var responses = await Task.WhenAll(Personal("race-a"), Personal("race-b"));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            Assert.Equal(1, await db.Companies.CountAsync(c => c.Slug == "race-a" || c.Slug == "race-b"));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM PersonalWorkspaces").SingleAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Links_RequireOwnership_RejectPersonalAndDuplicate_AndUnlinkIsAudited()
    {
        var workId = await CreateWork("work");
        var otherId = await CreateWork("not-owned");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var member = await db.CompanyMembers.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == otherId && m.UserId == _userId);
            member.Role = CompanyMemberRole.Member;
            await db.SaveChangesAsync();
        }
        using var personal = await Personal();
        Assert.Equal(HttpStatusCode.Created, personal.StatusCode);
        using var json = JsonDocument.Parse(await personal.Content.ReadAsStringAsync());
        var personalId = int.Parse(json.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
        foreach (var id in new[] { otherId, personalId })
        {
            using var denied = await Send(HttpMethod.Post, "/api/organizations/personal/link", new { workCompanyId = id });
            Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
        }
        using var linked = await Send(HttpMethod.Post, "/api/organizations/personal/link", new { workCompanyId = workId });
        Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
        using var duplicate = await Send(HttpMethod.Post, "/api/organizations/personal/link", new { workCompanyId = workId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("work_workspace_already_linked", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var list = await Send(HttpMethod.Get, "/api/organizations");
        using var listing = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var work = listing.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == workId.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(personalId, work.GetProperty("linkedPersonalCompanyId").GetInt32());
        Assert.False(work.GetProperty("canAddPersonalBooks").GetBoolean());
        using var unlinked = await Send(HttpMethod.Delete, $"/api/organizations/personal/link/{workId}");
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
        using var checkScope = _factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        check.SuppressTenantSecurityCheck = true;
        Assert.Equal(4, await check.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.TableName == "__WorkspaceLink"));
    }

    private async Task<int> CreateLegacyPersonal(string slug, string? plan = null)
    {
        var id = await CreateWork(slug);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var settings = await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.CompanyId == id);
        if (settings == null)
        {
            settings = new WorkspaceOperatingSettings { CompanyId = id };
            db.WorkspaceOperatingSettings.Add(settings);
        }
        settings.Purpose = WorkspacePurpose.Personal;
        if (plan != null) (await db.Companies.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).Plan = plan;
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<JsonElement> Row(int id)
    {
        using var list = await Send(HttpMethod.Get, "/api/organizations");
        var listing = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        return listing.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == id.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Adopt_RecordsOlderPersonalWorkspace_KeepsPlan_ThenAllowsLinkingWork()
    {
        var legacyId = await CreateLegacyPersonal("household");
        var workId = await CreateWork("studio");
        var before = await Row(legacyId);
        var planBefore = before.GetProperty("plan").GetString();
        Assert.True(before.GetProperty("canUseAsPersonalBooks").GetBoolean());
        Assert.True(before.GetProperty("isPersonalPurpose").GetBoolean());
        Assert.False((await Row(workId)).GetProperty("canUseAsPersonalBooks").GetBoolean());

        using (var workAdopt = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{workId}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, workAdopt.StatusCode);
            Assert.Contains("personal_books_not_eligible", await workAdopt.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var adopted = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{legacyId}"))
            Assert.Equal(HttpStatusCode.NoContent, adopted.StatusCode);

        var after = await Row(legacyId);
        Assert.True(after.GetProperty("isPersonalBooks").GetBoolean());
        Assert.False(after.GetProperty("canUseAsPersonalBooks").GetBoolean());
        Assert.False(after.GetProperty("canAddPersonalBooks").GetBoolean());
        Assert.False(after.GetProperty("onPersonalBooksPlan").GetBoolean());
        Assert.Equal(planBefore, after.GetProperty("plan").GetString());

        using (var again = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{legacyId}"))
            Assert.Contains("personal_workspace_exists", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using (var created = await Personal())
            Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);
        using (var linked = await Send(HttpMethod.Post, "/api/organizations/personal/link", new { workCompanyId = workId }))
            Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
        Assert.Equal(legacyId, (await Row(workId)).GetProperty("linkedPersonalCompanyId").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        Assert.Equal(1, await db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.CompanyId == legacyId && a.TableName == "__PersonalBooks"));
    }

    private async Task MakeIndividual(int companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        (await db.CompanySettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == companyId)).EntityType = EntityType.Individual;
        var capital = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "30100");
        capital.AccountName = "Accumulated net worth";
        await db.SaveChangesAsync();
    }

    private Api.Service.WorkspaceSetupCorrectionResult Convert(int companyId, string reason = "Owner confirmed these are freelance books")
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Api.Service.WorkspaceSetupCorrectionService>()
            .ConvertPersonalToFreelance(companyId, "staff-operator", reason);
    }

    [Fact]
    public async Task SetupCorrection_TurnsOlderPersonalWorkspaceIntoFreelanceBooks_WithoutTouchingEntries()
    {
        var legacyId = await CreateLegacyPersonal("old-personal");
        await MakeIndividual(legacyId);
        long revisionBefore;
        int accountsBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            revisionBefore = (await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == legacyId)).PolicyRevision;
            accountsBefore = await db.Accounts.IgnoreQueryFilters().CountAsync(a => a.CompanyId == legacyId);
        }

        Assert.Equal("reason_required", Convert(legacyId, " ").Code);
        var result = Convert(legacyId);
        Assert.True(result.Applied, result.Message);
        Assert.Equal("Personal", result.Before!.Purpose);
        Assert.Equal("Individual", result.Before.EntityType);
        Assert.Equal("Business", result.After!.Purpose);
        Assert.Equal("SoleProprietor", result.After.EntityType);
        Assert.Equal("Owner's Capital", result.After.EquityAccounts.Single(a => a.Code == "30100").Name);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            Assert.Equal(revisionBefore + 1, (await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == legacyId)).PolicyRevision);
            Assert.Equal(accountsBefore, await db.Accounts.IgnoreQueryFilters().CountAsync(a => a.CompanyId == legacyId));
            Assert.Equal(1, await db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.CompanyId == legacyId
                && a.TableName == Api.Service.WorkspaceSetupCorrectionService.AuditTable));
        }

        Assert.False((await Row(legacyId)).GetProperty("canUseAsPersonalBooks").GetBoolean());
        Assert.Equal("setup_correction_blocked", Convert(legacyId).Code);
        using (var personal = await Personal(link: legacyId))
            Assert.Equal(HttpStatusCode.Created, personal.StatusCode);
        Assert.True((await Row(legacyId)).GetProperty("linkedPersonalCompanyId").GetInt32() > 0);
    }

    [Fact]
    public async Task SetupCorrection_RefusesBusinessAndLinkedWorkspaces()
    {
        var businessId = await CreateWork("real-business");
        var blocked = Convert(businessId);
        Assert.False(blocked.Applied);
        Assert.Equal("setup_correction_blocked", blocked.Code);

        var booksId = await CreateLegacyPersonal("linked-books");
        using (var adopted = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{booksId}"))
            Assert.Equal(HttpStatusCode.NoContent, adopted.StatusCode);
        var workId = await CreateWork("linked-work");
        using (var linked = await Send(HttpMethod.Post, "/api/organizations/personal/link", new { workCompanyId = workId }))
            Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
        var refused = Convert(booksId);
        Assert.False(refused.Applied);
        Assert.Contains("Unlink", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Personal", refused.Before!.Purpose);
    }

    [Fact]
    public async Task SetupCorrection_ReleasesPersonalBooksRecord_SoNewPersonalBooksCanBeCreatedAndLinked()
    {
        var oldId = await CreateLegacyPersonal("was-personal");
        await MakeIndividual(oldId);
        using (var adopted = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{oldId}"))
            Assert.Equal(HttpStatusCode.NoContent, adopted.StatusCode);

        var result = Convert(oldId);
        Assert.True(result.Applied, result.Message);
        Assert.True(result.Before!.IsPersonalBooks);
        Assert.False(result.After!.IsPersonalBooks);

        using (var personal = await Personal(slug: "new-personal", link: oldId))
            Assert.Equal(HttpStatusCode.Created, personal.StatusCode);
        var row = await Row(oldId);
        Assert.False(row.GetProperty("isPersonalBooks").GetBoolean());
        Assert.True(row.GetProperty("linkedPersonalCompanyId").GetInt32() > 0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var audit = await db.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.CompanyId == oldId
            && a.TableName == Api.Service.WorkspaceSetupCorrectionService.AuditTable);
        Assert.Contains(_userId, audit.OriginalValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adopt_RequiresOwner_AndOnlyOneAdoptionPerPerson()
    {
        var first = await CreateLegacyPersonal("home-one");
        var second = await CreateLegacyPersonal("home-two");
        var notOwned = await CreateLegacyPersonal("home-shared");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            (await db.CompanyMembers.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == notOwned && m.UserId == _userId)).Role = CompanyMemberRole.Member;
            await db.SaveChangesAsync();
        }
        Assert.False((await Row(notOwned)).GetProperty("canUseAsPersonalBooks").GetBoolean());
        using (var denied = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{notOwned}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var adopted = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{first}"))
            Assert.Equal(HttpStatusCode.NoContent, adopted.StatusCode);
        Assert.False((await Row(second)).GetProperty("canUseAsPersonalBooks").GetBoolean());
        using var secondAdopt = await Send(HttpMethod.Post, $"/api/organizations/personal/adopt/{second}");
        Assert.Equal(HttpStatusCode.Conflict, secondAdopt.StatusCode);
        Assert.Contains("personal_workspace_exists", await secondAdopt.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersonalCreateWithInvalidLink_RollsBackCompany()
    {
        using var response = await Personal(link: int.MaxValue);
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        Assert.False(await db.Companies.AnyAsync(c => c.Slug == "personal-books"));
    }

    [Fact]
    public async Task CommunityCatalog_UsesDefaultPlanForPersonalBooks()
    {
        using var communityFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPlanCatalog>();
            services.AddSingleton<IPlanCatalog, CommunityPlanCatalog>();
        }));
        using var client = communityFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organizations/personal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = JsonContent.Create(new { name = "Community personal", slug = "community-personal", currency = "USD" });
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CommunityPlanCatalog.PlanKey, json.RootElement.GetProperty("plan").GetString());
    }

    [Fact]
    public async Task OwnershipTransfer_PersonalIsRejected_WorkTransferRemovesLink()
    {
        var workId = await CreateWork("transfer-work");
        using var created = await Personal(link: workId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var personalId = int.Parse(json.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
        var targetIds = new List<int>();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            foreach (var id in new[] { personalId, workId })
            {
                var member = new CompanyMember { CompanyId = id, UserId = "new-owner", Role = CompanyMemberRole.Member, Status = CompanyMemberStatus.Active };
                db.CompanyMembers.Add(member);
                await db.SaveChangesAsync();
                targetIds.Add(member.Id);
            }
        }
        using var denied = await Send(HttpMethod.Post, "/api/organizations/current/transfer-ownership", new { memberId = targetIds[0] }, "personal-books");
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Contains("personal_workspace_not_transferable", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var transferred = await Send(HttpMethod.Post, "/api/organizations/current/transfer-ownership", new { memberId = targetIds[1] }, "transfer-work");
        Assert.True(transferred.StatusCode == HttpStatusCode.NoContent, await transferred.Content.ReadAsStringAsync());
        using var checkScope = _factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await check.WorkspaceLinks.ToListAsync());
    }

    private sealed class PersonalTestCatalog : IPlanCatalog
    {
        public const string Key = "household-test";
        private readonly CommunityPlanCatalog _community = new();
        public string DefaultPlanKey => _community.DefaultPlanKey;
        public string? PersonalBooksPlanKey => Key;
        public IReadOnlyList<PlanDefinitionDto> GetPlans() => _community.GetPlans();
        public bool TryResolvePlan(string? key, out PlanDefinitionDto plan)
        {
            if (key == Key)
            {
                plan = new() { Key = Key, Name = "Household", IncludedModules = new() { "financials", "purchases", "reports" } };
                return true;
            }
            return _community.TryResolvePlan(key, out plan);
        }
        public PlanDefinitionDto ResolveStoredPlan(string? key)
        { TryResolvePlan(key, out var plan); return plan; }
    }
}
