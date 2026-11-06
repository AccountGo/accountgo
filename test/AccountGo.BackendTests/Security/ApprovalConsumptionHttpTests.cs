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
    /// <summary>Approval consume / revoke races for members and invitations over HTTP.</summary>
    public sealed class ApprovalConsumptionHttpTests : IClassFixture<Fixture>
    {
        private const string Current = "/api/organizations/current";
        private readonly Fixture _fixture;

        public ApprovalConsumptionHttpTests(Fixture fixture) => _fixture = fixture;

        [Fact]
        public async Task Approval_RevokeThroughMembersApi_BlocksMemberGrant()
        {
            var setup = await CreateDelegationWorkspace();
            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { targetMemberId = setup.TargetMemberId, targetUserId = setup.Target.Id, securityRoleIds = new[] { setup.AccountantRoleId } },
                token: setup.Owner.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var approvalId = (await Fixture.Json(approval)).GetProperty("approvalRequestId").GetString();

            var revoke = await _fixture.Send(HttpMethod.Delete, Current + "/members/access-approvals/" + approvalId,
                token: setup.Owner.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

            var grant = await _fixture.Send(HttpMethod.Patch, Current + "/members/" + setup.TargetMemberId,
                new { securityRoleIds = new[] { setup.AccountantRoleId }, approvalRequestId = approvalId },
                token: setup.Admin.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
        }

        [Fact]
        public async Task Approval_ResavingDraft_CancelsJournalApproval()
        {
            var team = await CreateJournalTeam();
            var journalId = await SaveDraft(team);
            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { operationKey = AccessOperationCatalog.FinancialsJournalPost, resourceId = journalId.ToString() },
                token: team.Reviewer.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var body = await Fixture.Json(approval);

            int cash, revenue;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                cash = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == team.CompanyId && a.AccountCode == "10111").Id;
                revenue = db.Accounts.IgnoreQueryFilters().Single(a => a.CompanyId == team.CompanyId && a.AccountCode == "40100").Id;
            }
            var resave = await _fixture.Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", new
            {
                id = journalId,
                journalDate = DateTime.UtcNow.Date.AddDays(-1),
                voucherType = 6,
                memo = "Edited after approval",
                journalEntryLines = new[]
                {
                    new { id = 0, accountId = cash, drCr = 1, amount = 30m },
                    new { id = 0, accountId = revenue, drCr = 2, amount = 30m }
                }
            }, token: team.Owner.Token, org: team.Slug);
            Assert.True(resave.IsSuccessStatusCode, await resave.Content.ReadAsStringAsync());

            var posted = await PostJournal(team, journalId,
                body.GetProperty("approvalRequestId").GetString(), body.GetProperty("approverUserId").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, posted.StatusCode);

            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var requestId = body.GetProperty("approvalRequestId").GetString();
                Assert.Contains(db.AuditLogs.IgnoreQueryFilters().Where(a => a.CompanyId == team.CompanyId && a.RecordId == requestId),
                    a => a.TableName == "__AccessDelegationApprovalState" && a.OriginalValue == "Revoked");
            }
        }

        [Fact]
        public async Task Approval_InvitationUsesApprovalOnce()
        {
            var setup = await CreateDelegationWorkspace();
            var inviteeEmail = $"invitee-{setup.Slug}@example.com";
            var invitee = await _fixture.CreateUser(inviteeEmail);

            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new
                {
                    operationKey = AccessOperationCatalog.WorkspaceInvitationsCreate,
                    targetEmail = inviteeEmail,
                    securityRoleIds = new[] { setup.AccountantRoleId }
                },
                token: setup.Owner.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var approvalBody = await Fixture.Json(approval);
            var approvalId = approvalBody.GetProperty("approvalRequestId").GetString();
            var approverId = approvalBody.GetProperty("approverUserId").GetString();

            var invite = await _fixture.Send(HttpMethod.Post, Current + "/invitations",
                new
                {
                    email = inviteeEmail,
                    role = "Member",
                    securityRoleIds = new[] { setup.AccountantRoleId },
                    approvalRequestId = approvalId,
                    approvedByUserId = approverId
                },
                token: setup.Admin.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

            var accept = await _fixture.Send(HttpMethod.Post, "/api/invitations/" + _fixture.LastInvitationToken() + "/accept",
                token: invitee.Token, scoped: false);
            Assert.True(accept.IsSuccessStatusCode, await accept.Content.ReadAsStringAsync());

            var secondEmail = $"second-{setup.Slug}@example.com";
            var secondUser = await _fixture.CreateUser(secondEmail);
            var secondInvite = await _fixture.Send(HttpMethod.Post, Current + "/invitations",
                new
                {
                    email = secondEmail,
                    role = "Member",
                    securityRoleIds = new[] { setup.AccountantRoleId },
                    approvalRequestId = approvalId,
                    approvedByUserId = approverId
                },
                token: setup.Admin.Token, org: setup.Slug);
            // Create may succeed (approval is checked on accept); accept must refuse.
            if (secondInvite.IsSuccessStatusCode)
            {
                var secondAccept = await _fixture.Send(HttpMethod.Post, "/api/invitations/" + _fixture.LastInvitationToken() + "/accept",
                    token: secondUser.Token, scoped: false);
                Assert.Equal(HttpStatusCode.Forbidden, secondAccept.StatusCode);
                Assert.Contains("Approval consumption failed", await secondAccept.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, secondInvite.StatusCode);
            }
        }

        [Fact]
        public async Task Approval_MemberGrantTwice_SecondFails()
        {
            var setup = await CreateDelegationWorkspace();
            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { targetMemberId = setup.TargetMemberId, targetUserId = setup.Target.Id, securityRoleIds = new[] { setup.AccountantRoleId } },
                token: setup.Owner.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var approvalId = (await Fixture.Json(approval)).GetProperty("approvalRequestId").GetString();

            var first = await _fixture.Send(HttpMethod.Patch, Current + "/members/" + setup.TargetMemberId,
                new { securityRoleIds = new[] { setup.AccountantRoleId }, approvalRequestId = approvalId },
                token: setup.Admin.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

            // Strip roles so a second grant would otherwise be needed, then reuse the same approval.
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var roles = db.CompanyMemberSecurityRoles.IgnoreQueryFilters()
                    .Where(r => r.CompanyMemberId == setup.TargetMemberId).ToList();
                db.CompanyMemberSecurityRoles.RemoveRange(roles);
                await db.SaveChangesAsync();
            }

            var second = await _fixture.Send(HttpMethod.Patch, Current + "/members/" + setup.TargetMemberId,
                new { securityRoleIds = new[] { setup.AccountantRoleId }, approvalRequestId = approvalId },
                token: setup.Admin.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
            Assert.DoesNotContain("500", second.StatusCode.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Approval_ConcurrentMemberGrants_ExactlyOneSucceeds()
        {
            var setup = await CreateDelegationWorkspace();
            var approval = await _fixture.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { targetMemberId = setup.TargetMemberId, targetUserId = setup.Target.Id, securityRoleIds = new[] { setup.AccountantRoleId } },
                token: setup.Owner.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            var approvalId = (await Fixture.Json(approval)).GetProperty("approvalRequestId").GetString()!;

            using var barrier = new System.Threading.Barrier(2);
            async Task<bool> Attempt()
            {
                using var scope = _fixture.Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                using (tenant.ImpersonateUser(setup.CompanyId, setup.Admin.Id, setup.Slug, CompanyMemberRole.Admin))
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    return await Api.Authorization.AccessAuthorizationService.ConsumeMatchingApprovalAsync(
                        db, setup.CompanyId, setup.Admin.Id, setup.TargetMemberId, setup.Target.Id, null,
                        new[] { setup.AccountantRoleId }, approvalRequestId: approvalId);
                }
            }

            var results = await Task.WhenAll(Attempt(), Attempt());
            Assert.Single(results, r => r);
            Assert.Single(results, r => !r);

            var patch = await _fixture.Send(HttpMethod.Patch, Current + "/members/" + setup.TargetMemberId,
                new { securityRoleIds = new[] { setup.AccountantRoleId }, approvalRequestId = approvalId },
                token: setup.Admin.Token, org: setup.Slug);
            Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);

            using var verify = _fixture.Factory.Services.CreateScope();
            var verifyDb = verify.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(1, await verifyDb.AuditLogs.IgnoreQueryFilters().CountAsync(a =>
                a.CompanyId == setup.CompanyId && a.TableName == "__AccessDelegationApprovalState" &&
                a.RecordId == approvalId && a.OriginalValue == "Consumed"));
        }

        private sealed record DelegationSetup(
            string Slug, int CompanyId, Fixture.UserSession Owner, Fixture.UserSession Admin,
            Fixture.UserSession Target, int TargetMemberId, int AccountantRoleId);

        private sealed record JournalTeam(string Slug, int CompanyId, Fixture.UserSession Owner, Fixture.UserSession Reviewer);

        private async Task<DelegationSetup> CreateDelegationWorkspace()
        {
            var slug = "apr-" + Guid.NewGuid().ToString("N")[..8];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var target = await _fixture.CreateUser($"target-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Approval Co", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());
            await _fixture.AddMemberDirectAsync(companyId, target.Id, CompanyMemberRole.Member, Array.Empty<string>());

            int targetMemberId, accountantRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                targetMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == target.Id).Id;
                accountantRoleId = db.SecurityRoles.Single(r => r.Name == "Accountant").Id;
            }

            return new DelegationSetup(slug, companyId, owner, admin, target, targetMemberId, accountantRoleId);
        }

        private async Task<JournalTeam> CreateJournalTeam()
        {
            var slug = "apr-je-" + Guid.NewGuid().ToString("N")[..8];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Approval Journal Co", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            var reviewer = await _fixture.CreateUser($"reviewer-{slug}@example.com");
            await _fixture.AddMemberDirectAsync(companyId, reviewer.Id, CompanyMemberRole.Admin, new[] { "Accountant" });

            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                using var impersonation = tenant.Impersonate(companyId, slug, CompanyMemberRole.Owner);
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var ownerMember = db.CompanyMembers.IgnoreQueryFilters()
                    .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
                var settings = db.WorkspaceOperatingSettings.IgnoreQueryFilters()
                    .Single(s => s.CompanyId == companyId);
                settings.Purpose = WorkspacePurpose.Business;
                settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                settings.ConfirmedOwnerMemberId = ownerMember.Id;
                settings.InitializedAtUtc = DateTime.UtcNow;
                settings.InitializedByUserId = owner.Id;
                settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
                settings.LastConfirmedAtUtc = settings.InitializedAtUtc;
                settings.LastConfirmedByUserId = owner.Id;
                settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                db.SaveChanges();
            }

            return new JournalTeam(slug, companyId, owner, reviewer);
        }

        private async Task<int> SaveDraft(JournalTeam team)
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
                memo = "Draft for approval cancel",
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

        private async Task<HttpResponseMessage> PostJournal(JournalTeam team, int journalId, string? approvalId, string? approverId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Financials/PostJournalEntry");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", team.Owner.Token);
            request.Headers.Add("X-AccountGo-Org", team.Slug);
            if (approvalId != null) request.Headers.Add("X-Approval-Request-Id", approvalId);
            if (approverId != null) request.Headers.Add("X-Approved-By-User-Id", approverId);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { id = journalId });
            using var client = _fixture.Factory.CreateClient();
            return await client.SendAsync(request);
        }
    }
}
