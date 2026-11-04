// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = AccountGo.BackendTests.Security.AccessControlConformanceTests.Fixture;

namespace AccountGo.BackendTests.Security
{
    /// <summary>
    /// Explicit journal approval-policy management over authenticated HTTP: Owner-authorized change with
    /// revision check, reviewer-eligibility enforcement, last-reviewer protection on member removal and
    /// permission changes, account-deletion reconciliation, and the independent-review posting path.
    /// </summary>
    public sealed class JournalApprovalPolicyHttpTests : IClassFixture<Fixture>
    {
        private const string Current = "/api/organizations/current";
        private const string PolicyPath = Current + "/operating-settings/journal-approval-policy";
        private readonly Fixture _fixture;

        public JournalApprovalPolicyHttpTests(Fixture fixture) => _fixture = fixture;

        private sealed record Team(string Slug, int CompanyId, Fixture.UserSession Owner, Fixture.UserSession Reviewer);

        private async Task<Team> CreateTeam(bool reviewerCanPost = true)
        {
            var slug = "jap-" + Guid.NewGuid().ToString("N")[..8];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Journal Policy Co", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            var reviewer = await _fixture.CreateUser($"reviewer-{slug}@example.com");
            await _fixture.AddMemberDirectAsync(
                companyId, reviewer.Id, CompanyMemberRole.Admin,
                reviewerCanPost ? new[] { "Accountant" } : Array.Empty<string>());
            return new Team(slug, companyId, owner, reviewer);
        }

        private async Task<long> Revision(Team team)
        {
            var response = await _fixture.Send(HttpMethod.Get, Current + "/operating-settings", token: team.Owner.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Fixture.Json(response)).GetProperty("policyRevision").GetInt64();
        }

        private async Task<int> MemberId(Team team, string userId)
        {
            var response = await _fixture.Send(HttpMethod.Get, Current + "/members", token: team.Owner.Token, org: team.Slug);
            var members = await Fixture.Json(response);
            return members.EnumerateArray().Single(m => m.GetProperty("userId").GetString() == userId).GetProperty("memberId").GetInt32();
        }

        private async Task<HttpResponseMessage> SetPolicy(Team team, string workflow, long? revision = null, string? token = null)
        {
            var expected = revision ?? await Revision(team);
            return await _fixture.Send(HttpMethod.Post, PolicyPath,
                new { expectedPolicyRevision = expected, journalWorkflow = workflow },
                token: token ?? team.Owner.Token, org: team.Slug);
        }

        private WorkspaceApprovalRule Rule(int companyId)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            return db.WorkspaceApprovalRules.IgnoreQueryFilters().AsNoTracking()
                .Single(r => r.CompanyId == companyId && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
        }

        private bool MemberExists(int companyId, string userId)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            return db.CompanyMembers.IgnoreQueryFilters().Any(m => m.CompanyId == companyId && m.UserId == userId);
        }

        private static async Task AssertReason(HttpResponseMessage response, HttpStatusCode status, string reasonCode)
        {
            Assert.Equal(status, response.StatusCode);
            var problem = await Fixture.Json(response);
            Assert.Equal(reasonCode, problem.GetProperty("reasonCode").GetString());
        }

        // ---- ChangeJournalApprovalPolicy ----------------------------------------------------------------

        [Fact]
        public async Task Owner_ChangesPolicy_ToIndependent_RecordsExplicitProvenance_AndImmutableAudit()
        {
            var team = await CreateTeam();
            var before = await Revision(team);

            var response = await SetPolicy(team, "IndependentReviewerRequired", before);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Fixture.Json(response);
            Assert.Equal(before + 1, body.GetProperty("policyRevision").GetInt64());
            Assert.Equal("IndependentReviewerRequired", body.GetProperty("journalWorkflow").GetString());

            var rule = Rule(team.CompanyId);
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
            Assert.Equal(WorkspaceApprovalRuleProvenance.ExplicitPolicyChange, rule.Provenance);
            Assert.Equal(team.Owner.Id, rule.ConfiguredByUserId);

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var audit = db.AuditLogs.IgnoreQueryFilters().Single(a => a.CompanyId == team.CompanyId
                && a.TableName == "__WorkspacePolicyTransition" && a.FieldName == "JournalApprovalRule");
            Assert.Equal(team.Owner.Id, audit.UserName);
            Assert.Contains("JournalApprovalPolicyChanged", audit.NewValue);
            Assert.Contains("ExplicitPolicyChange", audit.NewValue);
        }

        [Fact]
        public async Task Owner_CannotRequireIndependentReview_WithoutAnEligibleOtherReviewer()
        {
            var team = await CreateTeam(reviewerCanPost: false);
            Assert.Equal(HttpStatusCode.OK, (await SetPolicy(team, "SingleActorAllowed")).StatusCode);

            var response = await SetPolicy(team, "IndependentReviewerRequired");

            await AssertReason(response, HttpStatusCode.UnprocessableEntity, WorkspacePolicyReasonCodes.ReviewerRequired);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Owner_CanMoveAwayFromIndependentReview_EvenWithoutReviewer()
        {
            var team = await CreateTeam(reviewerCanPost: false);

            var response = await SetPolicy(team, "SingleActorAllowed");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rule = Rule(team.CompanyId);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, rule.RuleKind);
            Assert.Equal(WorkspaceApprovalRuleProvenance.ExplicitPolicyChange, rule.Provenance);
        }

        [Fact]
        public async Task ChangePolicy_RejectsStaleRevision_AndLeavesRuleUnchanged()
        {
            var team = await CreateTeam();
            var stale = await Revision(team) + 5;

            var response = await SetPolicy(team, "SingleActorAllowed", stale);

            await AssertReason(response, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.StalePolicyRevision);
            Assert.Equal(WorkspaceApprovalRuleProvenance.LegacyEnforced, Rule(team.CompanyId).Provenance);
        }

        [Fact]
        public async Task ChangePolicy_IsOwnerOnly()
        {
            var team = await CreateTeam();

            var response = await SetPolicy(team, "SingleActorAllowed", token: team.Reviewer.Token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(WorkspaceApprovalRuleProvenance.LegacyEnforced, Rule(team.CompanyId).Provenance);
        }

        [Fact]
        public async Task OperatingSettings_ShowsEffectivePolicy_ReviewerCapacity_ToOwner()
        {
            var team = await CreateTeam();
            Assert.Equal(HttpStatusCode.OK, (await SetPolicy(team, "IndependentReviewerRequired")).StatusCode);

            var response = await _fixture.Send(HttpMethod.Get, Current + "/operating-settings", token: team.Owner.Token, org: team.Slug);
            var approval = (await Fixture.Json(response)).GetProperty("journalApproval");

            Assert.Equal("IndependentReviewerRequired", approval.GetProperty("mode").GetString());
            Assert.Equal("ExplicitPolicyChange", approval.GetProperty("provenance").GetString());
            Assert.Equal(1, approval.GetProperty("eligibleOtherReviewerCount").GetInt32());
            Assert.True(approval.GetProperty("canChange").GetBoolean());
        }

        // ---- last-reviewer protection ---------------------------------------------------------------------

        [Fact]
        public async Task Remove_LastUsableReviewer_IsRejected_WithReasonCode_AndNoChange()
        {
            var team = await CreateTeam();
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);

            var response = await _fixture.Send(HttpMethod.Delete, $"{Current}/members/{reviewerMemberId}", token: team.Owner.Token, org: team.Slug);

            await AssertReason(response, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.LastReviewerUnavailable);
            Assert.True(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Remove_LastReviewer_WithConfirmedPolicyChange_AppliesExplicitSingleActorRule()
        {
            var team = await CreateTeam();
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);
            var revision = await Revision(team);

            var response = await _fixture.Send(HttpMethod.Delete,
                $"{Current}/members/{reviewerMemberId}?confirmPolicyChangeToSingleActor=true&expectedPolicyRevision={revision}",
                token: team.Owner.Token, org: team.Slug);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(MemberExists(team.CompanyId, team.Reviewer.Id));
            var rule = Rule(team.CompanyId);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, rule.RuleKind);
            Assert.Equal(WorkspaceApprovalRuleProvenance.ExplicitPolicyChange, rule.Provenance);
        }

        [Fact]
        public async Task Remove_LastReviewer_WithStaleConfirmedRevision_IsRejected_AndMemberRemains()
        {
            var team = await CreateTeam();
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);
            var stale = await Revision(team) + 9;

            var response = await _fixture.Send(HttpMethod.Delete,
                $"{Current}/members/{reviewerMemberId}?confirmPolicyChangeToSingleActor=true&expectedPolicyRevision={stale}",
                token: team.Owner.Token, org: team.Slug);

            await AssertReason(response, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.StalePolicyRevision);
            Assert.True(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Remove_Reviewer_IsAllowed_WhenReplacementReviewerRemains()
        {
            var team = await CreateTeam();
            var second = await _fixture.CreateUser($"second-{team.Slug}@example.com");
            await _fixture.AddMemberDirectAsync(team.CompanyId, second.Id, CompanyMemberRole.Admin, new[] { "Accountant" });
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);

            var response = await _fixture.Send(HttpMethod.Delete, $"{Current}/members/{reviewerMemberId}", token: team.Owner.Token, org: team.Slug);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Remove_NonReviewerMember_IsAllowed_WhileReviewCapacityRemains()
        {
            var team = await CreateTeam();
            var bystander = await _fixture.CreateUser($"bystander-{team.Slug}@example.com");
            await _fixture.AddMemberDirectAsync(team.CompanyId, bystander.Id, CompanyMemberRole.Member, Array.Empty<string>());
            var memberId = await MemberId(team, bystander.Id);

            var response = await _fixture.Send(HttpMethod.Delete, $"{Current}/members/{memberId}", token: team.Owner.Token, org: team.Slug);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Remove_NonReviewer_ThatWouldLeaveOnlyOneHuman_IsRejected()
        {
            var team = await CreateTeam();
            // Owner is the only eligible reviewer; the second human is a Member without posting rights.
            await _fixture.AssignFunctionalRolesDirectAsync(team.CompanyId, team.Reviewer.Id, Array.Empty<string>());
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var reviewer = db.CompanyMembers.IgnoreQueryFilters()
                    .Single(m => m.CompanyId == team.CompanyId && m.UserId == team.Reviewer.Id);
                reviewer.Role = CompanyMemberRole.Member;
                db.SaveChanges();
            }

            var memberId = await MemberId(team, team.Reviewer.Id);
            var response = await _fixture.Send(HttpMethod.Delete, $"{Current}/members/{memberId}", token: team.Owner.Token, org: team.Slug);

            await AssertReason(response, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.LastReviewerUnavailable);
            Assert.True(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Update_RemovingLastReviewersPostingRole_IsRejected_ThenAllowedWithConfirmedPolicyChange()
        {
            var team = await CreateTeam();
            await _fixture.AssignFunctionalRolesDirectAsync(team.CompanyId, team.Owner.Id, Array.Empty<string>());
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);

            var rejected = await _fixture.Send(HttpMethod.Patch, $"{Current}/members/{reviewerMemberId}",
                new { securityRoleIds = Array.Empty<int>() }, token: team.Owner.Token, org: team.Slug);
            await AssertReason(rejected, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.LastReviewerUnavailable);

            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.NotEmpty(db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Where(r => r.CompanyMemberId == reviewerMemberId));
            }

            var revision = await Revision(team);
            var accepted = await _fixture.Send(HttpMethod.Patch, $"{Current}/members/{reviewerMemberId}",
                new { securityRoleIds = Array.Empty<int>(), confirmPolicyChangeToSingleActor = true, expectedPolicyRevision = revision },
                token: team.Owner.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task Update_DemotingLastReviewerAdmin_ToMember_IsRejected()
        {
            var team = await CreateTeam();
            await _fixture.AssignFunctionalRolesDirectAsync(team.CompanyId, team.Owner.Id, Array.Empty<string>());
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);

            var response = await _fixture.Send(HttpMethod.Patch, $"{Current}/members/{reviewerMemberId}",
                new { role = "Member" }, token: team.Owner.Token, org: team.Slug);

            await AssertReason(response, HttpStatusCode.Conflict, WorkspacePolicyReasonCodes.LastReviewerUnavailable);
        }

        [Fact]
        public async Task Leave_ConfirmedPolicyChange_RequiresOwner_AdminCannotAuthorizeIt()
        {
            var team = await CreateTeam();
            // The Owner holds no posting role, so the Admin is the only usable reviewer.
            await _fixture.AssignFunctionalRolesDirectAsync(team.CompanyId, team.Owner.Id, Array.Empty<string>());
            var reviewerMemberId = await MemberId(team, team.Reviewer.Id);
            var revision = await Revision(team);

            var response = await _fixture.Send(HttpMethod.Delete,
                $"{Current}/members/{reviewerMemberId}?confirmPolicyChangeToSingleActor=true&expectedPolicyRevision={revision}",
                token: team.Reviewer.Token, org: team.Slug);

            await AssertReason(response, HttpStatusCode.Forbidden, WorkspacePolicyReasonCodes.OwnerAuthorizationRequired);
            Assert.True(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        // ---- account deletion reconciliation ---------------------------------------------------------------

        [Fact]
        public async Task AccountDeletion_NamesTheLastReviewerConflict_AndDoesNotDisableApproval()
        {
            var team = await CreateTeam();

            var blockers = await _fixture.Send(HttpMethod.Get, "/api/auth/account/deletion-blockers", token: team.Reviewer.Token, scoped: false);
            Assert.Equal(HttpStatusCode.OK, blockers.StatusCode);
            var conflict = (await Fixture.Json(blockers)).GetProperty("reviewerConflicts").EnumerateArray().Single();
            Assert.Equal(team.Slug, conflict.GetProperty("slug").GetString());
            Assert.Equal(WorkspacePolicyReasonCodes.LastReviewerUnavailable, conflict.GetProperty("reasonCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(conflict.GetProperty("message").GetString()));

            using var scope = _fixture.Factory.Services.CreateScope();
            var deletion = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();
            var outcome = await deletion.DeleteAsync(team.Reviewer.Id, $"reviewer-{team.Slug}@example.com");
            Assert.Equal(AccountDeletionStatus.Blocked, outcome.Status);
            Assert.True(MemberExists(team.CompanyId, team.Reviewer.Id));
            Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, Rule(team.CompanyId).RuleKind);
        }

        [Fact]
        public async Task AccountDeletion_IsNotBlocked_WhenAReplacementReviewerExists()
        {
            var team = await CreateTeam();
            var second = await _fixture.CreateUser($"second-{team.Slug}@example.com");
            await _fixture.AddMemberDirectAsync(team.CompanyId, second.Id, CompanyMemberRole.Admin, new[] { "Accountant" });

            var blockers = await _fixture.Send(HttpMethod.Get, "/api/auth/account/deletion-blockers", token: team.Reviewer.Token, scoped: false);

            Assert.Empty((await Fixture.Json(blockers)).GetProperty("reviewerConflicts").EnumerateArray());
        }

        // ---- independent-review posting path ---------------------------------------------------------------

        private async Task<int> SaveDraft(Team team)
        {
            int cash, revenue;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                cash = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == team.CompanyId && a.AccountCode == "10111").Id;
                revenue = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == team.CompanyId && a.AccountCode == "40100").Id;
            }

            var draft = new
            {
                id = 0,
                journalDate = DateTime.UtcNow.Date.AddDays(-1),
                voucherType = 6,
                memo = "Reviewed adjustment",
                journalEntryLines = new[]
                {
                    new { id = 0, accountId = cash, drCr = 1, amount = 25m },
                    new { id = 0, accountId = revenue, drCr = 2, amount = 25m }
                }
            };
            var save = await _fixture.Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", draft, token: team.Owner.Token, org: team.Slug);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            return (await Fixture.Json(save)).GetProperty("id").GetInt32();
        }

        private async Task<HttpResponseMessage> PostJournal(Team team, int journalId, string? approvalId = null, string? approverId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Financials/PostJournalEntry");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", team.Owner.Token);
            request.Headers.Add("X-AccountGo-Org", team.Slug);
            if (approvalId != null) request.Headers.Add("X-Approval-Request-Id", approvalId);
            if (approverId != null) request.Headers.Add("X-Approved-By-User-Id", approverId);
            request.Content = JsonContent.Create(new { id = journalId });
            using var client = _fixture.Factory.CreateClient();
            return await client.SendAsync(request);
        }

        [Fact]
        public async Task IndependentReview_ReviewerApproval_MakesPreparersPostingOperable()
        {
            var team = await CreateTeam();
            var journalId = await SaveDraft(team);

            // Without an independent approval the preparer cannot post their own entry.
            Assert.Equal(HttpStatusCode.Forbidden, (await PostJournal(team, journalId)).StatusCode);

            // The preparer cannot record the review approval for their own entry.
            var selfApproval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { operationKey = AccessOperationCatalog.FinancialsJournalPost, resourceId = journalId.ToString() },
                token: team.Owner.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

            // An independent eligible reviewer records the approval; the preparer then posts with the headers.
            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { operationKey = AccessOperationCatalog.FinancialsJournalPost, resourceId = journalId.ToString() },
                token: team.Reviewer.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var body = await Fixture.Json(approval);
            var posted = await PostJournal(team, journalId,
                body.GetProperty("approvalRequestId").GetString(), body.GetProperty("approverUserId").GetString());
            Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task IndependentReview_ApprovalFromIneligibleReviewer_IsRejected()
        {
            var team = await CreateTeam(reviewerCanPost: false);
            var journalId = await SaveDraft(team);

            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { operationKey = AccessOperationCatalog.FinancialsJournalPost, resourceId = journalId.ToString() },
                token: team.Reviewer.Token, org: team.Slug);

            await AssertReason(approval, HttpStatusCode.Forbidden, WorkspacePolicyReasonCodes.ReviewerRequired);
        }

        [Fact]
        public async Task IndependentReview_ApprovalRequiresAnExistingUnpostedDraft()
        {
            var team = await CreateTeam();

            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { operationKey = AccessOperationCatalog.FinancialsJournalPost, resourceId = "999999" },
                token: team.Reviewer.Token, org: team.Slug);

            Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode);
        }
    }
}
