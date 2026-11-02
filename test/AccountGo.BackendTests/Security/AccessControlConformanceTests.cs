#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Authorization;
using Api.Controllers;
using Api.Data;
using Api.Mcp.Tools;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Services.Administration;
using Services.Billing;
using Services.Dashboard;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Security
{
    /// <summary>
    /// End-to-end and multi-adapter conformance test suite for the shared access control contract.
    /// Exercises real relational persistence, real HTTP pipelines, controller aliases,
    /// direct C# controller method calls, and MCP tool adapters against the shared
    /// <see cref="IAccessAuthorizationService"/> contract.
    /// </summary>
    public sealed class AccessControlConformanceTests : IClassFixture<AccessControlConformanceTests.Fixture>
    {
        private readonly Fixture _fixture;

        public AccessControlConformanceTests(Fixture fixture)
        {
            _fixture = fixture;
        }

        // =====================================================================
        // AC1 & AC3: Governance vs. Functional Separation; Zero-Role Owner/Admin/Member Fail Financial Actions;
        // Identical Functional Roles Produce Identical Business Grants Regardless of Workspace Role.
        // =====================================================================

        [Theory]
        [InlineData(CompanyMemberRole.Owner)]
        [InlineData(CompanyMemberRole.Admin)]
        [InlineData(CompanyMemberRole.Member)]
        public async Task AC1_AC3_WorkspaceRoleWithNoFunctionalRoles_FailsFinancialReadsAndMutations_AcrossHttpDirectAndMcp(CompanyMemberRole workspaceRole)
        {
            var slug = "ac3-zero-" + workspaceRole.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Zero Role Org", slug);

            Fixture.UserSession actor = owner;
            if (workspaceRole != CompanyMemberRole.Owner)
            {
                actor = await _fixture.CreateUser($"actor-{slug}@example.com");
                await _fixture.AddMemberDirectAsync(companyId, actor.Id, workspaceRole, Array.Empty<string>());
            }

            int initialAccountCount;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                initialAccountCount = db.Accounts.IgnoreQueryFilters().Count(a => a.CompanyId == companyId);
            }

            // 1. HTTP financial reads and exports must fail with 403 Forbidden and disclose no account data
            var accountsRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, accountsRes.StatusCode);
            var accountsBody = await accountsRes.Content.ReadAsStringAsync();
            Assert.DoesNotContain("10111", accountsBody);
            Assert.DoesNotContain("Checking", accountsBody);

            var snapshotRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportSnapshot", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, snapshotRes.StatusCode);

            var structRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportStructure", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, structRes.StatusCode);

            var cashBanksRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/CashBanks", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, cashBanksRes.StatusCode);

            var dashboardRes = await _fixture.Send(HttpMethod.Get, "/api/Dashboard/Summary", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, dashboardRes.StatusCode);

            // 2. HTTP financial mutation must fail with 403 Forbidden and cause zero side effects
            var addAccountRes = await _fixture.Send(HttpMethod.Post, "/api/Financials/AddAccount", new Dto.Financial.Account
            {
                AccountCode = "19999",
                AccountName = "Unauthorized Cash",
                AccountClassId = 1
            }, token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, addAccountRes.StatusCode);

            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var afterCount = db.Accounts.IgnoreQueryFilters().Count(a => a.CompanyId == companyId);
                Assert.Equal(initialAccountCount, afterCount);
                Assert.False(db.Accounts.IgnoreQueryFilters().Any(a => a.CompanyId == companyId && a.AccountCode == "19999"));
            }

            // 3. Direct C# controller invocation and MCP tool invocation for the same actor/workspace must also deny
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenant.ImpersonateUser(companyId, actor.Id, slug, workspaceRole, permissions: new HashSet<string>()))
                {
                    var authService = scope.ServiceProvider.GetRequiredService<IAccessAuthorizationService>();
                    var decision = authService.Authorize(AccessOperationCatalog.FinancialsAccountsExportSnapshot);
                    Assert.False(decision.Allowed);
                    Assert.Equal(AccessReasonCode.PermissionDenied, decision.ReasonCode);

                    var financialsController = ActivatorUtilities.CreateInstance<FinancialsController>(
                        scope.ServiceProvider,
                        authService);

                    var directExport = financialsController.ExportSnapshot();
                    var objResult = Assert.IsType<ObjectResult>(directExport);
                    Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);

                    var mcpTools = new FinancialMcpTools(
                        scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>(),
                        scope.ServiceProvider.GetRequiredService<IAdministrationService>(),
                        scope.ServiceProvider.GetRequiredService<IEntitlementService>(),
                        new CommunityPlanCatalog(),
                        tenant,
                        authorizationService: authService);

                    var mcpRes = await mcpTools.GetChartOfAccounts();
                    Assert.Equal(Api.Mcp.Models.FinancialMcpStatus.AccessDenied, mcpRes.Status);
                    Assert.Empty(mcpRes.Accounts);
                }
            }

            // 4. Governance endpoints remain allowed for Owner/Admin according to workspace governance policy
            var orgCurrentRes = await _fixture.Send(HttpMethod.Get, "/api/organizations/current", token: actor.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, orgCurrentRes.StatusCode);
            var orgCurrentJson = await Fixture.Json(orgCurrentRes);
            Assert.Equal(0, orgCurrentJson.GetProperty("permissions").GetArrayLength());
        }

        [Fact]
        public async Task AC3_IdenticalFunctionalRoles_ProduceIdenticalBusinessGrants_RegardlessOfWorkspaceRole()
        {
            var slug = "ac3-equal-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"eq-owner-{slug}@example.com");
            var admin = await _fixture.CreateUser($"eq-admin-{slug}@example.com");
            var member = await _fixture.CreateUser($"eq-member-{slug}@example.com");

            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Equal Functional Org", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            await _fixture.AddMemberDirectAsync(companyId, admin.Id, CompanyMemberRole.Admin, new[] { "Accountant" });
            await _fixture.AddMemberDirectAsync(companyId, member.Id, CompanyMemberRole.Member, new[] { "Accountant" });

            var ownerPerms = await _fixture.GetEffectivePermissionsViaHttpAsync(owner.Token, slug);
            var adminPerms = await _fixture.GetEffectivePermissionsViaHttpAsync(admin.Token, slug);
            var memberPerms = await _fixture.GetEffectivePermissionsViaHttpAsync(member.Token, slug);

            Assert.NotEmpty(ownerPerms);
            Assert.Equal(ownerPerms, adminPerms);
            Assert.Equal(ownerPerms, memberPerms);

            var representativeOperations = new[]
            {
                AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView),
                AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsManage),
                AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsJournalPost),
                AccessOperationCatalog.ForPermission(SystemPermissions.ReportsView),
                AccessOperationCatalog.ForPermission(SystemPermissions.AuditLogsView), // Accountant does not hold audit.logs.view
                AccessOperationCatalog.ForPermission(SystemPermissions.SalesQuotationsView) // Accountant does not hold sales.quotations.view
            };

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

            foreach (var opKey in representativeOperations)
            {
                AccessDecision EvalFor(string userId, CompanyMemberRole role)
                {
                    var ctx = new TenantContext();
                    using (ctx.ImpersonateUser(companyId, userId, slug, role))
                    {
                        var svc = new AccessAuthorizationService(ctx, entitlements, db);
                        return svc.Authorize(opKey);
                    }
                }

                var dOwner = EvalFor(owner.Id, CompanyMemberRole.Owner);
                var dAdmin = EvalFor(admin.Id, CompanyMemberRole.Admin);
                var dMember = EvalFor(member.Id, CompanyMemberRole.Member);

                Assert.Equal(dOwner.Allowed, dAdmin.Allowed);
                Assert.Equal(dOwner.Allowed, dMember.Allowed);
                Assert.Equal(dOwner.ReasonCode, dAdmin.ReasonCode);
                Assert.Equal(dOwner.ReasonCode, dMember.ReasonCode);
                Assert.Equal(dOwner.PolicyId, dAdmin.PolicyId);
                Assert.Equal(dOwner.PolicyVersion, dMember.PolicyVersion);
            }
        }

        // =====================================================================
        // AC2: Decision Equivalence Across HTTP, Controller Aliases, Direct Calls, and MCP Tools
        // =====================================================================

        [Fact]
        public async Task AC2_SameActorWorkspaceOperationAndPolicyRevision_YieldEquivalentDecisionsAcrossAdapters()
        {
            var slug = "ac2-equiv-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"ac2-owner-{slug}@example.com");
            var viewer = await _fixture.CreateUser($"ac2-viewer-{slug}@example.com");
            var zeroRoleAdmin = await _fixture.CreateUser($"ac2-admin-{slug}@example.com");

            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Equiv Org", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            await _fixture.AddMemberDirectAsync(companyId, viewer.Id, CompanyMemberRole.Member, new[] { "Auditor / Viewer" });
            await _fixture.AddMemberDirectAsync(companyId, zeroRoleAdmin.Id, CompanyMemberRole.Admin, Array.Empty<string>());

            var actors = new (string Label, Fixture.UserSession Session, CompanyMemberRole Role, bool ExpectViewAllowed, bool ExpectManageAllowed)[]
            {
                ("AccountantOwner", owner, CompanyMemberRole.Owner, true, true),
                ("AuditorViewerMember", viewer, CompanyMemberRole.Member, true, false),
                ("ZeroRoleAdmin", zeroRoleAdmin, CompanyMemberRole.Admin, false, false)
            };

            foreach (var actor in actors)
            {
                // 1. HTTP GET /api/Financials/Accounts vs Direct Authorize vs MCP GetChartOfAccounts
                var httpAccounts = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: actor.Session.Token, org: slug);
                var httpExportSnapshot = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportSnapshot", token: actor.Session.Token, org: slug);
                var httpExportStructure = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportStructure", token: actor.Session.Token, org: slug);
                var httpExportAccountsAlias = await _fixture.Send(HttpMethod.Get, "/api/Financials/ExportAccounts", token: actor.Session.Token, org: slug);

                Assert.Equal(actor.ExpectViewAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, httpAccounts.StatusCode);
                Assert.Equal(actor.ExpectViewAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, httpExportSnapshot.StatusCode);
                Assert.Equal(actor.ExpectManageAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, httpExportStructure.StatusCode);
                Assert.Equal(actor.ExpectManageAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, httpExportAccountsAlias.StatusCode);

                using var scope = _fixture.Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
                var finService = scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();
                var adminService = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenant.ImpersonateUser(companyId, actor.Session.Id, slug, actor.Role))
                {
                    var authService = new AccessAuthorizationService(tenant, entitlements, db);
                    var directViewDecision = authService.Authorize(AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView));
                    var directManageDecision = authService.Authorize(AccessOperationCatalog.FinancialsAccountsExportStructure);

                    Assert.Equal(actor.ExpectViewAllowed, directViewDecision.Allowed);
                    Assert.Equal(actor.ExpectManageAllowed, directManageDecision.Allowed);

                    var controller = ActivatorUtilities.CreateInstance<FinancialsController>(scope.ServiceProvider, authService);
                    var directSnapshotResult = controller.ExportSnapshot();
                    var directStructureResult = controller.ExportStructure();
                    var directAliasResult = controller.ExportAccounts();

                    if (actor.ExpectViewAllowed)
                        Assert.IsType<FileContentResult>(directSnapshotResult);
                    else
                        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(directSnapshotResult).StatusCode);

                    if (actor.ExpectManageAllowed)
                    {
                        Assert.IsType<FileContentResult>(directStructureResult);
                        Assert.IsType<FileContentResult>(directAliasResult);
                    }
                    else
                    {
                        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(directStructureResult).StatusCode);
                        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(directAliasResult).StatusCode);
                    }

                    var mcpTools = new FinancialMcpTools(finService, adminService, entitlements, new CommunityPlanCatalog(), tenant, authorizationService: authService);
                    var coa = await mcpTools.GetChartOfAccounts();
                    if (actor.ExpectViewAllowed)
                    {
                        Assert.Equal(Api.Mcp.Models.FinancialMcpStatus.Success, coa.Status);
                        Assert.True(coa.TotalAccounts > 0);
                    }
                    else
                    {
                        Assert.Equal(Api.Mcp.Models.FinancialMcpStatus.AccessDenied, coa.Status);
                        Assert.Empty(coa.Accounts);
                    }
                }
            }
        }

        // =====================================================================
        // AC4: Positive Least-Privilege Cases and Denials for Unauthenticated, Pending-Auth,
        // Suspended/Removed Membership, Foreign Resources, Missing Grants, Missing Entitlements,
        // Missing Step-Up, Invalid Workflow State, Separation of Duties, and Delegation/Self-Approval
        // =====================================================================

        [Fact]
        public async Task AC4_Unauthenticated_PendingAuth_SuspendedMembership_AndForeignResource_FailClosedWithSafeSemantics()
        {
            var slugA = "ac4-a-" + Guid.NewGuid().ToString("N")[..6];
            var slugB = "ac4-b-" + Guid.NewGuid().ToString("N")[..6];
            var ownerA = await _fixture.CreateUser($"ac4-ownera-{slugA}@example.com");
            var ownerB = await _fixture.CreateUser($"ac4-ownerb-{slugB}@example.com");
            var suspendedUser = await _fixture.CreateUser($"ac4-susp-{slugA}@example.com");

            var companyA = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(ownerA.Token, "AC4 Alpha", slugA);
            var companyB = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(ownerB.Token, "AC4 Beta", slugB);
            await _fixture.AssignFunctionalRolesDirectAsync(companyA, ownerA.Id, new[] { "Accountant" });
            await _fixture.AssignFunctionalRolesDirectAsync(companyB, ownerB.Id, new[] { "Accountant" });
            await _fixture.AddMemberDirectAsync(companyA, suspendedUser.Id, CompanyMemberRole.Member, new[] { "Accountant" }, CompanyMemberStatus.Suspended);

            // 1. Unauthenticated HTTP request -> 401
            var unauthRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: "", org: slugA);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

            // 2. Pre-auth MFA pending token -> 401
            var pendingToken = _fixture.CreateCustomJwt(ownerA.Id, tokenUse: "mfa_pending");
            var pendingRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: pendingToken, org: slugA);
            Assert.Equal(HttpStatusCode.Unauthorized, pendingRes.StatusCode);

            // 3. Suspended member -> 404 NotFound (privacy-preserving workspace isolation)
            var suspendedRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: suspendedUser.Token, org: slugA);
            Assert.Equal(HttpStatusCode.NotFound, suspendedRes.StatusCode);

            // 4. Foreign workspace header -> 404 NotFound
            var foreignWorkspaceRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: ownerA.Token, org: slugB);
            Assert.Equal(HttpStatusCode.NotFound, foreignWorkspaceRes.StatusCode);

            // 5. Foreign resource ID inside authorized workspace -> 404 NotFound / ForeignResource
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var tenant = new TenantContext();
            using (tenant.ImpersonateUser(companyA, ownerA.Id, slugA, CompanyMemberRole.Owner))
            {
                var authService = new AccessAuthorizationService(tenant, entitlements, db);
                var foreignDecision = authService.Authorize(
                    AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView),
                    AccessResourceContext.ForWorkspaceResource(companyA, companyB, "Account", "10111"));

                Assert.False(foreignDecision.Allowed);
                Assert.Equal(AccessReasonCode.ForeignResource, foreignDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status404NotFound, foreignDecision.HttpStatus);
            }
        }

        [Fact]
        public async Task AC4_EntitlementDenial_MfaStepUp_WorkflowState_AndSeparationOfDuties_AreEnforcedByContract()
        {
            var slug = "ac4-dim-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"ac4-dim-{slug}@example.com");
            var approver = await _fixture.CreateUser($"ac4-approver-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Dimensions Org", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });
            await _fixture.AddMemberDirectAsync(companyId, approver.Id, CompanyMemberRole.Admin, new[] { "Accountant" });

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            // 1. Entitlement denial (module disabled on plan)
            var denyingEntitlements = new StubEntitlementService(_ =>
                new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "Financials module is not enabled.", RequiredModule: "financials"));
            using (tenant.ImpersonateUser(companyId, owner.Id, slug, CompanyMemberRole.Owner))
            {
                var authWithDeniedEntitlement = new AccessAuthorizationService(tenant, denyingEntitlements, db);
                var entDecision = authWithDeniedEntitlement.Authorize(
                    AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView));

                Assert.False(entDecision.Allowed);
                Assert.Equal(AccessReasonCode.EntitlementDenied, entDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, entDecision.HttpStatus);
            }

            // 2. MFA Step-Up requirement (WorkspaceOrganizationDelete requires RequiresMfaStepUp = true)
            var allowEntitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            using (tenant.ImpersonateUser(companyId, owner.Id, slug, CompanyMemberRole.Owner))
            {
                var authNoMfa = new AccessAuthorizationService(tenant, allowEntitlements, db);
                var stepUpDenied = authNoMfa.Authorize(AccessOperationCatalog.WorkspaceOrganizationDelete);
                Assert.False(stepUpDenied.Allowed);
                Assert.Equal(AccessReasonCode.StepUpRequired, stepUpDenied.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, stepUpDenied.HttpStatus);

                // With recent mfa_verified_at claim on HttpContext, WorkspaceOrganizationDelete is allowed
                var httpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", owner.Id),
                        new Claim("mfa_verified_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                    }, "Bearer"))
                };
                var accessor = new HttpContextAccessor { HttpContext = httpContext };
                var authWithMfa = new AccessAuthorizationService(tenant, allowEntitlements, db, accessor);
                var stepUpAllowed = authWithMfa.Authorize(AccessOperationCatalog.WorkspaceOrganizationDelete);
                Assert.True(stepUpAllowed.Allowed);
                Assert.Equal(AccessReasonCode.Allowed, stepUpAllowed.ReasonCode);
            }

            // 3. Mutable workflow state & separation of duties on FinancialsJournalPost
            using (tenant.ImpersonateUser(companyId, owner.Id, slug, CompanyMemberRole.Owner))
            {
                var authService = new AccessAuthorizationService(tenant, allowEntitlements, db);

                // 3a. Period locked -> 403 WorkflowStateInvalid
                var lockedDecision = authService.Authorize(
                    AccessOperationCatalog.FinancialsJournalPost,
                    new AccessResourceContext
                    {
                        WorkspaceCompanyId = companyId,
                        ResourceCompanyId = companyId,
                        IsPeriodLockedOrClosed = true
                    });
                Assert.False(lockedDecision.Allowed);
                Assert.Equal(AccessReasonCode.WorkflowStateInvalid, lockedDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, lockedDecision.HttpStatus);

                // 3b. Already Posted state -> 400 BadRequest WorkflowStateInvalid
                var alreadyPostedDecision = authService.Authorize(
                    AccessOperationCatalog.FinancialsJournalPost,
                    new AccessResourceContext
                    {
                        WorkspaceCompanyId = companyId,
                        ResourceCompanyId = companyId,
                        WorkflowState = "Posted"
                    });
                Assert.False(alreadyPostedDecision.Allowed);
                Assert.Equal(AccessReasonCode.WorkflowStateInvalid, alreadyPostedDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status400BadRequest, alreadyPostedDecision.HttpStatus);

                // 3c. Separation of duties: PreparedByUserId == actor.UserId without independent approver -> 403 SeparationOfDutiesViolation
                var sodDenied = authService.Authorize(
                    AccessOperationCatalog.FinancialsJournalPost,
                    new AccessResourceContext
                    {
                        WorkspaceCompanyId = companyId,
                        ResourceCompanyId = companyId,
                        WorkflowState = "Draft",
                        PreparedByUserId = owner.Id
                    });
                Assert.False(sodDenied.Allowed);
                Assert.Equal(AccessReasonCode.SeparationOfDutiesViolation, sodDenied.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, sodDenied.HttpStatus);

                // 3d. Separation of duties satisfied when an independent approval is recorded
                var (approvalReqId, _) = await AccessAuthorizationService.RecordAuthenticatedApprovalAsync(
                    db,
                    companyId,
                    approver.Id,
                    AccessOperationCatalog.FinancialsJournalPost,
                    resourceId: "123");

                var sodAllowed = authService.Authorize(
                    AccessOperationCatalog.FinancialsJournalPost,
                    new AccessResourceContext
                    {
                        WorkspaceCompanyId = companyId,
                        ResourceCompanyId = companyId,
                        ResourceId = "123",
                        WorkflowState = "Draft",
                        PreparedByUserId = owner.Id,
                        IndependentApproverUserId = approver.Id,
                        ApprovalRequestId = approvalReqId
                    });
                Assert.True(sodAllowed.Allowed);
            }
        }

        [Fact]
        public async Task AC4_F1_F5_DelegationAndSelfApprovalControls_PreventSelfGrantAndUnilateralAdminEscalation()
        {
            var slug = "ac4-f5-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"f5-owner-{slug}@example.com");
            var govOnlyAdmin = await _fixture.CreateUser($"f5-admin-{slug}@example.com");
            var targetMember = await _fixture.CreateUser($"f5-member-{slug}@example.com");
            var invitee = await _fixture.CreateUser($"f5-invitee-{slug}@example.com");

            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "F5 Delegation Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, govOnlyAdmin.Id, CompanyMemberRole.Admin, Array.Empty<string>());
            await _fixture.AddMemberDirectAsync(companyId, targetMember.Id, CompanyMemberRole.Member, Array.Empty<string>());

            int ownerMemberId;
            int adminMemberId;
            int targetMemberId;
            int accountantRoleId;
            int viewerRoleId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                ownerMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == owner.Id).Id;
                adminMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == govOnlyAdmin.Id).Id;
                targetMemberId = db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == targetMember.Id).Id;
                accountantRoleId = db.SecurityRoles.Single(r => r.Name == "Accountant").Id;
                viewerRoleId = db.SecurityRoles.Single(r => r.Name == "Auditor / Viewer").Id;
            }

            // 1. Zero-role Owner cannot self-grant Accountant via PATCH /api/organizations/current/members/{ownerMemberId} without an independent approver
            var ownerSelfGrantRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{ownerMemberId}",
                new { securityRoleIds = new[] { accountantRoleId } },
                token: owner.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, ownerSelfGrantRes.StatusCode);

            // 2. Zero-role Owner cannot name themselves as their own independent approver
            var ownerSelfApproveRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{ownerMemberId}",
                new { securityRoleIds = new[] { accountantRoleId }, approvedByUserId = owner.Id },
                token: owner.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, ownerSelfApproveRes.StatusCode);

            // 3. Zero-role Owner cannot self-invite their own email to acquire functional roles
            var selfInviteRes = await _fixture.Send(
                HttpMethod.Post,
                "/api/organizations/current/invitations",
                new { email = $"f5-owner-{slug}@example.com", role = "Member", securityRoleIds = new[] { accountantRoleId } },
                token: owner.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, selfInviteRes.StatusCode);

            // 4. Governance-only Admin cannot unilaterally grant Accountant (sensitive financial mutation role) to a Member without independent approval
            var adminUnilateralGrantRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{targetMemberId}",
                new { securityRoleIds = new[] { accountantRoleId } },
                token: govOnlyAdmin.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, adminUnilateralGrantRes.StatusCode);

            // 5. Governance-only Admin CAN grant read-only Auditor / Viewer role to a Member, OR grant Accountant when approved by the independent Owner
            var adminViewerGrantRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{targetMemberId}",
                new { securityRoleIds = new[] { viewerRoleId } },
                token: govOnlyAdmin.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.NoContent, adminViewerGrantRes.StatusCode);

            // 5a. Governance-only Admin cannot forge owner approval with a raw string (R1)
            var adminForgedGrantRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{targetMemberId}",
                new { securityRoleIds = new[] { accountantRoleId }, approvedByUserId = owner.Id },
                token: govOnlyAdmin.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, adminForgedGrantRes.StatusCode);

            // 5b. Independent Owner records an authenticated dual-control approval record
            var approvalRes = await _fixture.Send(
                HttpMethod.Post,
                "/api/organizations/current/members/access-approvals",
                new
                {
                    targetMemberId = targetMemberId,
                    targetUserId = targetMember.Id,
                    securityRoleIds = new[] { accountantRoleId }
                },
                token: owner.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Created, approvalRes.StatusCode);
            var approvalJson = await Fixture.Json(approvalRes);
            var approvalRequestId = approvalJson.GetProperty("approvalRequestId").GetString();

            // 5c. Governance-only Admin submits with valid approvalRequestId and succeeds
            var adminApprovedGrantRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{targetMemberId}",
                new { securityRoleIds = new[] { accountantRoleId }, approvalRequestId = approvalRequestId },
                token: govOnlyAdmin.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.NoContent, adminApprovedGrantRes.StatusCode);

            // 6. If an Admin creates an invitation and is subsequently demoted to Member before acceptance, invitation acceptance fails closed
            var createInviteRes = await _fixture.Send(
                HttpMethod.Post,
                "/api/organizations/current/invitations",
                new { email = $"f5-invitee-{slug}@example.com", role = "Member", securityRoleIds = new[] { viewerRoleId } },
                token: govOnlyAdmin.Token,
                org: slug);
            Assert.Equal(HttpStatusCode.Created, createInviteRes.StatusCode);

            string rawInviteToken;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                // Demote govOnlyAdmin to Member before the invitee accepts
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var adminRow = db.CompanyMembers.Single(m => m.Id == adminMemberId);
                adminRow.Role = CompanyMemberRole.Member;
                await db.SaveChangesAsync();
                rawInviteToken = _fixture.LastInvitationToken();
            }

            var acceptAfterDemotionRes = await _fixture.Send(
                HttpMethod.Post,
                $"/api/invitations/{rawInviteToken}/accept",
                token: invitee.Token,
                scoped: false);
            Assert.Equal(HttpStatusCode.Forbidden, acceptAfterDemotionRes.StatusCode);
        }

        // =====================================================================
        // AC5: Aliases, Direct Calls, Scoped Queries/Aggregates (Dashboard), Exports,
        // Bulk Actions (Import Replace vs Append), Mid-Session Revocation, and Workspace Switching
        // =====================================================================

        [Fact]
        public async Task AC5_DashboardScoping_BulkReplaceCompositeCheck_MidSessionRevocation_AndWorkspaceSwitching()
        {
            var slugA = "ac5-a-" + Guid.NewGuid().ToString("N")[..6];
            var slugB = "ac5-b-" + Guid.NewGuid().ToString("N")[..6];
            var actor = await _fixture.CreateUser($"ac5-actor-{slugA}@example.com");
            var ownerB = await _fixture.CreateUser($"ac5-ownerb-{slugB}@example.com");

            // Actor is a zero-role Owner in Workspace A, and an Accountant Member in Workspace B
            var companyA = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(actor.Token, "Workspace A", slugA);
            var companyB = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(ownerB.Token, "Workspace B", slugB);
            await _fixture.AddMemberDirectAsync(companyB, actor.Id, CompanyMemberRole.Member, new[] { "Accountant" });

            // 1. Workspace switching: same JWT denied in Workspace A (zero-role Owner) and allowed in Workspace B (Accountant Member)
            var accountsInA = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: actor.Token, org: slugA);
            var accountsInB = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: actor.Token, org: slugB);
            Assert.Equal(HttpStatusCode.Forbidden, accountsInA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, accountsInB.StatusCode);

            // 2. Dashboard scoping: Sales Representative only sees Sales & Receivables domain (no Cash/Profitability/Purchases/Inventory)
            var salesOnlyUser = await _fixture.CreateUser($"ac5-sales-{slugB}@example.com");
            await _fixture.AddMemberDirectAsync(companyB, salesOnlyUser.Id, CompanyMemberRole.Member, new[] { "Sales Representative" });

            var dashSalesRes = await _fixture.Send(HttpMethod.Get, "/api/Dashboard/Summary", token: salesOnlyUser.Token, org: slugB);
            Assert.Equal(HttpStatusCode.OK, dashSalesRes.StatusCode);
            var dashSalesJson = await Fixture.Json(dashSalesRes);
            Assert.NotEqual(JsonValueKind.Null, dashSalesJson.GetProperty("kpis").GetProperty("outstandingReceivables").ValueKind);
            Assert.Equal(0m, dashSalesJson.GetProperty("kpis").GetProperty("moneyInBank").GetDecimal());
            Assert.Equal(JsonValueKind.Null, dashSalesJson.GetProperty("kpis").GetProperty("bankBalances").ValueKind);
            Assert.Equal(0m, dashSalesJson.GetProperty("kpis").GetProperty("totalPayables").GetDecimal());
            Assert.Empty(dashSalesJson.GetProperty("upcomingPayables").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, dashSalesJson.GetProperty("bankCloseStatus").ValueKind);

            // 3. Bulk COA import replace composite check: an Accountant Member in Workspace B can append, but CANNOT replace (requires Owner/Admin governance + financials.accounts.manage)
            var csv = "# AccountGo-COA-v1\r\n" +
                      "AccountCode,AccountName,ParentAccountCode,AccountClass\r\n" +
                      "10000,New Assets Root,,Assets\r\n" +
                      "20000,New Liabilities Root,,Liabilities\r\n" +
                      "30000,New Equity Root,,Equity\r\n" +
                      "40000,New Revenue Root,,Revenue\r\n" +
                      "50000,New Expense Root,,Expenses\r\n";

            var memberReplaceRes = await _fixture.Send(
                HttpMethod.Post,
                "/api/Financials/ImportAccounts/Commit?mode=replace",
                new { csvContent = csv, mode = "replace" },
                token: actor.Token,
                org: slugB);
            Assert.Equal(HttpStatusCode.Forbidden, memberReplaceRes.StatusCode);

            // 4. Mid-session revocation: revoking actor's Accountant role in Workspace B immediately denies the next request with the same JWT
            int actorMemberBId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                actorMemberBId = db.CompanyMembers.Single(m => m.CompanyId == companyB && m.UserId == actor.Id).Id;
            }

            var revokeRes = await _fixture.Send(
                HttpMethod.Patch,
                $"/api/organizations/current/members/{actorMemberBId}",
                new { securityRoleIds = Array.Empty<int>() },
                token: ownerB.Token,
                org: slugB);
            Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

            var accountsInBAfterRevoke = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: actor.Token, org: slugB);
            Assert.Equal(HttpStatusCode.Forbidden, accountsInBAfterRevoke.StatusCode);
        }

        // =====================================================================
        // AC6: Unknown Policy Keys, Missing Context, and Dependency Failures Fail Closed
        // =====================================================================

        [Fact]
        public async Task AC6_UnknownPolicyKeys_MissingContext_AndPolicyDependencyFailures_FailClosed()
        {
            var slug = "ac6-failclosed-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"ac6-owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Fail Closed Org", slug);
            await _fixture.AssignFunctionalRolesDirectAsync(companyId, owner.Id, new[] { "Accountant" });

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var tenant = new TenantContext();

            using (tenant.ImpersonateUser(companyId, owner.Id, slug, CompanyMemberRole.Owner))
            {
                var authService = new AccessAuthorizationService(tenant, entitlements, db);

                // 1. Unknown operation key -> 403 UnknownOperation
                var unknownDecision = authService.Authorize("financials.nonexistent.operation.v99");
                Assert.False(unknownDecision.Allowed);
                Assert.Equal(AccessReasonCode.UnknownOperation, unknownDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, unknownDecision.HttpStatus);

                var emptyDecision = authService.Authorize("");
                Assert.False(emptyDecision.Allowed);
                Assert.Equal(AccessReasonCode.UnknownOperation, emptyDecision.ReasonCode);

                // 2. Missing required delegation resource context -> 403 MissingContext
                var missingDelegationContext = authService.Authorize(AccessOperationCatalog.WorkspaceMembersAssignFunctionalRoles, resource: null);
                Assert.False(missingDelegationContext.Allowed);
                Assert.Equal(AccessReasonCode.MissingContext, missingDelegationContext.ReasonCode);
                Assert.Equal(StatusCodes.Status403Forbidden, missingDelegationContext.HttpStatus);

                // 3. Simulated policy dependency failure (IEntitlementService throws exception) -> 503 PolicyDependencyFailure (fail closed)
                var throwingEntitlements = new StubEntitlementService(_ => throw new InvalidOperationException("Simulated entitlement store outage"));
                var faultedAuthService = new AccessAuthorizationService(tenant, throwingEntitlements, db);
                var dependencyFailureDecision = faultedAuthService.Authorize(
                    AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView));

                Assert.False(dependencyFailureDecision.Allowed);
                Assert.Equal(AccessReasonCode.PolicyDependencyFailure, dependencyFailureDecision.ReasonCode);
                Assert.Equal(StatusCodes.Status503ServiceUnavailable, dependencyFailureDecision.HttpStatus);
                Assert.DoesNotContain("Simulated entitlement store outage", dependencyFailureDecision.SafeMessage);
            }
        }

        // =====================================================================
        // AC8: Platform/Staff and Service Identities Cannot Acquire Workspace Financial Access;
        // Explicit Scoped Service Policies Remain Testable
        // =====================================================================

        [Fact]
        public async Task AC8_PlatformAdmin_StaffOperator_TransportGate_AndServicePrincipals_CannotAcquireWorkspaceFinancialAccess_WhileScopedServicePolicyWorks()
        {
            var slug = "ac8-domains-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"ac8-owner-{slug}@example.com");
            var sysAdmin = await _fixture.CreateUser($"ac8-sysadmin-{slug}@example.com", isSystemAdministrator: true);
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Trust Domains Org", slug);

            // 1. Platform Administrator (SystemAdministrators) without active workspace membership is denied on workspace financial endpoints
            var sysAdminFinancialRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: sysAdmin.Token, org: slug);
            Assert.Equal(HttpStatusCode.NotFound, sysAdminFinancialRes.StatusCode);

            // Even if Platform Administrator is a workspace Owner with zero functional roles, they still fail financial endpoints
            await _fixture.AddMemberDirectAsync(companyId, sysAdmin.Id, CompanyMemberRole.Owner, Array.Empty<string>());
            var sysAdminMemberFinancialRes = await _fixture.Send(HttpMethod.Get, "/api/Financials/Accounts", token: sysAdmin.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, sysAdminMemberFinancialRes.StatusCode);

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var tenant = new TenantContext();
            var authService = new AccessAuthorizationService(tenant, entitlements, db);

            var finViewOp = AccessOperationCatalog.ForPermission(SystemPermissions.FinancialsAccountsView);

            // 2. StaffOperator identity cannot access workspace financial operations
            var staffActor = new AccessActorContext
            {
                TrustDomain = ActorTrustDomain.StaffOperator,
                UserId = "staff-operator-1",
                CompanyId = companyId,
                WorkspaceRole = CompanyMemberRole.Owner,
                MembershipStatus = CompanyMemberStatus.Active
            };
            var staffFinDecision = authService.Authorize(finViewOp, actorOverride: staffActor);
            Assert.False(staffFinDecision.Allowed);
            Assert.Equal(AccessReasonCode.ActorDomainMismatch, staffFinDecision.ReasonCode);

            // 3. TransportGate identity (MCP gate without user context) cannot access workspace financial operations
            var transportActor = new AccessActorContext
            {
                TrustDomain = ActorTrustDomain.TransportGate,
                CompanyId = companyId
            };
            var transportFinDecision = authService.Authorize(finViewOp, actorOverride: transportActor);
            Assert.False(transportFinDecision.Allowed);
            Assert.Equal(AccessReasonCode.ActorDomainMismatch, transportFinDecision.ReasonCode);

            // 4. ServicePrincipal identity cannot access workspace financial operations
            var serviceActor = AccessActorContext.ForServicePrincipal(
                AccessOperationCatalog.EmailReconciliationServicePrincipalId,
                companyId);
            var serviceFinDecision = authService.Authorize(finViewOp, actorOverride: serviceActor);
            Assert.False(serviceFinDecision.Allowed);
            Assert.Equal(AccessReasonCode.ActorDomainMismatch, serviceFinDecision.ReasonCode);

            // 5. Explicit scoped ServicePrincipal policy (ServiceEmailDomainReconcile) succeeds for the registered service principal
            // when the workspace is entitled to custom_email, and fails when unentitled, for an unregistered service principal, or for a regular workspace user
            var unentitledServiceDecision = authService.Authorize(
                AccessOperationCatalog.ServiceEmailDomainReconcile,
                new AccessResourceContext { WorkspaceCompanyId = companyId, ResourceCompanyId = companyId },
                actorOverride: serviceActor);
            Assert.False(unentitledServiceDecision.Allowed);
            Assert.Equal(AccessReasonCode.EntitlementDenied, unentitledServiceDecision.ReasonCode);

            var entitledAuthService = new AccessAuthorizationService(
                tenant,
                new StubEntitlementService(_ => new EntitlementDecision(true, null, null)),
                db);
            var allowedServiceDecision = entitledAuthService.Authorize(
                AccessOperationCatalog.ServiceEmailDomainReconcile,
                new AccessResourceContext { WorkspaceCompanyId = companyId, ResourceCompanyId = companyId },
                actorOverride: serviceActor);
            Assert.True(allowedServiceDecision.Allowed);
            Assert.Equal(AccessReasonCode.Allowed, allowedServiceDecision.ReasonCode);

            var wrongPrincipalActor = AccessActorContext.ForServicePrincipal("service:unregistered-worker", companyId);
            var wrongPrincipalDecision = entitledAuthService.Authorize(
                AccessOperationCatalog.ServiceEmailDomainReconcile,
                new AccessResourceContext { WorkspaceCompanyId = companyId, ResourceCompanyId = companyId },
                actorOverride: wrongPrincipalActor);
            Assert.False(wrongPrincipalDecision.Allowed);
            Assert.Equal(AccessReasonCode.ActorDomainMismatch, wrongPrincipalDecision.ReasonCode);

            var workspaceUserOnServiceOp = entitledAuthService.Authorize(
                AccessOperationCatalog.ServiceEmailDomainReconcile,
                actorOverride: new AccessActorContext
                {
                    TrustDomain = ActorTrustDomain.WorkspaceUser,
                    UserId = owner.Id,
                    CompanyId = companyId,
                    WorkspaceRole = CompanyMemberRole.Owner,
                    MembershipStatus = CompanyMemberStatus.Active
                });
            Assert.False(workspaceUserOnServiceOp.Allowed);
            Assert.Equal(AccessReasonCode.ActorDomainMismatch, workspaceUserOnServiceOp.ReasonCode);
        }

        // =====================================================================
        // Coordinated Route Guards (F2, F3, F4): Audit Logs, Donations, Tax, Sales Void/Post
        // =====================================================================

        [Fact]
        public async Task CoordinatedRouteGuards_EnforceLeastPrivilegeAcrossAuditDonationsTaxAndSales()
        {
            var slug = "coord-guards-" + Guid.NewGuid().ToString("N")[..6];
            var owner = await _fixture.CreateUser($"coord-owner-{slug}@example.com");
            var viewer = await _fixture.CreateUser($"coord-viewer-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Coordinated Guards Org", slug);
            await _fixture.AddMemberDirectAsync(companyId, viewer.Id, CompanyMemberRole.Member, new[] { "Auditor / Viewer" });

            // Zero-role Owner is denied on AuditLogs, Donors, Donations, and Tax mutations
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Get, "/api/Administration/AuditLogs", token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Get, "/api/Audit/Entities", token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Get, "/api/Donors", token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Get, "/api/Donations/DonationInvoices", token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Post, "/api/Tax", new { taxName = "VAT", taxCode = "VAT", rate = 12 }, token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Delete, "/api/Tax/deletetaxgroup?id=1", token: owner.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fixture.Send(HttpMethod.Delete, "/api/Tax/deleteitemtaxgroup?id=1", token: owner.Token, org: slug)).StatusCode);

            // Auditor / Viewer has audit.logs.view, so GET /api/Administration/AuditLogs and GET /api/Audit/Entities succeed
            Assert.Equal(HttpStatusCode.OK, (await _fixture.Send(HttpMethod.Get, "/api/Administration/AuditLogs", token: viewer.Token, org: slug)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _fixture.Send(HttpMethod.Get, "/api/Audit/Entities", token: viewer.Token, org: slug)).StatusCode);
        }

        // =====================================================================
        // Test Fixture & Helpers
        // =====================================================================

        private sealed class StubEntitlementService : IEntitlementService
        {
            private readonly Func<EntitlementAction, EntitlementDecision> _evaluator;

            public StubEntitlementService(Func<EntitlementAction, EntitlementDecision> evaluator)
            {
                _evaluator = evaluator;
            }

            public EntitlementsOptions Options => new();

            public EntitlementSnapshot Get(int companyId) => throw new NotSupportedException();
            public EntitlementDecision Evaluate(int companyId, EntitlementAction action) => _evaluator(action);
            public void EnsureCanCreate(int companyId, MeteredResource resource, int additional = 1) => throw new NotSupportedException();
            public void EnsureCanCreate(MeteredResource resource, int additional = 1) => throw new NotSupportedException();
            public void Record(MeteredResource resource, int? companyId = null) => throw new NotSupportedException();
            public void EnsureCanOwnAnotherWorkspace(string userId) => throw new NotSupportedException();
            public void EnsureSelfServicePlan(string plan) => throw new NotSupportedException();
            public IDisposable Bypass() => throw new NotSupportedException();
        }

        public sealed class Fixture : IAsyncLifetime
        {
            private const string SecretKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            public string DbPath { get; } = Path.Combine(Path.GetTempPath(), "accountgo_ac0_conformance_" + Guid.NewGuid().ToString("N") + ".db");
            public WebApplicationFactory<Program> Factory { get; private set; } = null!;
            private HttpClient _client = null!;
            private readonly CapturingEmailSender _emails = new();

            public Task InitializeAsync()
            {
                Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting("DatabaseProvider", "Sqlite");
                    builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + DbPath);
                    builder.UseSetting("SECRET_KEY", SecretKey);
                    builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                    builder.UseSetting("PUBLIC_API_URL", "https://api.example");
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IEmailSender>();
                        services.AddSingleton<IEmailSender>(_emails);
                    });
                });
                _client = Factory.CreateClient();
                return Task.CompletedTask;
            }

            public Task DisposeAsync()
            {
                _client?.Dispose();
                Factory?.Dispose();
                foreach (var file in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                return Task.CompletedTask;
            }

            public async Task<UserSession> CreateUser(string email, bool isSystemAdministrator = false)
            {
                using var scope = Factory.Services.CreateScope();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                Assert.True((await userManager.CreateAsync(user)).Succeeded, email);

                if (isSystemAdministrator)
                {
                    if (!await roleManager.RoleExistsAsync("SystemAdministrators"))
                    {
                        await roleManager.CreateAsync(new IdentityRole("SystemAdministrators"));
                    }
                    await userManager.AddToRoleAsync(user, "SystemAdministrators");
                }

                var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
                return new UserSession(token.AccessToken, user.Id);
            }

            public string CreateCustomJwt(string userId, string? tokenUse = null)
            {
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, userId),
                    new(ClaimTypes.NameIdentifier, userId)
                };
                if (!string.IsNullOrWhiteSpace(tokenUse))
                {
                    claims.Add(new Claim("token_use", tokenUse));
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                var jwt = new JwtSecurityToken(
                    issuer: "AccountGoApi",
                    audience: "https://localhost:5001",
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(30),
                    signingCredentials: creds);
                return new JwtSecurityTokenHandler().WriteToken(jwt);
            }

            public async Task<int> CreateWorkspaceWithZeroFunctionalRoles(string token, string name, string slug)
            {
                var response = await Send(HttpMethod.Post, "/api/organizations", new { name, slug }, token, scoped: false);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                return int.Parse((await Json(response)).GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
            }

            public async Task AssignFunctionalRolesDirectAsync(int companyId, string userId, IEnumerable<string> roleNames)
            {
                using var scope = Factory.Services.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var member = db.CompanyMembers.Include(m => m.SecurityRoles)
                    .Single(m => m.CompanyId == companyId && m.UserId == userId);
                db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);

                var names = roleNames.ToList();
                if (names.Count > 0)
                {
                    var roleIds = db.SecurityRoles.Where(r => names.Contains(r.Name)).Select(r => r.Id).ToList();
                    foreach (var roleId in roleIds)
                    {
                        db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                        {
                            CompanyMemberId = member.Id,
                            SecurityRoleId = roleId
                        });
                    }
                }
                await db.SaveChangesAsync();
            }

            public async Task AddMemberDirectAsync(
                int companyId,
                string userId,
                CompanyMemberRole workspaceRole,
                IEnumerable<string> functionalRoleNames,
                CompanyMemberStatus status = CompanyMemberStatus.Active)
            {
                using var scope = Factory.Services.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var member = new CompanyMember
                {
                    CompanyId = companyId,
                    UserId = userId,
                    Role = workspaceRole,
                    Status = status,
                    JoinedAt = DateTime.UtcNow
                };
                db.CompanyMembers.Add(member);
                await db.SaveChangesAsync();

                var names = functionalRoleNames.ToList();
                if (names.Count > 0)
                {
                    var roleIds = db.SecurityRoles.Where(r => names.Contains(r.Name)).Select(r => r.Id).ToList();
                    foreach (var roleId in roleIds)
                    {
                        db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                        {
                            CompanyMemberId = member.Id,
                            SecurityRoleId = roleId
                        });
                    }
                    await db.SaveChangesAsync();
                }
            }

            public async Task<HashSet<string>> GetEffectivePermissionsViaHttpAsync(string token, string slug)
            {
                var res = await Send(HttpMethod.Get, "/api/organizations/current", token: token, org: slug);
                if (res.StatusCode != HttpStatusCode.OK)
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var json = await Json(res);
                return json.GetProperty("permissions")
                    .EnumerateArray()
                    .Select(p => p.GetString()!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            public string LastInvitationToken()
            {
                var html = _emails.Messages.Last(m => m.Subject.Contains(" invited you to ", StringComparison.Ordinal)).Html;
                return System.Text.RegularExpressions.Regex.Match(html, @"/invite/([A-Za-z0-9_-]{43})").Groups[1].Value;
            }

            public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null, string? org = null, bool scoped = true)
            {
                using var request = new HttpRequestMessage(method, path);
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (scoped)
                    request.Headers.Add("X-AccountGo-Org", org ?? "default-org");
                if (body != null)
                    request.Content = JsonContent.Create(body);
                return await _client.SendAsync(request);
            }

            public static async Task<JsonElement> Json(HttpResponseMessage response)
            {
                var text = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(text).RootElement.Clone();
            }

            public sealed record UserSession(string Token, string Id);

            private sealed class CapturingEmailSender : IEmailSender
            {
                public List<EmailMessage> Messages { get; } = new();

                public Task<string?> SendAsync(EmailMessage message, System.Threading.CancellationToken ct = default)
                {
                    Messages.Add(message);
                    return Task.FromResult<string?>("captured-" + Messages.Count);
                }
            }
        }
    }
}
