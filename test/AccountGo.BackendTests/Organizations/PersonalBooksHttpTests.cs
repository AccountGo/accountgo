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
