// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

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
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AccountGo.BackendTests.Security
{
    /// <summary>
    /// Request-level tests for new-workspace setup at the signup and authenticated create boundaries:
    /// verified initialization, approved-journey scope (with no provisioning on rejection), and
    /// setup-off precedence. The setup switch is controlled only through trusted server configuration/env.
    /// </summary>
    [Collection("WorkspaceSetupHttp")]
    public sealed class WorkspaceSetupHttpTests : IAsyncLifetime
    {
        private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string OwnerUserId = "setup-owner-user";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_setup_http_" + Guid.NewGuid().ToString("N") + ".db");
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;

        public Task InitializeAsync()
        {
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
                builder.UseSetting("SECRET_KEY", SecretKey);
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");
                builder.ConfigureTestServices(services =>
                {
                    // Deterministic email delivery so signup commits (and the workspace persists) rather
                    // than rolling back on a flaky dev sender.
                    services.RemoveAll<Api.Service.IEmailSender>();
                    services.AddSingleton<Api.Service.IEmailSender, CapturingEmailSender>();
                });
            });
            _client = _factory.CreateClient();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", null);
            _client.Dispose();
            _factory.Dispose();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
            return Task.CompletedTask;
        }

        private async Task<(string Token, string UserId)> CreateUserAsync(string email, string? forcedUserId = null)
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            if (forcedUserId != null) user.Id = forcedUserId;
            Assert.True((await userManager.CreateAsync(user)).Succeeded, email);
            var security = scope.ServiceProvider.GetRequiredService<Services.Security.ISecurityService>();
            security.AddUser(email, email, "T", "U", new List<int>());
            var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
            return (token.AccessToken, user.Id);
        }

        private async Task<HttpResponseMessage> CreateOrg(string token, object body)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(body);
            return await _client.SendAsync(req);
        }

        private int CompanyCount(string slug)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            return db.Companies.IgnoreQueryFilters().Count(c => c.Slug == slug);
        }

        private (WorkspaceCollaborationState State, bool Initialized)? OperatingState(string slug)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var company = db.Companies.IgnoreQueryFilters().FirstOrDefault(c => c.Slug == slug);
            if (company == null) return null;
            var s = db.WorkspaceOperatingSettings.IgnoreQueryFilters().FirstOrDefault(x => x.CompanyId == company.Id);
            return s == null ? null : (s.CollaborationState, s.InitializedAtUtc != null);
        }

        private int FunctionalGrantCount(string slug)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var company = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug);
            return db.CompanyMemberSecurityRoles.IgnoreQueryFilters()
                .Count(r => r.CompanyMember.CompanyId == company.Id);
        }

        private Core.Domain.WorkspaceApprovalRuleKind? JournalRuleKind(string slug)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var company = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug);
            return db.WorkspaceApprovalRules.IgnoreQueryFilters()
                .Where(r => r.CompanyId == company.Id
                    && r.OperationKey == Core.Authorization.AccessOperationCatalog.FinancialsJournalPost)
                .Select(r => (Core.Domain.WorkspaceApprovalRuleKind?)r.RuleKind)
                .FirstOrDefault();
        }

        [Fact]
        public async Task Review_PersonalInitialization_MustNotKeepCorporateCloseTarget()
        {
            var previous = Environment.GetEnvironmentVariable("WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED");
            var previousApproval = EntityEquityMapping.TestApprovalOverride;
            try
            {
                EntityEquityMapping.TestApprovalOverride = true;
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED", "true");
                var (token, _) = await CreateUserAsync("personal-approved@example.com", forcedUserId: OwnerUserId);
                var response = await CreateOrg(token, new
                {
                    name = "Personal fixture",
                    slug = "personal-profile-review",
                    plan = "accounting",
                    workspacePurpose = "Personal",
                    collaborationIntent = "JustMe",
                    entityType = "Corporation"
                });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var company = db.Companies.IgnoreQueryFilters().Single(c => c.Slug == "personal-profile-review");
                Assert.Equal(EntityType.Individual, db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id).EntityType);
                var closeId = db.GeneralLedgerSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id).PermanentAccountId;
                var closeCode = db.Accounts.IgnoreQueryFilters().Single(a => a.Id == closeId).AccountCode;
                Assert.Equal("30100", closeCode);
            }
            finally
            {
                EntityEquityMapping.TestApprovalOverride = previousApproval;
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED", previous);
            }
        }

        // ---- Authenticated create boundary --------------------------------------------------------

        [Fact]
        public async Task Create_User_BusinessJustMe_Admitted_AndInitializedSolo()
        {
            var (token, _) = await CreateUserAsync("owner@example.com", forcedUserId: OwnerUserId);
            var resp = await CreateOrg(token, new
            {
                name = "Solo Co",
                slug = "solo-co",
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var state = OperatingState("solo-co");
            Assert.NotNull(state);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, state!.Value.State);
            Assert.True(state.Value.Initialized);
        }

        [Fact]
        public async Task Create_User_TeamIntent_Admitted_AndInitializedAsTeamSetup()
        {
            var (token, _) = await CreateUserAsync("owner2@example.com", forcedUserId: OwnerUserId);
            var resp = await CreateOrg(token, new
            {
                name = "Team Co",
                slug = "team-co",
                workspacePurpose = "Business",
                collaborationIntent = "WithOthers"
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var state = OperatingState("team-co");
            Assert.NotNull(state);
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, state!.Value.State);
            Assert.True(state.Value.Initialized);
            Assert.Equal(Core.Domain.WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind("team-co"));
        }

        [Fact]
        public async Task Create_User_PersonalPurpose_Rejected_OutOfScope_AndNotInitialized()
        {
            var (token, _) = await CreateUserAsync("owner3@example.com", forcedUserId: OwnerUserId);
            var resp = await CreateOrg(token, new
            {
                name = "Personal Co",
                slug = "personal-co",
                workspacePurpose = "Personal",
                collaborationIntent = "WithOthers"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.SetupScopeNotAllowed, body, StringComparison.Ordinal);
            // Scope rejection happens before provisioning.
            Assert.Equal(0, CompanyCount("personal-co"));
        }

        // ---- Anonymous signup boundary ------------------------------------------------------------

        [Fact]
        public async Task Signup_WithAboutAnswers_DoesNotInitialize_BeforeVerification()
        {
            // Signup must not initialize the workspace: the account is unverified.
            var resp = await _client.PostAsync("/api/auth/signup", JsonContent.Create(new
            {
                email = "freelancer@example.com",
                firstName = "Free",
                organizationName = "Freelancer Books",
                organizationSlug = "freelancer-books",
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            }));

            // Signup must succeed (dev console email sender delivers in Development).
            var respBody = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"signup => {(int)resp.StatusCode} :: {respBody}");

            // The workspace exists (name/slug prefill reserved) ...
            var state = OperatingState("freelancer-books");
            Assert.NotNull(state);

            // ... but is unclassified and uninitialized: no policy was established before verification.
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, state!.Value.State);
            Assert.False(state.Value.Initialized);

            // No functional access was granted to anyone (the owner has no managed/functional role).
            Assert.Equal(0, FunctionalGrantCount("freelancer-books"));

            // The journal rule remains the seeded independent-review default — no single-actor grant.
            Assert.Equal(Core.Domain.WorkspaceApprovalRuleKind.IndependentReviewerRequired,
                JournalRuleKind("freelancer-books"));
        }

        // ---- Post-verification continuation (slice 5 dependency) ----------------------------------

        [Fact]
        public async Task PendingWorkspace_InitializesAfterVerification_ViaExistingWorkspace_NotDuplicate()
        {
            const string email = "continuation@example.com";
            const string slug = "continuation-books";
            // 1. Signup reserves the slug and creates the workspace unclassified (no init yet).
            var signup = await _client.PostAsync("/api/auth/signup", JsonContent.Create(new
            {
                email, firstName = "Cont", organizationName = "Continuation Books",
                organizationSlug = slug, workspacePurpose = "Business", collaborationIntent = "JustMe"
            }));
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, OperatingState(slug)!.Value.State);

            // 2. Simulate verified sign-in: confirm the email and mint a token for the owner.
            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByEmailAsync(email);
                Assert.NotNull(user);
                user!.EmailConfirmed = true;
                await userManager.UpdateAsync(user);
                token = (await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
            }

            // 3. Continuation: initialize the EXISTING workspace (no second workspace, no legacy flow).
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations/current/initialize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-AccountGo-Org", slug);
            req.Content = JsonContent.Create(new { workspacePurpose = "Business", collaborationIntent = "JustMe" });
            var resp = await _client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"initialize => {(int)resp.StatusCode} :: {body}");

            // Still exactly one workspace; now SoloActive with Full access and the single-actor rule.
            Assert.Equal(1, CompanyCount(slug));
            var state = OperatingState(slug);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, state!.Value.State);
            Assert.True(state.Value.Initialized);
            Assert.True(FunctionalGrantCount(slug) >= 1);
            Assert.Equal(Core.Domain.WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));

            // 4. A second continuation attempt does not re-initialize or duplicate.
            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/organizations/current/initialize");
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req2.Headers.Add("X-AccountGo-Org", slug);
            req2.Content = JsonContent.Create(new { workspacePurpose = "Business", collaborationIntent = "JustMe" });
            var resp2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp2.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
        }

        // ---- Pending-signup eligibility: team/legacy workspaces must not be initializable ---------

        [Fact]
        public async Task InitializeCurrent_PristineNonSignupWorkspace_Rejected_NoGrantsNoRuleChange()
        {
            // A pristine, uninitialized, sole-owner workspace created OUTSIDE the signup flow (so it has
            // generic origin evidence but NOT the signup pending-continuation marker). The verified
            // original owner must still be rejected at the continuation endpoint: generic origin
            // evidence does not prove the workspace is awaiting the approved signup continuation.
            const string slug = "pristine-nonsignup";
            const string ownerId = "pristine-owner-id";
            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var owner = new ApplicationUser { Id = ownerId, UserName = "pristine@example.com", Email = "pristine@example.com", EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(owner)).Succeeded);
                sp.GetRequiredService<Services.Security.ISecurityService>().AddUser("pristine@example.com", "pristine@example.com", "P", "O", new List<int>());

                // Create WITHOUT the signup continuation marker (the default) — i.e. not the signup flow.
                var workspaces = sp.GetRequiredService<Api.Service.WorkspaceService>();
                var planKey = sp.GetRequiredService<Services.Billing.IPlanCatalog>().DefaultPlanKey;
                workspaces.Create(ownerId, "Pristine Co", slug, planKey, "USD");

                token = (await sp.GetRequiredService<IAuthenticationService>().CreateToken(owner, true)).AccessToken;
            }

            // Sanity: the workspace is pristine and uninitialized, just lacking the signup marker.
            var stateBefore = OperatingState(slug);
            Assert.NotNull(stateBefore);
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, stateBefore!.Value.State);
            Assert.False(stateBefore.Value.Initialized);
            var grantsBefore = FunctionalGrantCount(slug);
            var ruleBefore = JournalRuleKind(slug);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations/current/initialize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-AccountGo-Org", slug);
            req.Content = JsonContent.Create(new { workspacePurpose = "Business", collaborationIntent = "JustMe" });
            var resp = await _client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            Assert.True(resp.StatusCode == HttpStatusCode.UnprocessableEntity, $"initialize => {(int)resp.StatusCode} :: {body}");
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.NotPendingSignupWorkspace, body, StringComparison.Ordinal);

            // Grants, journal rule, and initialization state are all preserved.
            Assert.Equal(grantsBefore, FunctionalGrantCount(slug));
            Assert.Equal(ruleBefore, JournalRuleKind(slug));
            Assert.Equal(Core.Domain.WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
            Assert.False(OperatingState(slug)!.Value.Initialized);
        }

        [Fact]
        public async Task InitializeCurrent_TeamWorkspaceWithMembers_Rejected_NoGrantsNoRuleChange()
        {
            const string slug = "team-not-eligible";
            // Setup is enabled, so only the eligibility proof can reject.
            const string ownerId = "team-owner-id";
            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var owner = new ApplicationUser { Id = ownerId, UserName = "teamowner@example.com", Email = "teamowner@example.com", EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(owner)).Succeeded);
                sp.GetRequiredService<Services.Security.ISecurityService>().AddUser("teamowner@example.com", "teamowner@example.com", "T", "O", new List<int>());

                var second = new ApplicationUser { Id = "team-second-id", UserName = "second@example.com", Email = "second@example.com", EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(second)).Succeeded);

                // Build an application-created workspace (origin evidence written) then mutate it into an
                // uninitialized TEAM workspace: a second active member and EverTeamActivatedAtUtc set.
                var workspaces = sp.GetRequiredService<Api.Service.WorkspaceService>();
                var planKey = sp.GetRequiredService<Services.Billing.IPlanCatalog>().DefaultPlanKey;
                var company = workspaces.Create(ownerId, "Team Co", slug, planKey, "USD");

                var db = sp.GetRequiredService<ApiDbContext>();
                db.SuppressTenantSecurityCheck = true;
                db.CompanyMembers.Add(new CompanyMember
                {
                    CompanyId = company.Id,
                    UserId = second.Id,
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                });
                var settings = db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
                settings.EverTeamActivatedAtUtc = DateTime.UtcNow; // historical team activation marker
                db.SaveChanges();

                token = (await sp.GetRequiredService<IAuthenticationService>().CreateToken(owner, true)).AccessToken;
            }

            var grantsBefore = FunctionalGrantCount(slug);
            var ruleBefore = JournalRuleKind(slug);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations/current/initialize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-AccountGo-Org", slug);
            req.Content = JsonContent.Create(new { workspacePurpose = "Business", collaborationIntent = "JustMe" });
            var resp = await _client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            // The team workspace must be rejected — it is not a pending-signup solo continuation.
            Assert.True(resp.StatusCode == HttpStatusCode.UnprocessableEntity, $"initialize => {(int)resp.StatusCode} :: {body}");
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.NotPendingSignupWorkspace, body, StringComparison.Ordinal);

            // Nothing was granted and the approval rule was not relaxed.
            Assert.Equal(grantsBefore, FunctionalGrantCount(slug));
            Assert.Equal(ruleBefore, JournalRuleKind(slug));
            Assert.Equal(Core.Domain.WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
            Assert.False(OperatingState(slug)!.Value.Initialized);
        }

        // ---- Setup-off precedence -----------------------------------------------------------------

        [Fact]
        public async Task Create_WhenSetupDisabled_ReportsDisabled()
        {
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "false");
            try
            {
                // New factory so the disabled flag is read at startup/options resolution.
                var offDbPath = Path.Combine(Path.GetTempPath(), "accountgo_setup_off_" + Guid.NewGuid().ToString("N") + ".db");
                using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting("DatabaseProvider", "Sqlite");
                    builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + offDbPath);
                    builder.UseSetting("SECRET_KEY", SecretKey);
                    builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                    builder.UseSetting("PUBLIC_API_URL", "https://api.example");
                });
                using var client = factory.CreateClient();

                using var scope = factory.Services.CreateScope();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { Id = OwnerUserId + "-off", UserName = "off@example.com", Email = "off@example.com", EmailConfirmed = true };
                await userManager.CreateAsync(user);
                scope.ServiceProvider.GetRequiredService<Services.Security.ISecurityService>().AddUser("off@example.com", "off@example.com", "O", "F", new List<int>());
                var token = (await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;

                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = JsonContent.Create(new { name = "Off Co", slug = "off-co", workspacePurpose = "Business", collaborationIntent = "JustMe" });
                var resp = await client.SendAsync(req);

                Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
                var body = await resp.Content.ReadAsStringAsync();
                Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.SoloSetupDisabled, body, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");
            }
        }

        /// <summary>Deterministic email sink so signup commits in tests instead of rolling back.</summary>
        private sealed class CapturingEmailSender : Api.Service.IEmailSender
        {
            public Task<string?> SendAsync(Api.Service.EmailMessage message, CancellationToken ct = default)
                => Task.FromResult<string?>("captured");
        }
    }

    [CollectionDefinition("WorkspaceSetupHttp", DisableParallelization = true)]
    public sealed class WorkspaceSetupHttpCollection { }
}
