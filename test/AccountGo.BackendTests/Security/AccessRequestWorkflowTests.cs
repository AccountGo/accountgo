using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Security
{
    public sealed class AccessRequestWorkflowTests : IClassFixture<AccessControlConformanceTests.Fixture>
    {
        private readonly AccessControlConformanceTests.Fixture _fixture;

        public AccessRequestWorkflowTests(AccessControlConformanceTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task OwnerSetup_LegacySoleOwner_WithoutOrigin_ReturnsForbidden()
        {
            var slug = "owner-legacy-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            int companyId;
            int roleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var company = new Company
                {
                    Name = "Legacy Org",
                    Slug = slug,
                    CreatedByUserId = owner.Id,
                    AccessBootstrapAvailable = false
                };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
                companyId = company.Id;

                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);

                var settings = new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion,
                    PolicyRevision = 1
                };
                db.WorkspaceOperatingSettings.Add(settings);

                var member = new CompanyMember
                {
                    CompanyId = companyId,
                    UserId = owner.Id,
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active
                };
                db.CompanyMembers.Add(member);
                roleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
                await db.SaveChangesAsync();
            }

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
            Assert.Empty(savedOwner.SecurityRoles);
        }

        [Fact]
        public async Task OwnerSetup_CreatePathSoleOwner_WithOrigin_CanAssignFunctionalRole()
        {
            var slug = "owner-recovery-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Owner recovery", slug);
            int roleId;
            long initialRevision;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var company = db.Companies.Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;
                var settings = db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
                initialRevision = settings.PolicyRevision;
                roleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
                await db.SaveChangesAsync();
            }

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
            Assert.Contains(savedOwner.SecurityRoles, r => r.SecurityRoleId == roleId);
            Assert.DoesNotContain(savedOwner.SecurityRoles, r => r.SecurityRole?.Name == Core.Domain.Security.WorkspaceAccessProfileKeys.FullWorkspaceAccessV1);
            Assert.True(verify.AuditLogs.IgnoreQueryFilters().Any(a => a.CompanyId == companyId && a.TableName == "__OwnerAccessSetup" && a.UserName == owner.Id));
            
            var updatedSettings = verify.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
            Assert.True(updatedSettings.PolicyRevision > initialRevision);

            var accounts = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
        }

        [Fact]
        public async Task OwnerSetup_WithDeletedMemberHistory_ReturnsForbidden()
        {
            var slug = "owner-delhist-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var member = await _fixture.CreateUser($"member-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Deleted member history", slug);
            await _fixture.AddMemberDirectAsync(companyId, member.Id, CompanyMemberRole.Member, Array.Empty<string>());
            int memberId;
            int roleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                roleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
                memberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == member.Id).Id;
            }

            var delResp = await _fixture.Send(HttpMethod.Delete, $"/api/organizations/current/members/{memberId}", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
            Assert.Empty(savedOwner.SecurityRoles);
        }

        [Theory]
        [InlineData("Pending")]
        [InlineData("Accepted")]
        [InlineData("Revoked")]
        public async Task OwnerSetup_WithInvitationHistory_ReturnsForbidden(string state)
        {
            var slug = "owner-invhist-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Invitation history", slug);
            int roleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var company = db.Companies.Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;
                roleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;

                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);

                db.CompanyInvitations.Add(new CompanyInvitation
                {
                    CompanyId = companyId,
                    Email = "invitee@example.com",
                    Role = CompanyMemberRole.Member,
                    InvitedByUserId = owner.Id,
                    TokenHash = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    AcceptedAt = state == "Accepted" ? DateTime.UtcNow : null,
                    RevokedAt = state == "Revoked" ? DateTime.UtcNow : null
                });
                await db.SaveChangesAsync();
            }

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
            Assert.Empty(savedOwner.SecurityRoles);
        }

        [Fact]
        public async Task OwnerSetup_WithTransferredOwnership_ReturnsForbidden()
        {
            var slug = "owner-xfer-" + Guid.NewGuid().ToString("N")[..6];
            var originalCreator = await _fixture.CreateUser($"creator-{slug}@example.com");
            var newOwner = await _fixture.CreateUser($"newowner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(originalCreator.Token, "Transferred ownership", slug);
            int roleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var company = db.Companies.Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;
                roleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;

                var member = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == originalCreator.Id);
                member.UserId = newOwner.Id;
                await db.SaveChangesAsync();
            }

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: newOwner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == newOwner.Id);
            Assert.Empty(savedOwner.SecurityRoles);
        }

        [Fact]
        public async Task OwnerSetup_WithAnotherActiveMember_DoesNotGrantRoles()
        {
            var slug = "owner-team-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var member = await _fixture.CreateUser($"member-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Owner team", slug);
            await _fixture.AddMemberDirectAsync(companyId, member.Id, CompanyMemberRole.Member, Array.Empty<string>());
            int roleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
                roleId = scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityRoles.First(r => r.Name == "Accountant").Id;

            var response = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/owner-access-setup",
                new { securityRoleIds = new[] { roleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var savedOwner = verify.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles)
                .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
            Assert.DoesNotContain(savedOwner.SecurityRoles, r => r.SecurityRoleId == roleId);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AccessSetup_AfterSecondAdministratorDemotedOrRemoved_RemainsUnavailable(bool remove)
        {
            var slug = "bootstrap-history-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Bootstrap history", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());
            int memberId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                memberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == admin.Id).Id;
            }

            var change = await _fixture.Send(remove ? HttpMethod.Delete : HttpMethod.Patch,
                $"/api/organizations/current/members/{memberId}", remove ? null : new { role = "Member" },
                token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

            var status = await _fixture.Send(HttpMethod.Get, "/api/organizations/current/access-setup/status", token: owner.Token, org: slug);
            Assert.False((await AccessControlConformanceTests.Fixture.Json(status)).GetProperty("isEligibleForBootstrap").GetBoolean());
            var setup = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-setup", new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
        }

        [Fact]
        public async Task AccessSetup_LegacyWorkspaceWithoutInitialEligibility_DeniesSelfGrant()
        {
            var slug = "bootstrap-legacy-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            int companyId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var plan = db.Companies.Select(c => c.Plan).First();
                var company = new Company { Name = "Legacy workspace", Slug = slug, Plan = plan, CreatedByUserId = owner.Id };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
                companyId = company.Id;
            }
            await _fixture.AddMemberDirectAsync(companyId, owner.Id, CompanyMemberRole.Owner, Array.Empty<string>());

            var setup = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-setup", new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
        }

        [Fact]
        public async Task AC1_Reproduction_TwoPersonZeroRoleWorkspace_OffersRequestRoute_PendingAccessIneffective()
        {
            var slug = "ac1-req-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC1 Reproduction Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            int accountantRoleId;
            int adminMemberId;
            int ownerMemberId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                accountantRoleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
                adminMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == admin.Id).Id;
                ownerMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == owner.Id).Id;
            }

            // 1. Owner tries to assign Accountant to Admin -> Denied (delegation ceiling)
            var patchOtherRes = await _fixture.Send(HttpMethod.Patch, $"/api/organizations/current/members/{adminMemberId}",
                new { securityRoleIds = new[] { accountantRoleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, patchOtherRes.StatusCode);

            // 2. Owner tries to assign Accountant to self -> Denied (self-grant)
            var patchSelfRes = await _fixture.Send(HttpMethod.Patch, $"/api/organizations/current/members/{ownerMemberId}",
                new { securityRoleIds = new[] { accountantRoleId } }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, patchSelfRes.StatusCode);

            // 3. Owner creates a legitimate access request for self -> 201 Created
            var createReqRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Need to reconcile year-end books" },
                token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Created, createReqRes.StatusCode);

            var reqJson = await AccessControlConformanceTests.Fixture.Json(createReqRes);
            var requestId = reqJson.GetProperty("requestId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(requestId));
            Assert.Equal("Pending", reqJson.GetProperty("status").GetString());

            // 4. While pending, Owner STILL cannot view accounts (zero privilege conferral)
            var accountsRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, accountsRes.StatusCode);
        }

        [Fact]
        public async Task AC2_TwoPersonPositiveCases_OwnerApprovedByAdmin_And_AdminApprovedByOwner()
        {
            var slug = "ac2-twoperson-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC2 Two Person Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            int accountantRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                accountantRoleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
            }

            // Case A: Owner self-requests Accountant -> Admin approves -> Owner gains Accountant
            var ownerReqRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Owner financial access" },
                token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Created, ownerReqRes.StatusCode);
            var ownerReqId = (await AccessControlConformanceTests.Fixture.Json(ownerReqRes)).GetProperty("requestId").GetString()!;

            var adminApproveRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{ownerReqId}/approve",
                new { reason = "Approved by co-administrator" },
                token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, adminApproveRes.StatusCode);

            // Owner can now access Financials!
            var ownerAccounts = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, ownerAccounts.StatusCode);

            // Case B: Admin self-requests Accountant -> Owner approves -> Admin gains Accountant
            var adminReqRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Admin financial access" },
                token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.Created, adminReqRes.StatusCode);
            var adminReqId = (await AccessControlConformanceTests.Fixture.Json(adminReqRes)).GetProperty("requestId").GetString()!;

            var ownerApproveRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{adminReqId}/approve",
                new { reason = "Approved by owner" },
                token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, ownerApproveRes.StatusCode);

            // Admin can now access Financials!
            var adminAccounts = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, adminAccounts.StatusCode);
        }

        [Fact]
        public async Task AC3_IndependenceAndScope_SelfApprovalAndDataIsolationEnforced()
        {
            var slug = "ac3-indep-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC3 Independence Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            int accountantRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                accountantRoleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
            }

            // 1. Owner creates request
            var reqRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Financial work" },
                token: owner.Token, org: slug);
            var reqId = (await AccessControlConformanceTests.Fixture.Json(reqRes)).GetProperty("requestId").GetString()!;

            // 2. Owner attempts self-approval -> 403 Forbidden
            var selfApproveRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{reqId}/approve",
                new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, selfApproveRes.StatusCode);

            // 3. Cross-workspace approval attempt
            var slugB = "ac3-other-" + Guid.NewGuid().ToString("N")[..6];
            var foreignOwner = await _fixture.CreateUser($"foreign-{slugB}@example.com");
            await _fixture.CreateWorkspaceWithZeroFunctionalRoles(foreignOwner.Token, "Foreign Org", slugB);

            var foreignApproveRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{reqId}/approve",
                new { }, token: foreignOwner.Token, org: slugB);
            Assert.Equal(HttpStatusCode.NotFound, foreignApproveRes.StatusCode);

            // 4. Admin approves Owner's request -> Permitted
            var approveRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{reqId}/approve",
                new { }, token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

            // 5. Admin (the approver) STILL has zero functional roles and CANNOT view financial accounts
            var adminAccountsRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, adminAccountsRes.StatusCode);
        }

        [Fact]
        public async Task AC4_Lifecycle_RejectionAndCancellation_WorkCorrectly()
        {
            var slug = "ac4-life-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC4 Lifecycle Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            int accountantRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                accountantRoleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
            }

            // 1. Rejection lifecycle
            var req1Res = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Rejection test" },
                token: owner.Token, org: slug);
            var req1Id = (await AccessControlConformanceTests.Fixture.Json(req1Res)).GetProperty("requestId").GetString()!;

            var rejectRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{req1Id}/reject",
                new { reason = "Not authorized at this time" },
                token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

            var listRes1 = await _fixture.Send(HttpMethod.Get, "/api/organizations/current/access-requests", token: owner.Token, org: slug);
            var listJson1 = await AccessControlConformanceTests.Fixture.Json(listRes1);
            var item1 = listJson1.EnumerateArray().First(r => r.GetProperty("requestId").GetString() == req1Id);
            Assert.Equal("Rejected", item1.GetProperty("status").GetString());
            Assert.Equal("Not authorized at this time", item1.GetProperty("decisionReason").GetString());

            // 2. Cancellation lifecycle
            var req2Res = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Cancellation test" },
                token: owner.Token, org: slug);
            var req2Id = (await AccessControlConformanceTests.Fixture.Json(req2Res)).GetProperty("requestId").GetString()!;

            var cancelRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{req2Id}/cancel",
                new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

            var listRes2 = await _fixture.Send(HttpMethod.Get, "/api/organizations/current/access-requests", token: owner.Token, org: slug);
            var listJson2 = await AccessControlConformanceTests.Fixture.Json(listRes2);
            var item2 = listJson2.EnumerateArray().First(r => r.GetProperty("requestId").GetString() == req2Id);
            Assert.Equal("Cancelled", item2.GetProperty("status").GetString());
        }

        [Fact]
        public async Task AC6_AtomicApplication_MultipleRequestsPreserveUnrelatedRoles()
        {
            var slug = "ac6-atomic-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"admin-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC6 Atomic Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            int accountantRoleId;
            int salesRepRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                accountantRoleId = db.SecurityRoles.First(r => r.Name == "Accountant").Id;
                salesRepRoleId = db.SecurityRoles.First(r => r.Name == "Sales Representative").Id;
            }

            // Create Request A (Accountant) and Request B (Sales Representative) for Owner
            var reqARes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { accountantRoleId }, reason = "Request A" },
                token: owner.Token, org: slug);
            var reqAId = (await AccessControlConformanceTests.Fixture.Json(reqARes)).GetProperty("requestId").GetString()!;

            var reqBRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-requests",
                new { securityRoleIds = new[] { salesRepRoleId }, reason = "Request B" },
                token: owner.Token, org: slug);
            var reqBId = (await AccessControlConformanceTests.Fixture.Json(reqBRes)).GetProperty("requestId").GetString()!;

            // Admin approves Request A
            var approveARes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{reqAId}/approve",
                new { }, token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, approveARes.StatusCode);

            // Request B is still pending
            var listRes = await _fixture.Send(HttpMethod.Get, "/api/organizations/current/access-requests", token: admin.Token, org: slug);
            var listJson = await AccessControlConformanceTests.Fixture.Json(listRes);
            var itemB = listJson.EnumerateArray().First(r => r.GetProperty("requestId").GetString() == reqBId);
            Assert.Equal("Pending", itemB.GetProperty("status").GetString());

            // Admin approves Request B
            var approveBRes = await _fixture.Send(HttpMethod.Post, $"/api/organizations/current/access-requests/{reqBId}/approve",
                new { }, token: admin.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, approveBRes.StatusCode);

            // Owner now has BOTH roles!
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var ownerMember = db.CompanyMembers.Include(m => m.SecurityRoles)
                    .Single(m => m.CompanyId == companyId && m.UserId == owner.Id);
                var assignedRoleIds = ownerMember.SecurityRoles.Select(r => r.SecurityRoleId).ToHashSet();
                Assert.Contains(accountantRoleId, assignedRoleIds);
                Assert.Contains(salesRepRoleId, assignedRoleIds);
            }
        }

        [Fact]
        public async Task AC7_SetupAndRecovery_SingleOwnerBootstrap_BoundedPermanently()
        {
            var slug = "ac7-setup-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "AC7 Single Owner Org", slug);

            // 1. Single owner workspace check status
            var statusRes = await _fixture.Send(HttpMethod.Get, "/api/organizations/current/access-setup/status", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
            var statusJson = await AccessControlConformanceTests.Fixture.Json(statusRes);
            Assert.True(statusJson.GetProperty("isEligibleForBootstrap").GetBoolean());

            // 2. Complete initial access setup
            var setupRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-setup", new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, setupRes.StatusCode);

            // Owner now has Accountant role and can access financials
            var accountsRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, accountsRes.StatusCode);

            // 3. Subsequent bootstrap attempt is permanently rejected (cannot be reopened)
            var retrySetupRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-setup", new { }, token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, retrySetupRes.StatusCode);

            // 4. Two-administrator workspace cannot bypass into bootstrap
            var slug2 = "ac7-twoadmin-" + Guid.NewGuid().ToString("N")[..6];
            var owner2 = await _fixture.CreateUser($"owner2-{slug2}@example.com");
            var admin2 = await _fixture.CreateUser($"admin2-{slug2}@example.com");
            var companyId2 = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner2.Token, "AC7 Two Admin Org", slug2);
            await _fixture.AddMemberDirectAsync(companyId2, admin2.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            var twoAdminSetupRes = await _fixture.Send(HttpMethod.Post, "/api/organizations/current/access-setup", new { }, token: owner2.Token, org: slug2);
            Assert.Equal(HttpStatusCode.Forbidden, twoAdminSetupRes.StatusCode);
        }
    }
}
