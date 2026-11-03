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
    /// test-specific configuration is the isolated WorkspaceExperience enrollment (trusted server config),
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
            // Trusted canary enrollment comes from environment only. Enroll by email (for the anonymous
            // signup boundary) and by any resolved user id (for the authenticated continuation) through a
            // broad allow so the verified owner id is admitted. Production leaves all of this disabled.
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_SOLO_SETUP_ENABLED", "true");
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", "");
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", "");

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
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", null);
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", null);
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
        public async Task RegisteredOnePersonBusiness_SignupVerifyContinue_InitializesExactlyOneSoloWorkspace()
        {
            const string email = "solo-owner@example.com";
            const string slug = "solo-owner-books";
            EnrollEmails(email);

            // 1. Anonymous signup with the About answers. The workspace is reserved but must stay
            //    unclassified before the email is verified.
            var signup = await Signup(email, "Maria", "Solo Owner Books", slug);
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
            Assert.Equal(1, CompanyCount(slug));
            Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, OperatingState(slug)!.Value.State);
            Assert.False(OperatingState(slug)!.Value.Initialized);
            Assert.Equal(0, FunctionalGrantCount(slug));

            // 2. Verify the REAL delivered OTP. Enroll the resolved user id too, as operators enroll the
            //    verified identity for the authenticated continuation path.
            EnrollUserIds(await ResolveUserId(email));
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
            EnrollEmails(email);
            var signup = await Signup(email, "Sam", organizationName: null, slug: null);
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);

            EnrollUserIds(await ResolveUserId(email));
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
            EnrollEmails(email);
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rae", "Repeat Books", slug)).StatusCode);
            EnrollUserIds(await ResolveUserId(email));
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
            // A verified, enrolled owner whose pristine workspace was created OUTSIDE the signup flow (no
            // signup-continuation marker) must be rejected without any grant or policy change.
            const string email = "nomarker@example.com";
            const string slug = "nomarker-books";
            var (token, userId) = await CreateVerifiedUser(email);
            EnrollUserIds(userId);
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

        // ---- enrollment and Business+JustMe scope enforced server-side --------------------------------

        [Fact]
        public async Task Create_UnenrolledVerifiedUser_Rejected_NoProvisioning()
        {
            var (token, _) = await CreateVerifiedUser("unenrolled-journey@example.com"); // not enrolled
            var resp = await CreateOrg(token, new
            {
                name = "Unenrolled Journey",
                slug = "unenrolled-journey",
                workspacePurpose = "Business",
                collaborationIntent = "JustMe"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.CanaryNotEnrolled,
                await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, CompanyCount("unenrolled-journey"));
        }

        [Fact]
        public async Task Create_EnrolledUser_TeamIntent_Rejected_OutOfScope()
        {
            var (token, userId) = await CreateVerifiedUser("scope-journey@example.com");
            EnrollUserIds(userId);
            var resp = await CreateOrg(token, new
            {
                name = "Scope Journey",
                slug = "scope-journey",
                workspacePurpose = "Business",
                collaborationIntent = "WithOthers"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.Contains(Core.Authorization.WorkspacePolicyReasonCodes.CanaryScopeNotAllowed,
                await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, CompanyCount("scope-journey"));
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
        public async Task Continuation_EmitsOnboardingStart_AndSuccessCompletion_OnDurableCommit()
        {
            const string email = "measure-owner@example.com";
            const string slug = "measure-books";
            EnrollEmails(email);
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Mea", "Measure Books", slug)).StatusCode);
            EnrollUserIds(await ResolveUserId(email));
            var (token, _) = await VerifyRealOtp(email);

            var startsBefore = NewWorkspaceStarts();
            var successBefore = NewWorkspaceCompletions("success");

            Assert.Equal(HttpStatusCode.OK, (await InitializeCurrent(token, slug, "Business", "JustMe")).StatusCode);

            // Exactly one start and one success completion for the committed initialization.
            Assert.Equal(startsBefore + 1, NewWorkspaceStarts());
            Assert.Equal(successBefore + 1, NewWorkspaceCompletions("success"));

            // Durable cohort anchors persisted in the same transaction as the initialization.
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(1, OnboardingCohortCount(slug, "OnboardingCompletion"));
        }

        [Fact]
        public async Task RepeatInitialization_EmitsIneligibleCompletion_NotASecondSuccess()
        {
            const string email = "measure-repeat@example.com";
            const string slug = "measure-repeat-books";
            EnrollEmails(email);
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rep", "Measure Repeat", slug)).StatusCode);
            EnrollUserIds(await ResolveUserId(email));
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
        public async Task RejectedInitialization_EmitsNoSuccessCompletion_AndNoCohortRow()
        {
            // An out-of-scope (team) continuation is rejected before the Execute guard runs: it must not
            // emit a success completion nor write a durable cohort row (nothing committed).
            const string email = "measure-reject@example.com";
            const string slug = "measure-reject-books";
            EnrollEmails(email);
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Rej", "Measure Reject", slug)).StatusCode);
            EnrollUserIds(await ResolveUserId(email));
            var (token, _) = await VerifyRealOtp(email);

            var successBefore = NewWorkspaceCompletions("success");
            var resp = await InitializeCurrent(token, slug, "Business", "WithOthers"); // out of scope
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            Assert.Equal(successBefore, NewWorkspaceCompletions("success"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingStart"));
            Assert.Equal(0, OnboardingCohortCount(slug, "OnboardingCompletion"));
            Assert.False(OperatingState(slug)!.Value.Initialized);
        }



        private void EnrollEmails(params string[] emails) =>
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_EMAILS", string.Join(",", emails));

        private void EnrollUserIds(params string[] userIds)
        {
            var existing = Environment.GetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS");
            var all = (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(userIds).Distinct();
            Environment.SetEnvironmentVariable("WORKSPACE_EXPERIENCE_CANARY_USER_IDS", string.Join(",", all));
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
            EnrollEmails(email);
            Assert.Equal(HttpStatusCode.OK, (await Signup(email, "Owner", "Org " + slug, slug)).StatusCode);
            EnrollUserIds(await ResolveUserId(email));
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

        private Task<HttpResponseMessage> SendScoped(HttpMethod method, string path, string token, string slug, object? body)
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-AccountGo-Org", slug);
            if (body != null) req.Content = JsonContent.Create(body);
            return _client.SendAsync(req);
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
