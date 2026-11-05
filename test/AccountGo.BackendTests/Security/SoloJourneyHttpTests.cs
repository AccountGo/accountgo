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
using System.Text.RegularExpressions;
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
    /// End-to-end business/freelance "Just me" solo-owner journey over real HTTP, through the actual
    /// signup, delivered-OTP verification, continuation initialization, journal prepare/post, entitlement
    /// and invitation-lifecycle endpoints.
    ///
    /// The OTP is read from the deterministic email sink exactly as it was delivered; nothing here sets
    /// EmailConfirmed directly or mints a token that claims to prove the verification journey. The only
    /// test-specific configuration is the isolated WorkspaceExperience setup switch (trusted server config),
    /// which production leaves disabled.
    /// </summary>
    [Collection("SoloJourneyHttp")]
    public sealed class SoloJourneyHttpTests : IAsyncLifetime
    {
        private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_solo_journey_" + Guid.NewGuid().ToString("N") + ".db");
        private readonly CapturingEmailSender _emails = new();
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;

        // Process-global meter; the collection is serialized (DisableParallelization), and onboarding
        // assertions below measure DELTAS around a specific action rather than absolute totals, since the
        // counter tags carry no company id.
        private readonly object _meterLock = new();
        private readonly List<(string Name, string? Flow, string? Status, long Value)> _onboarding = new();
        private System.Diagnostics.Metrics.MeterListener _meterListener = null!;

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
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(_emails);
                });
            });
            _client = _factory.CreateClient();

            _meterListener = new System.Diagnostics.Metrics.MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == Core.Authorization.WorkspaceOperatingPolicyMetrics.MeterName
                        && (instrument.Name == "workspace.onboarding.starts" || instrument.Name == "workspace.onboarding.completions"))
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                string? flow = null, status = null;
                foreach (var t in tags)
                {
                    if (t.Key == "flow") flow = t.Value as string;
                    else if (t.Key == "status") status = t.Value as string;
                }
                lock (_meterLock) _onboarding.Add((instrument.Name, flow, status, measurement));
            });
            _meterListener.Start();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", null);
            _meterListener.Dispose();
            _client.Dispose();
            _factory.Dispose();
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
            return Task.CompletedTask;
        }

        // Onboarding counter deltas for the new-workspace flow, captured around a single action.
        private long NewWorkspaceStarts()
        {
            lock (_meterLock)
                return _onboarding.Where(m => m.Name == "workspace.onboarding.starts"
                    && m.Flow == "new_workspace").Sum(m => m.Value);
        }

        private long NewWorkspaceCompletions(string status)
        {
            lock (_meterLock)
                return _onboarding.Where(m => m.Name == "workspace.onboarding.completions"
                    && m.Flow == "new_workspace" && m.Status == status).Sum(m => m.Value);
        }

        // ---- the real signup -> delivered OTP -> verification -> continuation journey ----------------

        [Fact]
        public async Task CompanyContinuation_ExistingPolicyEndpointAppliesLegalFormBeforeInitialization()
        {
            const string email = "company-form@example.com";
            const string slug = "company-form-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Pat", "Company Form", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);
            Assert.False(OperatingState(slug)!.Value.Initialized);
            using var policy = await SendScoped(HttpMethod.Put, "/api/organizations/current/accounting-policy", token, slug,
                new { entityType = "Corporation" });
            Assert.True(policy.StatusCode == HttpStatusCode.OK, await policy.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);
            using var currentPolicy = await SendScoped(HttpMethod.Get, "/api/organizations/current/accounting-policy", token, slug, null);
            var body = await Json(currentPolicy);
            Assert.Equal("Corporation", body.GetProperty("entityType").GetString());
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var companyId = db.Companies.Single(c => c.Slug == slug).Id;
            var closeAccountId = db.GeneralLedgerSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId).PermanentAccountId;
            Assert.Equal("30300", db.Accounts.IgnoreQueryFilters().Single(a => a.Id == closeAccountId).AccountCode);
            Assert.Equal("Share Capital", db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == companyId && a.AccountCode == "30100").AccountName);
            Assert.Equal(1, CompanyCount(slug));
        }

        [Fact]
        public async Task RegisteredOnePersonBusiness_SignupVerifyContinue_InitializesExactlyOneSoloWorkspace()
        {
            const string email = "solo-owner@example.com";
            const string slug = "solo-owner-books";

            // 1. Anonymous signup with the About answers. The workspace is reserved but must stay
            //    unclassified before the email is verified.
            var signup = await Signup(email, "Maria", "Solo Owner Books", slug);
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, OperatingState(slug)!.Value.State);
            Assert.False(OperatingState(slug)!.Value.Initialized);
            Assert.Equal(0, FunctionalGrantCount(slug));

            // 2. Verify the REAL delivered OTP.
            var (token, _) = await VerifyRealOtp(email);

            // 3. Continuation initializes the EXISTING workspace: no duplicate, now SoloActive with Full
            //    access and the single-actor journal rule.
            var init = await InitializeCurrent(token, slug, "Business", "JustMe");
            Assert.Equal(HttpStatusCode.OK, init.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
            var state = OperatingState(slug)!.Value;
            Assert.Equal(WorkspaceCollaborationState.SoloActive, state.State);
            Assert.True(state.Initialized);
            Assert.True(FunctionalGrantCount(slug) >= 1);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));
        }

        [Fact]
        public async Task UnregisteredFreelancer_SignupWithoutOrg_ThenCreateAndInitialize_IsSoloActive()
        {
            // A freelancer without a business name at signup: verify first (email-only signup), then use the
            // authenticated create-and-initialize path. Exercises journey path 3 (authenticated create).
            const string email = "freelancer@example.com";
            var signup = await Signup(email, "Sam", organizationName: null, slug: null);
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);

            var (token, _) = await VerifyRealOtp(email);

            const string slug = "freelance-books";
            var create = await CreateOrg(token, new
            {
                name = "Freelance Books",
                slug,
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var state = OperatingState(slug)!.Value;
            Assert.Equal(WorkspaceCollaborationState.SoloActive, state.State);
            Assert.True(state.Initialized);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));
        }

        // ---- repeat initialization cannot duplicate grants or overwrite policy ------------------------

        [Fact]
        public async Task RepeatContinuation_AfterInitialized_DoesNotDuplicateGrantsOrOverwritePolicy()
        {
            const string email = "repeat-owner@example.com";
            const string slug = "repeat-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rae", "Repeat Books", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);
            var grantsAfterFirst = FunctionalGrantCount(slug);
            var revisionAfterFirst = PolicyRevision(slug);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));

            // A second initialization must be rejected and must not add grants or bump the policy revision.
            var second = await InitializeCurrent(token, slug, "Business", "JustMe");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
            Assert.Equal(grantsAfterFirst, FunctionalGrantCount(slug));
            Assert.Equal(revisionAfterFirst, PolicyRevision(slug));
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));
        }

        // ---- missing continuation evidence / disqualifying history reject without elevation -----------

        [Fact]
        public async Task Continuation_WithoutSignupMarker_Rejected_NoElevation()
        {
            // A verified owner whose pristine workspace was created OUTSIDE the signup flow (no
            // signup-continuation marker) must be rejected without any grant or policy change.
            const string email = "nomarker@example.com";
            const string slug = "nomarker-books";
            var (token, userId) = await CreateVerifiedUser(email);
            using (var scope = _factory.Services.CreateScope())
            {
                var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
                var planKey = scope.ServiceProvider.GetRequiredService<Services.Billing.IPlanCatalog>().DefaultPlanKey;
                // markPendingSignupContinuation defaults false — i.e. not the signup flow.
                workspaces.Create(userId, "No Marker Books", slug, planKey, "USD");
            }

            var grantsBefore = FunctionalGrantCount(slug);
            var resp = await InitializeCurrent(token, slug, "Business", "JustMe");
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.UnprocessableEntity, body);
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.NotPendingSignupWorkspace, body, StringComparison.Ordinal);
            Assert.Equal(grantsBefore, FunctionalGrantCount(slug));
            Assert.False(OperatingState(slug)!.Value.Initialized);
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
        }

        // ---- supported scope enforced server-side --------------------------------

        [Fact]
        public async Task Create_User_BusinessWithOthers_InitializesTeamSetup_WithOwnerFullAccessAndIndependentReview()
        {
            var (token, userId) = await CreateVerifiedUser("team-journey@example.com");

            var resp = await CreateOrg(token, new
            {
                name = "Team Journey",
                slug = "team-journey",
                workspacePurpose = "Business",
                collaborationIntent = "WithOthers"
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var state = OperatingState("team-journey")!.Value;
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, state.State);
            Assert.True(state.Initialized);
            // Owner keeps the managed Full workspace access profile, so there is no self-approval dead end for
            // access; journals still need an independent reviewer once teammates join.
            Assert.True(OwnerFunctionalGrantCount("team-journey", userId) >= 1);
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind("team-journey"));
        }

        [Fact]
        public async Task Create_User_Personal_StaysRejected_ForSoloAndTeam()
        {
            var (token, userId) = await CreateVerifiedUser("personal-journey@example.com");

            foreach (var intent in new[] { "JustMe", "WithOthers" })
            {
                var slug = "personal-journey-" + intent.ToLowerInvariant();
                var resp = await CreateOrg(token, new
                {
                    name = "Personal Journey",
                    slug,
                    workspacePurpose = "Personal",
                    collaborationIntent = intent
                });
                Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
                Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.SetupScopeNotAllowed,
                    await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                Assert.Equal(0, CompanyCount(slug));
            }
        }

        [Fact]
        public async Task Continuation_BusinessWithOthers_InitializesTheExistingWorkspaceAsTeamSetup()
        {
            const string email = "team-continuation@example.com";
            const string slug = "team-continuation-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Tess", "Team Continuation", slug)).StatusCode);
            var userId = await ResolveUserId(email);
            var (token, _) = await VerifyRealOtp(email);

            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "WithOthers")).StatusCode);

            Assert.Equal(1, CompanyCount(slug));
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, OperatingState(slug)!.Value.State);
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
            Assert.True(OwnerFunctionalGrantCount(slug, userId) >= 1);
        }

        private async Task<(string Token, string UserId, string Slug)> CreateTeamWorkspace(string email, string slug, string? ownerAccessPreset = null)
        {
            var (token, userId) = await CreateVerifiedUser(email);
            var resp = await CreateOrg(token, new
            {
                name = slug,
                slug,
                workspacePurpose = "Business",
                collaborationIntent = "WithOthers",
                ownerAccessPreset
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            return (token, userId, slug);
        }

        private async Task<JsonElement> OperatingSettings(string token, string slug) =>
            await Json(await SendScoped(HttpMethod.Get, "/api/organizations/current/operating-settings", token, slug, null));

        [Fact]
        public async Task ReturnToSolo_UnderSoloSetupEnabled_WithoutLegacyFlag_PreservesGrantsAndJournalPolicy()
        {
            var (token, userId, slug) = await CreateTeamWorkspace("rts-owner@example.com", "rts-books");
            var grantsBefore = OwnerFunctionalGrantCount(slug, userId);
            var settings = await OperatingSettings(token, slug);
            Assert.Contains("return_to_solo", settings.GetProperty("allowedNextActions").EnumerateArray().Select(a => a.GetString()));
            var revision = settings.GetProperty("policyRevision").GetInt64();

            var response = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision, revokeOutstandingInvitations = false });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);
            Assert.Equal("SoloActive", body.GetProperty("collaborationState").GetString());
            Assert.True(body.GetProperty("policyRevision").GetInt64() > revision);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, OperatingState(slug)!.Value.State);
            Assert.Equal(grantsBefore, OwnerFunctionalGrantCount(slug, userId));
            // The independent-review rule is preserved, never relaxed by returning to solo.
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
            Assert.Contains("independent reviewer", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReturnToSolo_RejectsAStaleRevision_AndLeavesTheWorkspaceInTeamSetup()
        {
            var (token, _, slug) = await CreateTeamWorkspace("rts-stale@example.com", "rts-stale-books");
            var revision = (await OperatingSettings(token, slug)).GetProperty("policyRevision").GetInt64();

            var response = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision + 4, revokeOutstandingInvitations = false });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, OperatingState(slug)!.Value.State);
        }

        [Fact]
        public async Task ReturnToSolo_WithOutstandingInvitation_RequiresRevokeFlag()
        {
            var (token, _, slug) = await CreateTeamWorkspace("rts-invite@example.com", "rts-invite-books");
            Assert.Equal(HttpStatusCode.Created, (await SendScoped(HttpMethod.Post, "/api/organizations/current/invitations", token, slug,
                new { email = "later@example.com", role = "Member", securityRoleIds = Array.Empty<int>() })).StatusCode);
            var revision = (await OperatingSettings(token, slug)).GetProperty("policyRevision").GetInt64();

            var refused = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision, revokeOutstandingInvitations = false });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(Core.Authorization.WorkspacePolicyReasonCodes.ApprovalPolicyChangeRequired,
                (await Json(refused)).GetProperty("reasonCode").GetString());
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, OperatingState(slug)!.Value.State);

            var accepted = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision, revokeOutstandingInvitations = true });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var invitations = await Json(await SendScoped(HttpMethod.Get, "/api/organizations/current/invitations", token, slug, null));
            Assert.Empty(invitations.EnumerateArray());
        }

        [Fact]
        public async Task ReturnToSolo_WithAnotherActiveMember_IsRefused_AndNeverInfersAGrantFromMemberCount()
        {
            var (token, userId, slug) = await CreateTeamWorkspace("rts-team@example.com", "rts-team-books");
            var other = await CreateVerifiedUser("rts-mate@example.com");
            var companyId = CompanyId(slug);
            WithDb<object?>(db =>
            {
                db.CompanyMembers.Add(new Core.Domain.CompanyMember
                {
                    CompanyId = companyId, UserId = other.UserId, Role = CompanyMemberRole.Member,
                    Status = Core.Domain.CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow
                });
                db.SaveChanges();
                return null;
            });
            var grantsBefore = FunctionalGrantCount(slug);
            var revision = (await OperatingSettings(token, slug)).GetProperty("policyRevision").GetInt64();

            var response = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision, revokeOutstandingInvitations = true });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(grantsBefore, FunctionalGrantCount(slug));
            Assert.True(OwnerFunctionalGrantCount(slug, userId) >= 1);
            Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, OperatingState(slug)!.Value.State);
        }

        [Fact]
        public async Task ReturnToSolo_ForNarrowerOwner_DoesNotElevateTheSoleMember()
        {
            var (token, userId, slug) = await CreateTeamWorkspace("rts-narrow@example.com", "rts-narrow-books", "Narrower");
            Assert.Equal(0, OwnerFunctionalGrantCount(slug, userId));
            var revision = (await OperatingSettings(token, slug)).GetProperty("policyRevision").GetInt64();

            var response = await SendScoped(HttpMethod.Post, "/api/organizations/current/operating-settings/return-to-solo", token, slug,
                new { expectedPolicyRevision = revision, revokeOutstandingInvitations = false });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Sole membership never implies a functional grant or a relaxed approval rule.
            Assert.Equal(0, OwnerFunctionalGrantCount(slug, userId));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, JournalRuleKind(slug));
        }

        // ---- first useful task: the accounting-entitled solo owner posts a balanced journal -----------

        [Fact]
        public async Task SoloOwner_PreparesAndPostsBalancedJournal_UnderSingleActor_WithTruthfulEvidence()
        {
            var (token, userId, slug) = await CreateInitializedSoloWorkspace("poster@example.com", "poster-books");

            // Prepare (draft) a balanced manual journal through the real endpoint.
            var cash = AccountId(slug, "10111");
            var revenue = AccountId(slug, "40100");
            var draft = new
            {
                journalDate = DateTime.UtcNow.Date.AddDays(-1),
                voucherType = 6,
                memo = "Owner-prepared adjustment",
                journalEntryLines = new object[]
                {
                    new { accountId = cash, drCr = 1, amount = 25m },
                    new { accountId = revenue, drCr = 2, amount = 25m }
                }
            };
            var save = await SendScoped(HttpMethod.Post, "/api/Financials/SaveJournalEntry", token, slug, draft);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var journalId = (await Json(save)).GetProperty("id").GetInt32();

            // Post as the SAME user with NO independent-approval headers: allowed because the workspace
            // policy is SingleActorAllowed.
            var postBody = new
            {
                id = journalId,
                journalDate = draft.journalDate,
                voucherType = 6,
                memo = draft.memo,
                journalEntryLines = draft.journalEntryLines
            };
            var post = await SendScoped(HttpMethod.Post, "/api/Financials/PostJournalEntry", token, slug, postBody);
            Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());

            // Balanced ledger lines persisted.
            var (debit, credit, lineCount) = PostedLedgerTotals(slug, journalId);
            Assert.Equal(25m, debit);
            Assert.Equal(25m, credit);
            Assert.True(lineCount >= 2);

            // Truthful authorization evidence: same preparer/poster, single-user workflow, no independent
            // review claimed.
            var evidence = PostingAuthorizationEvidence(slug, journalId);
            Assert.NotNull(evidence);
            using var doc = JsonDocument.Parse(evidence!);
            var root = doc.RootElement;
            Assert.Equal("SingleActorAllowed", root.GetProperty("actualReviewMode").GetString());
            Assert.False(root.GetProperty("independentReviewUsed").GetBoolean());
            Assert.Equal(userId, root.GetProperty("preparerUserId").GetString());
            Assert.Equal(userId, root.GetProperty("posterUserId").GetString());
            Assert.Contains("single-user workflow", root.GetProperty("rendering").GetString(), StringComparison.Ordinal);
        }

        // ---- plan-limited workspace cannot post; entitlement removed after draft; nothing persisted ---

        [Fact]
        public async Task EntitlementRemovedAfterDraft_PostRejected_NoLedgerOrPostingEvidence()
        {
            var (token, _, slug) = await CreateInitializedSoloWorkspace("limited@example.com", "limited-books");

            var cash = AccountId(slug, "10111");
            var revenue = AccountId(slug, "40100");
            var draft = new
            {
                journalDate = DateTime.UtcNow.Date.AddDays(-1),
                voucherType = 6,
                memo = "Draft before downgrade",
                journalEntryLines = new object[]
                {
                    new { accountId = cash, drCr = 1, amount = 25m },
                    new { accountId = revenue, drCr = 2, amount = 25m }
                }
            };
            var save = await SendScoped(HttpMethod.Post, "/api/Financials/SaveJournalEntry", token, slug, draft);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var journalId = (await Json(save)).GetProperty("id").GetInt32();

            // Remove the financials entitlement after the draft exists (plan downgrade / module opt-out).
            DisableModule(slug, "financials");

            var postBody = new
            {
                id = journalId,
                journalDate = draft.journalDate,
                voucherType = 6,
                memo = draft.memo,
                journalEntryLines = draft.journalEntryLines
            };
            var post = await SendScoped(HttpMethod.Post, "/api/Financials/PostJournalEntry", token, slug, postBody);
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

            // No ledger lines and no posting-authorization evidence were persisted.
            Assert.Equal(0, PostedLedgerHeaderCount(slug));
            Assert.Null(PostingAuthorizationEvidence(slug, journalId));
            Assert.False(JournalPosted(slug, journalId));
        }

        // ---- setup-disabled preserves an already-initialized workspace's grants and policy ------------

        [Fact]
        public async Task SetupDisabled_PreservesExistingInitializedWorkspacePolicyAndGrants()
        {
            var (_, _, slug) = await CreateInitializedSoloWorkspace("retain@example.com", "retain-books");
            var grantsBefore = FunctionalGrantCount(slug);
            var ruleBefore = JournalRuleKind(slug);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, ruleBefore);

            // Disabling new setup must not touch a workspace that is already initialized.
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "false");
            try
            {
                using var offFactory = NewFactory(out var offClient);
                using (offClient)
                {
                    // The existing stored policy and grants are unchanged under the setup-off host.
                    Assert.Equal(grantsBefore, FunctionalGrantCount(slug));
                    Assert.Equal(ruleBefore, JournalRuleKind(slug));
                    Assert.True(OperatingState(slug)!.Value.Initialized);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");
            }
        }

        // ---- invitation lifecycle preserves owner access and the established journal rule -------------

        [Fact]
        public async Task InvitationAcceptanceByReadOnlyAccountant_PreservesOwnerAccessAndJournalRule()
        {
            var (ownerToken, ownerId, slug) = await CreateInitializedSoloWorkspace("owner-inv@example.com", "owner-inv-books");
            var ownerGrantsBefore = OwnerFunctionalGrantCount(slug, ownerId);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));

            // Owner invites a read-only viewer.
            var viewerRoleId = SecurityRoleId("Auditor / Viewer");
            var invite = await SendScoped(HttpMethod.Post, "/api/organizations/current/invitations", ownerToken, slug,
                new { email = "accountant@example.com", role = "Member", securityRoleIds = new[] { viewerRoleId } });
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

            // The invitee accepts with their own verified identity.
            var (accountantToken, accountantId) = await CreateVerifiedUser("accountant@example.com");
            var token = InvitationToken();
            var accept = await SendTenantFree(HttpMethod.Post, "/api/invitations/" + token + "/accept", accountantToken);
            Assert.True(accept.IsSuccessStatusCode, await accept.Content.ReadAsStringAsync());

            // Owner access and the established single-actor journal rule are preserved; collaboration state
            // is recorded. Adding a read-only accountant does not relax independent review back on nor
            // revoke the owner's grants.
            Assert.Equal(ownerGrantsBefore, OwnerFunctionalGrantCount(slug, ownerId));
            Assert.True(ownerGrantsBefore >= 1, "owner retains its Full-access functional grant");
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));
            Assert.True(IsActiveMember(slug, accountantId));

            // The owner can still prepare and post under the single-actor rule after collaboration began.
            var cash = AccountId(slug, "10111");
            var revenue = AccountId(slug, "40100");
            var draft = new
            {
                journalDate = DateTime.UtcNow.Date.AddDays(-1),
                voucherType = 6,
                memo = "Owner still posts after invite",
                journalEntryLines = new object[]
                {
                    new { accountId = cash, drCr = 1, amount = 10m },
                    new { accountId = revenue, drCr = 2, amount = 10m }
                }
            };
            var save = await SendScoped(HttpMethod.Post, "/api/Financials/SaveJournalEntry", ownerToken, slug, draft);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var journalId = (await Json(save)).GetProperty("id").GetInt32();
            var post = await SendScoped(HttpMethod.Post, "/api/Financials/PostJournalEntry", ownerToken, slug, new
            {
                id = journalId,
                draft.journalDate,
                voucherType = 6,
                draft.memo,
                draft.journalEntryLines
            });
            Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MemberRemoval_PreservesApprovedPolicy_AndDoesNotAutoRelaxReview()
        {
            var (ownerToken, _, slug) = await CreateInitializedSoloWorkspace("owner-rm@example.com", "owner-rm-books");
            var viewerRoleId = SecurityRoleId("Auditor / Viewer");
            Assert.Equal(HttpStatusCode.Created, (await SendScoped(HttpMethod.Post, "/api/organizations/current/invitations", ownerToken, slug,
                new { email = "rm-member@example.com", role = "Member", securityRoleIds = new[] { viewerRoleId } })).StatusCode);
            var (memberToken, memberId) = await CreateVerifiedUser("rm-member@example.com");
            Assert.True((await SendTenantFree(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", memberToken)).IsSuccessStatusCode);

            var ruleBeforeRemoval = JournalRuleKind(slug);
            var memberCompanyId = MemberId(slug, memberId);
            var remove = await SendScoped(HttpMethod.Delete, "/api/organizations/current/members/" + memberCompanyId, ownerToken, slug, null);
            Assert.True(remove.IsSuccessStatusCode, await remove.Content.ReadAsStringAsync());

            // Removing the member must not silently change the journal rule nor auto-restore anything.
            Assert.Equal(ruleBeforeRemoval, JournalRuleKind(slug));
            Assert.False(IsActiveMember(slug, memberId));
        }

        [Fact]
        public async Task OwnershipTransfer_PreservesApprovedPolicy_AndDoesNotGrantAccessOrRelaxReview()
        {
            var (ownerToken, _, slug) = await CreateInitializedSoloWorkspace("owner-xfer@example.com", "owner-xfer-books");
            var accountantRoleId = SecurityRoleId("Accountant");
            Assert.Equal(HttpStatusCode.Created, (await SendScoped(HttpMethod.Post, "/api/organizations/current/invitations", ownerToken, slug,
                new { email = "new-owner@example.com", role = "Admin", securityRoleIds = new[] { accountantRoleId } })).StatusCode);
            var (newOwnerToken, newOwnerId) = await CreateVerifiedUser("new-owner@example.com");
            Assert.True((await SendTenantFree(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", newOwnerToken)).IsSuccessStatusCode);

            var ruleBefore = JournalRuleKind(slug);
            var targetMemberId = MemberId(slug, newOwnerId);
            var transfer = await SendScoped(HttpMethod.Post, "/api/organizations/current/transfer-ownership", ownerToken, slug,
                new { memberId = targetMemberId });
            Assert.True(transfer.IsSuccessStatusCode, await transfer.Content.ReadAsStringAsync());

            // The approved journal rule survives an ownership transfer unchanged; transfer does not relax
            // independent review or auto-grant beyond the explicit assignment.
            Assert.Equal(ruleBefore, JournalRuleKind(slug));
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, JournalRuleKind(slug));
        }

        // ---- onboarding measurement for the new-workspace flow -------------------------------------

        [Fact]
        public async Task SignupThenContinuation_RecordsDurableStartAtSignup_AndCompletionOnlyOnSuccess()
        {
            const string email = "measure-owner@example.com";
            const string slug = "measure-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Mea", "Measure Books", slug)).StatusCode);

            // The durable cohort START exists at the signup boundary — BEFORE verification or
            // initialization — and has no completion yet. This is what lets dropouts be counted.
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingCompletion"));

            var (token, _) = await VerifyRealOtp(email);

            var successBefore = NewWorkspaceCompletions("success");
            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);

            // Success emits the completion counter and writes exactly one durable completion — the start is
            // NOT re-written (it was recorded at signup), so there is exactly one start and one completion.
            Assert.Equal(successBefore + 1, NewWorkspaceCompletions("success"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingCompletion"));
        }

        [Fact]
        public async Task AbandonedSignup_LeavesDurableStart_WithNoCompletion()
        {
            // A signup whose owner never verifies/continues: the durable start persists with NO completion,
            // so cohort conversion (completions / starts) correctly counts this as a dropout and is not
            // structurally pinned to 100%.
            const string email = "measure-dropout@example.com";
            const string slug = "measure-dropout-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Drop", "Measure Dropout", slug)).StatusCode);

            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingCompletion"));
            Assert.False(OperatingState(slug)!.Value.Initialized);
        }

        [Fact]
        public async Task RejectedThenRetried_StartPersists_CompletionOnlyAfterEventualSuccess()
        {
            // Reproduces the reviewer's requested restart/resume + retry + eventual completion lifecycle.
            const string email = "measure-retry@example.com";
            const string slug = "measure-retry-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Ret", "Measure Retry", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            // First attempt rejected (out of scope): the durable start from signup persists, still no
            // completion — the cohort member is a not-yet-converted start.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await InitializeCurrent(token, slug, "Personal", "JustMe")).StatusCode);
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingCompletion"));

            // Retry with the correct in-scope answers eventually succeeds: completion is now recorded once,
            // and the start was not duplicated by the retry.
            var successBefore = NewWorkspaceCompletions("success");
            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);
            Assert.Equal(successBefore + 1, NewWorkspaceCompletions("success"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingCompletion"));
        }

        [Fact]
        public async Task Continuation_DoesNotChangeThePlan_PaidUpgradeGoesThroughBillingNotHere()
        {
            // Findings 1 & 4: the continuation must NOT change the plan. Plan upgrades (especially paid
            // tiers) go through the billing-gated subscription-change flow, which leaves the plan untouched
            // until payment is confirmed. The workspace is initialized on its existing (free/default) plan,
            // and no plan field sent to the endpoint has any effect.
            const string email = "measure-plan@example.com";
            const string slug = "measure-plan-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Pla", "Measure Plan", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            var planBefore = StoredPlan(slug);

            // Even if a client sends a plan (e.g. a paid tier), the endpoint ignores it: InitializeCurrentRequest
            // has no Plan field, so it binds to nothing and the stored plan is unchanged.
            var resp = await SendScoped(HttpMethod.Post, "/api/organizations/current/initialize", token, slug,
                new { workspacePurpose = "Business", collaborationIntent = "JustMe", plan = "enterprise" });
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, body);

            // The workspace is initialized (SoloActive) but the plan is exactly what it was before — the
            // free/default tier the workspace was created on. The response echoes that same stored plan.
            Assert.Equal(WorkspaceCollaborationState.SoloActive, OperatingState(slug)!.Value.State);
            Assert.Equal(planBefore, StoredPlan(slug));
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(StoredPlanResolvedKey(slug), doc.RootElement.GetProperty("plan").GetString());
        }

        [Fact]
        public async Task RepeatInitialization_EmitsIneligibleCompletion_NotASecondSuccess()
        {
            const string email = "measure-repeat@example.com";
            const string slug = "measure-repeat-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rep", "Measure Repeat", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);
            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);

            var successBefore = NewWorkspaceCompletions("success");
            var ineligibleBefore = NewWorkspaceCompletions("ineligible");

            // A retry against the already-initialized workspace: starts (an attempt) then an ineligible
            // completion, never a second success. Retry-safe: no double success, no duplicate cohort rows.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);
            Assert.Equal(successBefore, NewWorkspaceCompletions("success"));
            Assert.Equal(ineligibleBefore + 1, NewWorkspaceCompletions("ineligible"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingCompletion"));
        }

        [Fact]
        public async Task RejectedInitialization_EmitsNoSuccessCompletion_AndNoCompletionRow()
        {
            // An out-of-scope (Personal) continuation is rejected before the Execute guard runs: it must not
            // emit a success completion nor write a durable COMPLETION row. The durable START from signup
            // persists (the workspace is a real cohort member awaiting conversion).
            const string email = "measure-reject@example.com";
            const string slug = "measure-reject-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rej", "Measure Reject", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            var successBefore = NewWorkspaceCompletions("success");
            var resp = await InitializeCurrent(token, slug, "Personal", "JustMe"); // out of scope
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            Assert.Equal(successBefore, NewWorkspaceCompletions("success"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingCompletion"));
            Assert.False(OperatingState(slug)!.Value.Initialized);
        }

        [Fact]
        public async Task PendingSignupContinuation_FlagIsServerDerived_TrueOnlyForUninitializedSignupOwner()
        {
            // Finding 1: the SPA must route to continuation on a SERVER-DERIVED capability, not the raw
            // collaboration state. Prove the GET current endpoint exposes operatingPolicy.pendingSignupContinuation
            // that is TRUE only for the original owner of an uninitialized signup workspace, and FALSE once
            // the workspace is initialized — so a legacy/initialized workspace is never steered into a flow
            // it cannot complete.
            const string email = "pending-flag@example.com";
            const string slug = "pending-flag-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Pen", "Pending Flag", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            // Before initialization: a genuine pending-signup workspace reports the capability as true.
            Assert.True(await PendingSignupContinuationFlag(token, slug),
                "an uninitialized signup workspace reports pendingSignupContinuation=true for its owner");

            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);

            // After initialization: the workspace is SoloActive and the capability flips to false — the same
            // authoritative predicate the continuation endpoint enforces now rejects a repeat, so the flag
            // can never disagree with it.
            Assert.Equal(WorkspaceCollaborationState.SoloActive, OperatingState(slug)!.Value.State);
            Assert.False(await PendingSignupContinuationFlag(token, slug),
                "an initialized workspace reports pendingSignupContinuation=false");
        }

        [Fact]
        public async Task PendingSignupContinuation_FlagIsFalse_ForPristineNonSignupWorkspace()
        {
            // A pristine, uninitialized workspace created OUTSIDE the signup flow (no signup-continuation
            // marker) also sits at LegacyUnclassified, yet it is NOT a pending signup continuation. The
            // server-derived flag must be false so routing does not steer the owner into a continuation the
            // endpoint would reject. This is the exact legacy-vs-signup distinction finding 1 is about.
            const string email = "legacy-flag@example.com";
            const string slug = "legacy-flag-books";
            var (token, userId) = await CreateVerifiedUser(email);
            using (var scope = _factory.Services.CreateScope())
            {
                var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
                var planKey = scope.ServiceProvider.GetRequiredService<Services.Billing.IPlanCatalog>().DefaultPlanKey;
                // markPendingSignupContinuation defaults false — not the signup flow.
                workspaces.Create(userId, "Legacy Flag Books", slug, planKey, "USD");
            }

            // It is uninitialized (LegacyUnclassified) but carries no signup marker, so the capability is false.
            Assert.False(OperatingState(slug)!.Value.Initialized);
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, OperatingState(slug)!.Value.State);
            Assert.False(await PendingSignupContinuationFlag(token, slug),
                "a pristine non-signup workspace reports pendingSignupContinuation=false");
        }

        [Fact]
        public async Task PendingSignupContinuation_FlagIsFalse_WhenSetupDisabled_AndInitializeStill422()
        {
            // Finding (round 5.1): the capability must also reflect the admission gates. With solo setup
            // DISABLED the continuation is unavailable, so routing must not steer the owner into it even
            // though the workspace is structurally eligible. The flag is false under the setup-off host,
            // and initialization still returns 422 (authorization is retained, not merely hidden).
            const string email = "gate-disabled@example.com";
            const string slug = "gate-disabled-books";
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Gate", "Gate Disabled", slug)).StatusCode);
            var (token, _) = await VerifyRealOtp(email);

            // Sanity: with setup enabled, the capability is available.
            Assert.True(await PendingSignupContinuationFlag(token, slug));

            // Bring up a host with solo setup DISABLED against the same database and signup workspace.
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "false");
            try
            {
                using var offFactory = NewFactory(out var offClient);
                using (offClient)
                {
                    // Capability is false because the admission gate (SoloSetupEnabled) fails...
                    var flagResp = await SendScopedWith(offClient, HttpMethod.Get, "/api/organizations/current", token, slug, null);
                    Assert.Equal(HttpStatusCode.OK, flagResp.StatusCode);
                    using var doc = JsonDocument.Parse(await flagResp.Content.ReadAsStringAsync());
                    Assert.False(doc.RootElement.GetProperty("operatingPolicy").GetProperty("pendingSignupContinuation").GetBoolean());

                    // ...and initialization still fails closed with 422 (authorization retained).
                    var init = await SendScopedWith(offClient, HttpMethod.Post, "/api/organizations/current/initialize", token, slug,
                        new { workspacePurpose = "Business", collaborationIntent = "JustMe" });
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, init.StatusCode);
                    Assert.False(OperatingState(slug)!.Value.Initialized);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");
            }
        }

        private Task<HttpResponseMessage> Signup(string email, string firstName, string? organizationName, string? slug)
            => _client.PostAsync("/api/auth/signup", JsonContent.Create(new
            {
                email,
                firstName,
                organizationName,
                organizationSlug = slug,
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            }));

        private string OtpCodeFor(string email)
        {
            var message = _emails.Messages.Last(m =>
                m.Subject.StartsWith("Welcome to AccountGo!", StringComparison.Ordinal)
                && string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));
            var code = Regex.Match(message.Subject, @"\d{6}").Value;
            Assert.False(string.IsNullOrEmpty(code), "a delivered OTP code was captured");
            // Cross-check the same single-use code was rendered in the delivered body (the body also
            // contains CSS hex colors like #334155, so match the code as delivered rather than any 6 digits).
            Assert.Contains(code, message.Html ?? string.Empty, StringComparison.Ordinal);
            return code;
        }

        private async Task<(string Token, string UserId)> VerifyRealOtp(string email)
        {
            var code = OtpCodeFor(email);
            var resp = await _client.PostAsync("/api/auth/verify-otp", JsonContent.Create(new { email, otp = code }));
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"verify-otp => {(int)resp.StatusCode} :: {body}");
            var json = JsonDocument.Parse(body).RootElement;
            // A real session, not an MFA challenge.
            Assert.False(json.TryGetProperty("status", out var status) &&
                (status.GetString() == "mfa_required" || status.GetString() == "mfa_enrollment_required"),
                "verify-otp returned a full session");
            var token = json.GetProperty("accessToken").GetString()!;
            var userId = await ResolveUserId(email);
            return (token, userId);
        }

        private async Task<string> ResolveUserId(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            return user!.Id;
        }

        private async Task<(string Token, string UserId)> CreateVerifiedUser(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await userManager.CreateAsync(user)).Succeeded, email);
            scope.ServiceProvider.GetRequiredService<Services.Security.ISecurityService>().AddUser(email, email, "T", "U", new List<int>());
            var token = (await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
            return (token, user.Id);
        }

        /// <summary>Full real journey to a SoloActive, financials-entitled workspace owned by a verified user.</summary>
        private async Task<(string Token, string UserId, string Slug)> CreateInitializedSoloWorkspace(string email, string slug)
        {
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Owner", "Org " + slug, slug)).StatusCode);
            var (token, userId) = await VerifyRealOtp(email);
            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);
            return (token, userId, slug);
        }

        private Task<HttpResponseMessage> CreateOrg(string token, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(body);
            return _client.SendAsync(req);
        }

        private Task<HttpResponseMessage> InitializeCurrent(string token, string slug, string purpose, string intent)
            => SendScoped(HttpMethod.Post, "/api/organizations/current/initialize", token, slug,
                new { workspacePurpose = purpose, collaborationIntent = intent });

        /// <summary>Reads operatingPolicy.pendingSignupContinuation from the real GET current endpoint.</summary>
        private async Task<bool> PendingSignupContinuationFlag(string token, string slug)
        {
            var resp = await SendScoped(HttpMethod.Get, "/api/organizations/current", token, slug, null);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, body);
            using var doc = JsonDocument.Parse(body);
            var policy = doc.RootElement.GetProperty("operatingPolicy");
            return policy.ValueKind != JsonValueKind.Null
                && policy.GetProperty("pendingSignupContinuation").GetBoolean();
        }

        private Task<HttpResponseMessage> SendScoped(HttpMethod method, string path, string token, string slug, object? body)
            => SendScopedWith(_client, method, path, token, slug, body);

        private static Task<HttpResponseMessage> SendScopedWith(HttpClient client, HttpMethod method, string path, string token, string slug, object? body)
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-AccountGo-Org", slug);
            if (body != null) req.Content = JsonContent.Create(body);
            return client.SendAsync(req);
        }

        private Task<HttpResponseMessage> SendTenantFree(HttpMethod method, string path, string token, object? body = null)
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body);
            return _client.SendAsync(req);
        }

        private WebApplicationFactory<Program> NewFactory(out HttpClient client)
        {
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
                builder.UseSetting("SECRET_KEY", SecretKey);
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(_emails);
                });
            });
            client = factory.CreateClient();
            return factory;
        }

        private string InvitationToken()
        {
            var html = _emails.Messages.Last(m => m.Subject.Contains(" invited you to ", StringComparison.Ordinal)).Html ?? string.Empty;
            return Regex.Match(html, @"/invite/([A-Za-z0-9_-]{43})").Groups[1].Value;
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
            => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

        // ---- direct DB observers (read-only, IgnoreQueryFilters) --------------------------------------

        private T WithDb<T>(Func<ApiDbContext, T> read)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            return read(db);
        }

        private int CompanyCount(string slug) => WithDb(db => db.Companies.IgnoreQueryFilters().Count(c => c.Slug == slug));

        private int CompanyId(string slug) => WithDb(db => db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id);

        private (WorkspaceCollaborationState State, bool Initialized)? OperatingState(string slug) => WithDb(db =>
        {
            var company = db.Companies.IgnoreQueryFilters().FirstOrDefault(c => c.Slug == slug);
            if (company == null) return ((WorkspaceCollaborationState, bool)?)null;
            var s = db.WorkspaceOperatingSettings.IgnoreQueryFilters().FirstOrDefault(x => x.CompanyId == company.Id);
            return s == null ? null : (s.CollaborationState, s.InitializedAtUtc != null);
        });

        private long PolicyRevision(string slug) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.WorkspaceOperatingSettings.IgnoreQueryFilters().Where(x => x.CompanyId == id).Select(x => x.PolicyRevision).First();
        });

        private int FunctionalGrantCount(string slug) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Count(r => r.CompanyMember.CompanyId == id);
        });

        private int OwnerFunctionalGrantCount(string slug, string ownerUserId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.CompanyMemberSecurityRoles.IgnoreQueryFilters()
                .Count(r => r.CompanyMember.CompanyId == id && r.CompanyMember.UserId == ownerUserId);
        });

        private WorkspaceApprovalRuleKind? JournalRuleKind(string slug) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.WorkspaceApprovalRules.IgnoreQueryFilters()
                .Where(r => r.CompanyId == id && r.OperationKey == Core.Authorization.AccessOperationCatalog.FinancialsJournalPost)
                .Select(r => (WorkspaceApprovalRuleKind?)r.RuleKind)
                .FirstOrDefault();
        });

        private int AccountId(string slug, string code) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.Accounts.IgnoreQueryFilters().First(a => a.CompanyId == id && a.AccountCode == code).Id;
        });

        private int SecurityRoleId(string name) => WithDb(db => db.SecurityRoles.First(r => r.Name == name).Id);

        private int MemberId(string slug, string userId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.CompanyMembers.IgnoreQueryFilters().First(m => m.CompanyId == id && m.UserId == userId).Id;
        });

        private bool IsActiveMember(string slug, string userId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.CompanyMembers.IgnoreQueryFilters().Any(m => m.CompanyId == id && m.UserId == userId && m.Status == CompanyMemberStatus.Active);
        });

        private void DisableModule(string slug, string moduleKey) => WithDb<object?>(db =>
        {
            var company = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug);
            company.DisabledModulesJson = "[\"" + moduleKey + "\"]";
            db.SaveChanges();
            return null;
        });

        private int PostedLedgerHeaderCount(string slug) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.GeneralLedgerHeaders.IgnoreQueryFilters().Count(h => h.CompanyId == id);
        });

        private (decimal Debit, decimal Credit, int LineCount) PostedLedgerTotals(string slug, int journalId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            var header = db.JournalEntryHeaders.IgnoreQueryFilters().FirstOrDefault(h => h.Id == journalId && h.CompanyId == id);
            if (header?.GeneralLedgerHeaderId == null) return (0m, 0m, 0);
            var lines = db.GeneralLedgerLines.IgnoreQueryFilters()
                .Where(l => l.GeneralLedgerHeaderId == header.GeneralLedgerHeaderId).ToList();
            var debit = lines.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount);
            var credit = lines.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
            return (debit, credit, lines.Count);
        });

        private bool JournalPosted(string slug, int journalId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.JournalEntryHeaders.IgnoreQueryFilters().Any(h => h.Id == journalId && h.CompanyId == id && h.Posted == true);
        });

        private string? PostingAuthorizationEvidence(string slug, int journalId) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.CompanyId == id
                    && a.TableName == Services.Financial.FinancialService.JournalPostingAuthorizationAuditTable
                    && a.RecordId == journalId.ToString(CultureInfo.InvariantCulture))
                .OrderByDescending(a => a.Id)
                .Select(a => a.NewValue)
                .FirstOrDefault();
        });

        private int OnboardingCohortCount(string slug, string eventKind) => WithDb(db =>
        {
            var id = db.Companies.IgnoreQueryFilters().First(c => c.Slug == slug).Id;
            return db.AuditLogs.IgnoreQueryFilters()
                .Count(a => a.CompanyId == id
                    && a.TableName == "__WorkspaceOnboardingCohort"
                    && a.NewValue != null
                    && a.NewValue.Contains("\"eventKind\":\"" + eventKind + "\""));
        });

        private string StoredPlanResolvedKey(string slug)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var stored = db.Companies.IgnoreQueryFilters().Where(c => c.Slug == slug).Select(c => c.Plan).Single();
            return scope.ServiceProvider.GetRequiredService<Services.Billing.IPlanCatalog>().ResolveStoredPlan(stored).Key;
        }

        private string? StoredPlan(string slug) => WithDb(db =>
            db.Companies.IgnoreQueryFilters().Where(c => c.Slug == slug).Select(c => c.Plan).Single());

        /// <summary>Deterministic email sink: preserves delivered messages so tests can read the real OTP.</summary>
        private sealed class CapturingEmailSender : IEmailSender
        {
            private readonly List<CapturedMessage> _messages = new();
            public IReadOnlyList<CapturedMessage> Messages
            {
                get { lock (_messages) return _messages.ToList(); }
            }

            public Task<string?> SendAsync(EmailMessage message, CancellationToken ct = default)
            {
                lock (_messages)
                    _messages.Add(new CapturedMessage(message.To, message.Subject, message.Html));
                return Task.FromResult<string?>("captured-" + _messages.Count);
            }
        }

        private sealed record CapturedMessage(string To, string Subject, string? Html);
    }

    [CollectionDefinition("SoloJourneyHttp", DisableParallelization = true)]
    public sealed class SoloJourneyHttpCollection { }
}
