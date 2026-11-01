#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Api.Authorization;
using Api.Controllers;
using Api.Filters;
using Api.Mcp.Tools;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Architecture
{
    /// <summary>
    /// Automated architecture and coverage checks for shared access control conformance.
    /// Detects unclassified entry points, missing policy definitions, governance/functional conflation,
    /// and unapproved Owner/Admin permission shortcuts across backend and frontend adapters.
    /// </summary>
    public sealed class AuthorizationCoverageArchitectureTests
    {
        private static readonly string RepoRoot = ResolveRepoRoot();

        private static string ResolveRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "accountgo.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate accountgo.sln from AppContext.BaseDirectory.");
        }

        [Fact]
        public void Catalog_HasValidVersion_UniqueKeys_AndConsistentPolicyMetadata()
        {
            Assert.False(string.IsNullOrWhiteSpace(AccessOperationCatalog.CurrentVersion));
            var all = AccessOperationCatalog.GetAll();
            Assert.NotEmpty(all);

            var policyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in all)
            {
                var key = kvp.Key;
                var policy = kvp.Value;

                Assert.Equal(key, policy.OperationKey);
                Assert.False(string.IsNullOrWhiteSpace(policy.PolicyId));
                Assert.Equal(AccessOperationCatalog.CurrentVersion, policy.PolicyVersion);
                Assert.True(policyIds.Add(policy.PolicyId), $"Duplicate PolicyId '{policy.PolicyId}' in AccessOperationCatalog.");
                Assert.True(AccessOperationCatalog.TryGet(key, out var resolved) && resolved != null);
            }
        }

        [Fact]
        public void Catalog_GovernanceAndFunctionalPoliciesRemainDistinct_NoGovernanceRoleGrantsFinancialAccess()
        {
            var all = AccessOperationCatalog.GetAll().Values.ToList();

            var governancePolicies = all.Where(p => p.Domain == AccessPolicyDomain.WorkspaceGovernance).ToList();
            Assert.NotEmpty(governancePolicies);
            foreach (var gov in governancePolicies)
            {
                Assert.NotEmpty(gov.AllowedGovernanceRoles);
                Assert.Empty(gov.RequiredPermissionsAll);
                Assert.Empty(gov.RequiredPermissionsAny);
            }

            var functionalPolicies = all.Where(p => p.Domain == AccessPolicyDomain.WorkspaceFunctional).ToList();
            Assert.NotEmpty(functionalPolicies);
            foreach (var func in functionalPolicies)
            {
                // Only WorkspaceBrandingLogoView allows Owner/Admin governance OR sales.invoices.view for workspace logo display.
                // No financial, accounting, sales, purchasing, banking, inventory, donation, or report policy may allow governance roles as a substitute for functional permissions.
                if (!string.Equals(func.OperationKey, AccessOperationCatalog.WorkspaceBrandingLogoView, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Empty(func.AllowedGovernanceRoles);
                }

                Assert.True(
                    func.RequiredPermissionsAll.Count > 0 || func.RequiredPermissionsAny.Count > 0,
                    $"WorkspaceFunctional policy '{func.OperationKey}' must require at least one functional permission.");
            }
        }

        [Fact]
        public void AllPermissionConstants_MapToRegisteredCatalogPolicies()
        {
            var permissionFields = typeof(SystemPermissions)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();

            Assert.NotEmpty(permissionFields);

            foreach (var field in permissionFields)
            {
                var permission = (string)field.GetRawConstantValue()!;
                var opKey = AccessOperationCatalog.ForPermission(permission);
                Assert.True(
                    AccessOperationCatalog.TryGet(opKey, out var policy) && policy != null,
                    $"Permission constant '{field.Name}' ('{permission}') does not resolve to a registered catalog policy ('{opKey}').");
                Assert.Contains(permission, policy!.RequiredPermissionsAll);
            }
        }

        [Fact]
        public void Controllers_AllActionMethodsHaveExplicitTenancyAndAuthClassification_AndAttributesResolveInCatalog()
        {
            var apiAssembly = typeof(Program).Assembly;
            var controllers = apiAssembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                .ToList();

            Assert.NotEmpty(controllers);
            var errors = new List<string>();

            foreach (var controller in controllers)
            {
                errors.AddRange(ValidateControllerAuthorization(controller));
            }

            Assert.True(errors.Count == 0, "Unclassified endpoints:\n" + string.Join("\n", errors));
        }

        public static List<string> ValidateControllerAuthorization(Type controller)
        {
            var errors = new List<string>();
            var classTenantFree = controller.GetCustomAttribute<TenantFreeAttribute>(inherit: true) != null;
            var classTenantScoped = controller.GetCustomAttribute<TenantScopedAttribute>(inherit: true) != null;
            var classAllowAnon = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null;
            var classAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

            var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
                .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute || a is RouteAttribute))
                .ToList();

            foreach (var action in actions)
            {
                var methodTenantFree = action.GetCustomAttribute<TenantFreeAttribute>(inherit: true) != null;
                var methodTenantScoped = action.GetCustomAttribute<TenantScopedAttribute>(inherit: true) != null;
                var methodAllowAnon = action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null;
                var methodAuthorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

                var isClassifiedTenancy = classTenantFree || classTenantScoped || methodTenantFree || methodTenantScoped;
                if (!isClassifiedTenancy)
                {
                    errors.Add($"Unclassified tenancy on '{controller.Name}.{action.Name}': must be marked [TenantScoped] or [TenantFree].");
                }

                var methodPermsForAuth = action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)).ToList();
                var methodRolesForAuth = action.GetCustomAttributes<RequireWorkspaceRoleAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequireWorkspaceRoleAttribute>(inherit: true)).ToList();
                var methodOpsForAuth = action.GetCustomAttributes<RequireOperationAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequireOperationAttribute>(inherit: true)).ToList();

                var isClassifiedAuth = classAllowAnon || methodAllowAnon
                    || classAuthorize.Count > 0 || methodAuthorize.Count > 0
                    || methodPermsForAuth.Count > 0 || methodRolesForAuth.Count > 0 || methodOpsForAuth.Count > 0;
                if (!isClassifiedAuth)
                {
                    errors.Add($"Unclassified authorization on '{controller.Name}.{action.Name}': must be marked [Authorize], [AllowAnonymous], [RequirePermission], [RequireWorkspaceRole], or [RequireOperation].");
                }

                // Verify every RequirePermissionAttribute, RequireWorkspaceRoleAttribute, and RequireOperationAttribute maps to AccessOperationCatalog
                var permAttrs = action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true));
                foreach (var attr in permAttrs)
                {
                    var opKey = AccessOperationCatalog.ForPermission(attr.Permission);
                    if (!AccessOperationCatalog.TryGet(opKey, out _))
                    {
                        errors.Add($"Controller '{controller.Name}.{action.Name}' uses [RequirePermission(\"{attr.Permission}\")] which is not in AccessOperationCatalog.");
                    }
                }

                var roleAttrs = action.GetCustomAttributes<RequireWorkspaceRoleAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequireWorkspaceRoleAttribute>(inherit: true));
                foreach (var attr in roleAttrs)
                {
                    var opKey = AccessOperationCatalog.ForWorkspaceRole(attr.MinimumRole);
                    if (!AccessOperationCatalog.TryGet(opKey, out _))
                    {
                        errors.Add($"Controller '{controller.Name}.{action.Name}' uses [RequireWorkspaceRole({attr.MinimumRole})] which is not in AccessOperationCatalog.");
                    }
                }

                var opAttrs = action.GetCustomAttributes<RequireOperationAttribute>(inherit: true)
                    .Concat(controller.GetCustomAttributes<RequireOperationAttribute>(inherit: true));
                foreach (var attr in opAttrs)
                {
                    if (!AccessOperationCatalog.TryGet(attr.OperationKey, out _))
                    {
                        errors.Add($"Controller '{controller.Name}.{action.Name}' uses [RequireOperation(\"{attr.OperationKey}\")] which is not in AccessOperationCatalog.");
                    }
                }
            }
            return errors;
        }

        [Fact]
        public void ArchitectureGate_FailsWhenControllerActionLacksExplicitAuthorizationGuard()
        {
            var errors = ValidateControllerAuthorization(typeof(TestUnguardedCanaryController));
            Assert.Contains(errors, e => e.Contains("Unclassified authorization on 'TestUnguardedCanaryController.CanaryAction'"));
        }

        [Fact]
        public async Task TenantEndpointFilter_RejectsUnguardedTenantScopedActionAtRuntime()
        {
            var filter = new TenantEndpointFilter();
            var httpContext = new DefaultHttpContext();
            var services = new ServiceCollection();
            var tenantMock = new Mock<ITenantContext>();
            tenantMock.Setup(t => t.IsResolved).Returns(true);
            services.AddSingleton(tenantMock.Object);
            httpContext.RequestServices = services.BuildServiceProvider();

            var actionDescriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = typeof(TestUnguardedCanaryController).GetTypeInfo(),
                MethodInfo = typeof(TestUnguardedCanaryController).GetMethod(nameof(TestUnguardedCanaryController.CanaryAction))!
            };

            var context = new ActionExecutingContext(
                new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), actionDescriptor),
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                new TestUnguardedCanaryController());

            bool nextCalled = false;
            await filter.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new TestUnguardedCanaryController()));
            });

            Assert.False(nextCalled);
            Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(403, ((ObjectResult)context.Result).StatusCode);
        }

        [Core.Tenancy.TenantScoped]
        private class TestUnguardedCanaryController : ControllerBase
        {
            [HttpGet("test")]
            public IActionResult CanaryAction() => Ok();
        }

        [Fact]
        public void SensitiveFinancialAndBusinessControllers_EnforceFunctionalOrPlatformGuardOnEveryAction()
        {
            var businessControllers = new[]
            {
                typeof(FinancialsController),
                typeof(SalesController),
                typeof(PurchasingController),
                typeof(DonationsController),
                typeof(DonorsController),
                typeof(TaxController),
                typeof(AuditController),
                typeof(DashboardController),
                typeof(CommonController)
            };

            foreach (var controller in businessControllers)
            {
                var classPerms = controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).ToList();
                var classOps = controller.GetCustomAttributes<RequireOperationAttribute>(inherit: true).ToList();

                var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
                    .Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute || a is RouteAttribute))
                    .ToList();

                Assert.NotEmpty(actions);

                foreach (var action in actions)
                {
                    var methodPerms = action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).ToList();
                    var methodOps = action.GetCustomAttributes<RequireOperationAttribute>(inherit: true).ToList();
                    var methodPlatformRole = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                        .Concat(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                        .Any(a => string.Equals(a.Roles, "SystemAdministrators", StringComparison.Ordinal));
                    var hasFunctionalGuard = classPerms.Count > 0 || classOps.Count > 0 || methodPerms.Count > 0 || methodOps.Count > 0 || methodPlatformRole;

                    Assert.True(
                        hasFunctionalGuard,
                        $"Business controller action '{controller.Name}.{action.Name}' is missing an explicit [RequirePermission], [RequireOperation], or platform-admin [Authorize(Roles)] guard.");
                }
            }
        }

        [Fact]
        public void McpTools_AllToolMethodsAreCataloged_AndEnforceSharedAuthorizationContract()
        {
            var toolMethods = typeof(FinancialMcpTools)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
                .ToList();

            Assert.Equal(6, toolMethods.Count);

            var expectedToolNames = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(FinancialMcpTools.GetChartOfAccounts),
                nameof(FinancialMcpTools.GetTrialBalance),
                nameof(FinancialMcpTools.GetBalanceSheet),
                nameof(FinancialMcpTools.GetIncomeStatement),
                nameof(FinancialMcpTools.GetCashFlow),
                nameof(FinancialMcpTools.GetGeneralLedgerTransactions)
            };

            foreach (var method in toolMethods)
            {
                Assert.True(
                    expectedToolNames.Contains(method.Name),
                    $"Unclassified MCP tool method '{method.Name}' detected on FinancialMcpTools. Register its operation policy and conformance tests.");
            }
        }

        [Fact]
        public void SourceCode_ContainsNoOwnerOrAdminPermissionBypassShortcuts()
        {
            var middlewareSource = File.ReadAllText(Path.Combine(RepoRoot, "src/Api/Middleware/TenantResolutionMiddleware.cs"));
            Assert.DoesNotContain("Role == CompanyMemberRole.Owner || membership.Role == CompanyMemberRole.Admin", middlewareSource);
            Assert.DoesNotContain("membership.Role == CompanyMemberRole.Owner", middlewareSource);

            var mcpSource = File.ReadAllText(Path.Combine(RepoRoot, "src/Api/Mcp/Tools/FinancialMcpTools.cs"));
            Assert.DoesNotContain("CompanyMemberRole.Owner", mcpSource);
            Assert.DoesNotContain("CompanyMemberRole.Admin", mcpSource);
            Assert.Contains("Authorize", mcpSource);

            var orgLayoutSource = File.ReadAllText(Path.Combine(RepoRoot, "src/Web/src/layouts/OrgLayout.tsx"));
            Assert.DoesNotContain("currentOrg?.role === 'Owner' || currentOrg?.role === 'Admin'", orgLayoutSource);
            Assert.DoesNotContain("role === 'Owner' || role === 'Admin'", orgLayoutSource);
        }
    }
}
