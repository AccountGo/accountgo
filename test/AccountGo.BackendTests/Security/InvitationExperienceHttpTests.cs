// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = AccountGo.BackendTests.Security.AccessControlConformanceTests.Fixture;

namespace AccountGo.BackendTests.Security
{
    /// <summary>
    /// Invitation and member experience over authenticated HTTP: invitee access summary, pending / expired /
    /// stale listing, resend with explicit policy reconfirmation, revoke, and actionable reason codes.
    /// </summary>
    public sealed class InvitationExperienceHttpTests : IClassFixture<Fixture>
    {
        private const string Current = "/api/organizations/current";
        private const string InvitationsPath = Current + "/invitations";
        private const string ViewerRoleName = "Auditor / Viewer";
        private const string SingleActorWorkflow = "SingleActorAllowed";
        private const int ExpiredYesterdayOffsetDays = -1;
        private const int ExpiredLongAgoOffsetDays = -45;
        private readonly Fixture _fixture;

        public InvitationExperienceHttpTests(Fixture fixture) => _fixture = fixture;

        private sealed record Workspace(string Slug, int CompanyId, Fixture.UserSession Owner, string InviteeEmail);

        private async Task<Workspace> CreateWorkspace()
        {
            var slug = "inv-" + Guid.NewGuid().ToString("N")[..8];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Invitation Co", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            return new Workspace(slug, companyId, owner, $"invitee-{slug}@example.com");
        }

        private int ViewerRoleId()
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            return db.SecurityRoles.Single(r => r.Name == ViewerRoleName).Id;
        }

        private async Task<int> Invite(Workspace workspace, params int[] securityRoleIds)
        {
            var response = await _fixture.Send(HttpMethod.Post, InvitationsPath,
                new { email = workspace.InviteeEmail, role = "Member", securityRoleIds },
                token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await Fixture.Json(response)).GetProperty("id").GetInt32();
        }

        private void SetExpiry(int invitationId, int offsetDays)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var invitation = db.CompanyInvitations.IgnoreQueryFilters().Single(i => i.Id == invitationId);
            invitation.ExpiresAt = DateTime.UtcNow.AddDays(offsetDays);
            db.SaveChanges();
        }

        private async Task<System.Text.Json.JsonElement> ListInvitation(Workspace workspace, int invitationId)
        {
            var response = await _fixture.Send(HttpMethod.Get, InvitationsPath, token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Fixture.Json(response)).EnumerateArray()
                .SingleOrDefault(i => i.GetProperty("id").GetInt32() == invitationId);
        }

        private async Task<long> PolicyRevision(Workspace workspace)
        {
            var response = await _fixture.Send(HttpMethod.Get, Current + "/operating-settings", token: workspace.Owner.Token, org: workspace.Slug);
            return (await Fixture.Json(response)).GetProperty("policyRevision").GetInt64();
        }

        private async Task ChangeToSingleActor(Workspace workspace)
        {
            var response = await _fixture.Send(HttpMethod.Post, Current + "/operating-settings/journal-approval-policy",
                new { expectedPolicyRevision = await PolicyRevision(workspace), journalWorkflow = SingleActorWorkflow },
                token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task InviteePreview_ShowsFunctionalAccessSummary_WithoutExposingPermissionInternals()
        {
            var workspace = await CreateWorkspace();
            await Invite(workspace, ViewerRoleId());

            var preview = await _fixture.Send(HttpMethod.Get, "/api/invitations/" + _fixture.LastInvitationToken(), scoped: false);

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var access = (await Fixture.Json(preview)).GetProperty("access");
            Assert.Contains(ViewerRoleName, access.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            var areas = access.GetProperty("areas").EnumerateArray().ToList();
            var reports = areas.Single(a => a.GetProperty("area").GetString() == "reports");
            Assert.Equal("View only", reports.GetProperty("level").GetString());
            Assert.DoesNotContain(areas, a => a.GetProperty("level").GetString() == "Manage and post");
        }

        [Fact]
        public async Task InviteePreview_WithNoFunctionalRoles_HasEmptyAccessSummary()
        {
            var workspace = await CreateWorkspace();
            await Invite(workspace);

            var preview = await _fixture.Send(HttpMethod.Get, "/api/invitations/" + _fixture.LastInvitationToken(), scoped: false);

            var access = (await Fixture.Json(preview)).GetProperty("access");
            Assert.Empty(access.GetProperty("roles").EnumerateArray());
            Assert.Empty(access.GetProperty("areas").EnumerateArray());
        }

        [Fact]
        public async Task List_ShowsExpiredInvitations_ForResendOrRevoke_AndHidesVeryOldOnes()
        {
            var workspace = await CreateWorkspace();
            var invitationId = await Invite(workspace);

            var fresh = await ListInvitation(workspace, invitationId);
            Assert.False(fresh.GetProperty("expired").GetBoolean());
            Assert.False(fresh.GetProperty("policyChanged").GetBoolean());

            SetExpiry(invitationId, ExpiredYesterdayOffsetDays);
            Assert.True((await ListInvitation(workspace, invitationId)).GetProperty("expired").GetBoolean());

            SetExpiry(invitationId, ExpiredLongAgoOffsetDays);
            Assert.Equal(System.Text.Json.JsonValueKind.Undefined, (await ListInvitation(workspace, invitationId)).ValueKind);
        }

        [Fact]
        public async Task Resend_RenewsAnExpiredInvitation()
        {
            var workspace = await CreateWorkspace();
            var invitationId = await Invite(workspace);
            SetExpiry(invitationId, ExpiredYesterdayOffsetDays);

            var resend = await _fixture.Send(HttpMethod.Post, $"{InvitationsPath}/{invitationId}/resend", new { },
                token: workspace.Owner.Token, org: workspace.Slug);

            Assert.Equal(HttpStatusCode.NoContent, resend.StatusCode);
            Assert.False((await ListInvitation(workspace, invitationId)).GetProperty("expired").GetBoolean());
        }

        [Fact]
        public async Task Revoke_RemovesAnExpiredInvitation_FromTheList()
        {
            var workspace = await CreateWorkspace();
            var invitationId = await Invite(workspace);
            SetExpiry(invitationId, ExpiredYesterdayOffsetDays);

            var revoke = await _fixture.Send(HttpMethod.Delete, $"{InvitationsPath}/{invitationId}",
                token: workspace.Owner.Token, org: workspace.Slug);

            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            Assert.Equal(System.Text.Json.JsonValueKind.Undefined, (await ListInvitation(workspace, invitationId)).ValueKind);
        }

        [Fact]
        public async Task PolicyChange_MarksInvitationStale_AndResendRequiresExplicitReconfirmation()
        {
            var workspace = await CreateWorkspace();
            // This policy-transition scenario starts from an initialized current-model workspace.
            // Unclassified workspaces cannot acquire single-actor posting through the settings endpoint.
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                using var workspaceScope = tenant.Impersonate(workspace.CompanyId, workspace.Slug, CompanyMemberRole.Owner);
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var owner = db.CompanyMembers.IgnoreQueryFilters()
                    .Single(m => m.CompanyId == workspace.CompanyId && m.UserId == workspace.Owner.Id);
                var settings = db.WorkspaceOperatingSettings.IgnoreQueryFilters()
                    .Single(s => s.CompanyId == workspace.CompanyId);
                settings.Purpose = WorkspacePurpose.Business;
                settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                settings.CollaborationState = WorkspaceCollaborationState.TeamSetupPending;
                settings.ConfirmedOwnerMemberId = owner.Id;
                settings.InitializedAtUtc = DateTime.UtcNow;
                settings.InitializedByUserId = workspace.Owner.Id;
                settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
                db.SaveChanges();
            }
            var invitationId = await Invite(workspace);
            await ChangeToSingleActor(workspace);
            Assert.True((await ListInvitation(workspace, invitationId)).GetProperty("policyChanged").GetBoolean());

            // Without confirmation the resend is refused with an actionable reason code and the current revision.
            var refused = await _fixture.Send(HttpMethod.Post, $"{InvitationsPath}/{invitationId}/resend", new { },
                token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await Fixture.Json(refused);
            Assert.Equal(WorkspacePolicyReasonCodes.PolicyAssumptionsChanged, problem.GetProperty("reasonCode").GetString());
            var currentRevision = problem.GetProperty("policyRevision").GetInt64();
            Assert.Equal(await PolicyRevision(workspace), currentRevision);

            // A stale revision is rejected even with confirmation.
            var stale = await _fixture.Send(HttpMethod.Post, $"{InvitationsPath}/{invitationId}/resend",
                new { confirmChangedPolicy = true, expectedPolicyRevision = currentRevision + 3 },
                token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(WorkspacePolicyReasonCodes.StalePolicyRevision, (await Fixture.Json(stale)).GetProperty("reasonCode").GetString());

            // Explicit reconfirmation with the current revision succeeds and clears the stale marker.
            var confirmed = await _fixture.Send(HttpMethod.Post, $"{InvitationsPath}/{invitationId}/resend",
                new { confirmChangedPolicy = true, expectedPolicyRevision = currentRevision },
                token: workspace.Owner.Token, org: workspace.Slug);
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
            Assert.False((await ListInvitation(workspace, invitationId)).GetProperty("policyChanged").GetBoolean());
        }

        [Fact]
        public async Task Invite_DoesNotChangeOwnerAccessOrApprovalPolicy()
        {
            var workspace = await CreateWorkspace();
            var revisionBefore = await PolicyRevision(workspace);
            var permissionsBefore = await _fixture.GetEffectivePermissionsViaHttpAsync(workspace.Owner.Token, workspace.Slug);

            await Invite(workspace, ViewerRoleId());

            Assert.Equal(revisionBefore, await PolicyRevision(workspace));
            var permissionsAfter = await _fixture.GetEffectivePermissionsViaHttpAsync(workspace.Owner.Token, workspace.Slug);
            Assert.True(permissionsBefore.SetEquals(permissionsAfter));
            var settings = await Fixture.Json(await _fixture.Send(HttpMethod.Get, Current + "/operating-settings",
                token: workspace.Owner.Token, org: workspace.Slug));
            Assert.Equal("IndependentReviewerRequired", settings.GetProperty("journalApproval").GetProperty("mode").GetString());
        }
    }
}
