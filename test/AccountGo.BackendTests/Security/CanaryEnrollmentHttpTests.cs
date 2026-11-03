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
    /// Request-level tests for the server-side canary at the signup and authenticated create boundaries:
    /// enrollment rejection (with no provisioning), verified admission, approved-journey scope, and
    /// setup-off precedence. Enrollment is controlled only through trusted server configuration/env.
    /// </summary>
    [Collection("CanaryEnrollmentHttp")]
    public sealed class CanaryEnrollmentHttpTests : IAsyncLifetime
    {
        private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string EnrolledUserId = "canary-enrolled-user";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_canary_http_" + Guid.NewGuid().ToString("N") + ".db");
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;

        public Task InitializeAsync()
        {
            // Trusted enrollment comes from environment only. Enroll one known user id; nobody else.
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", EnrolledUserId);
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", "");

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
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", null);
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", null);
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

        // ---- Authenticated create boundary --------------------------------------------------------

        [Fact]
        public async Task Create_UnenrolledUser_WithAboutAnswers_Rejected_AndNotInitialized()
        {
            var (token, _) = await CreateUserAsync("unenrolled@example.com"); // random id, not enrolled
            var resp = await CreateOrg(token, new
            {
                name = "Unenrolled Co",
                slug = "unenrolled-co",
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.CanaryNotEnrolled, body, StringComparison.Ordinal);

            // No provisioning on rejection: the workspace was not created.
            Assert.Equal(0, CompanyCount("unenrolled-co"));
        }

        [Fact]
        public async Task Create_EnrolledUser_BusinessJustMe_Admitted_AndInitializedSolo()
        {
            var (token, _) = await CreateUserAsync("enrolled@example.com", forcedUserId: EnrolledUserId);
            var resp = await CreateOrg(token, new
            {
                name = "Enrolled Co",
                slug = "enrolled-co",
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var state = OperatingState("enrolled-co");
            Assert.NotNull(state);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, state!.Value.State);
            Assert.True(state.Value.Initialized);
        }

        [Fact]
        public async Task Create_EnrolledUser_TeamIntent_Rejected_OutOfScope_AndNotInitialized()
        {
            var (token, _) = await CreateUserAsync("enrolled2@example.com", forcedUserId: EnrolledUserId);
            var resp = await CreateOrg(token, new
            {
                name = "Enrolled Team Co",
                slug = "enrolled-team-co",
                workspacePurpose = "Business",
                collaborationIntent = "WithOthers"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.CanaryScopeNotAllowed, body, StringComparison.Ordinal);
            // Scope rejection happens before provisioning.
            Assert.Equal(0, CompanyCount("enrolled-team-co"));
        }

        // ---- Anonymous signup boundary ------------------------------------------------------------

        [Fact]
        public async Task Signup_WithAboutAnswers_DoesNotInitialize_BeforeVerification()
        {
            // Even an enrolled *email* must not cause initialization at signup: the account is unverified.
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", "freelancer@example.com");
            try
            {
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
            finally
            {
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", "");
            }
        }

        // ---- Post-verification continuation (slice 5 dependency) ----------------------------------

        [Fact]
        public async Task PendingWorkspace_InitializesAfterVerification_ViaExistingWorkspace_NotDuplicate()
        {
            const string email = "continuation@example.com";
            const string slug = "continuation-books";
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", email);
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", EnrolledUserId);
            try
            {
                // 1. Signup reserves the slug and creates the workspace unclassified (no init yet).
                var signup = await _client.PostAsync("/api/auth/signup", JsonContent.Create(new
                {
                    email, firstName = "Cont", organizationName = "Continuation Books",
                    organizationSlug = slug, workspacePurpose = "Business", collaborationIntent = "JustMe"
                }));
                Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
                Assert.Equal(1, CompanyCount(slug));
                Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, OperatingState(slug)!.Value.State);

                // 2. Simulate verified sign-in: confirm the email and mint a token for the owner. Enroll the
                //    resolved user id too (operators enroll the verified identity for the authenticated path).
                string token;
                using (var scope = _factory.Services.CreateScope())
                {
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    var user = await userManager.FindByEmailAsync(email);
                    Assert.NotNull(user);
                    user!.EmailConfirmed = true;
                    await userManager.UpdateAsync(user);
                    Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", user.Id);
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
            finally
            {
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", "");
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", EnrolledUserId);
            }
        }

        // ---- Setup-off precedence -----------------------------------------------------------------

        [Fact]
        public async Task Create_WhenSetupDisabled_ReportsDisabled_EvenForEnrolledUser()
        {
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "false");
            try
            {
                // New factory so the disabled flag is read at startup/options resolution.
                var offDbPath = Path.Combine(Path.GetTempPath(), "accountgo_canary_off_" + Guid.NewGuid().ToString("N") + ".db");
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
                var user = new ApplicationUser { Id = EnrolledUserId + "-off", UserName = "off@example.com", Email = "off@example.com", EmailConfirmed = true };
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

    [CollectionDefinition("CanaryEnrollmentHttp", DisableParallelization = true)]
    public sealed class CanaryEnrollmentHttpCollection { }
}
