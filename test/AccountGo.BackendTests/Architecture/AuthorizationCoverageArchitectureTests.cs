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
using System.Text.RegularExpressions;
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

        private static readonly IReadOnlyDictionary<string, string> AllowedCSharpFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core
            ["src/Core/Authorization/AccessOperationCatalog.cs"] = "governance: policy definitions and governance role requirements",

            // Api Tenancy & Auth
            ["src/Api/AccountGoApp.cs"] = "governance: tenant middleware and authentication setup",
            ["src/Api/Authorization/AccessAuthorizationService.cs"] = "governance: workspace role authorization and delegation evaluation",
            ["src/Api/Filters/AuthorizationFilters.cs"] = "governance: RequireWorkspaceRoleAttribute authorization filter",
            ["src/Api/Tenancy/TenantContext.cs"] = "governance: tenant context user impersonation and role tracking",

            // Api Data & Seeder
            ["src/Api/Data/ApiDbContext.cs"] = "governance: workspace member entity configuration and access bootstrap lifecycle",
            ["src/Api/Data/Seed/DatabaseSeeder.cs"] = "governance: seeding workspace impersonation",

            // Api Services
            ["src/Api/Service/AccountDeletionService.cs"] = "governance: workspace owner deletion authorization",
            ["src/Api/Service/CompanyProvisioningService.cs"] = "governance: initial workspace owner assignment",
            ["src/Api/Service/ContractAVerifier.cs"] = "governance: contract verification workspace role check",
            ["src/Api/Service/LinkedEntryService.cs"] = "governance: personal books link and cross-workspace access control",
            ["src/Api/Service/StartupWarmup.cs"] = "governance: warmup impersonation",
            ["src/Api/Service/WorkspaceOperatingPolicyService.cs"] = "governance: workspace administration and operating policies",
            ["src/Api/Service/WorkspacePolicyTransaction.cs"] = "governance: policy transaction authorization",
            ["src/Api/Service/WorkspaceReviewerEligibility.cs"] = "governance: independent journal reviewer eligibility requires Owner or Admin",
            ["src/Api/Service/WorkspaceService.cs"] = "governance: workspace ownership and role assignment",
            ["src/Api/Service/WorkspaceSetupCorrectionService.cs"] = "governance: setup correction role checks",

            // Api Controllers
            ["src/Api/Controllers/AdministrationController.cs"] = "governance: workspace administrative endpoints",
            ["src/Api/Controllers/BillingController.cs"] = "governance: workspace billing administration",
            ["src/Api/Controllers/FinancialsController.cs"] = "governance: chart of accounts replace requires Owner role in addition to permission",
            ["src/Api/Controllers/InvitationsController.cs"] = "governance: invitation role assignment",
            ["src/Api/Controllers/LinkedEntriesController.cs"] = "governance: linked entries management",
            ["src/Api/Controllers/OrganizationsController.PersonalBooks.cs"] = "governance: personal books ownership and link management",
            ["src/Api/Controllers/OrganizationsController.cs"] = "governance: organization administration and ownership transfer",
            ["src/Api/Controllers/SessionPolicyController.cs"] = "governance: session policy administration",
            ["src/Api/Controllers/SubscriptionController.cs"] = "governance: subscription administration",
            ["src/Api/Controllers/WorkspaceInvitationsController.cs"] = "governance: workspace invitation authorization",
            ["src/Api/Controllers/WorkspaceMembersController.cs"] = "governance: member management, roles, and ownership transfer",

            // Commercial Modules (if present)
            ["modules/Billing/Announcements/HostedAnnouncementSource.cs"] = "governance: hosted announcement visibility",
            ["modules/Billing/Services/HostedEntitlementService.cs"] = "governance: hosted entitlement role check",
            ["modules/Billing/Webhooks/BillingWebhooksController.cs"] = "governance: webhook subscription provisioning",
            ["modules/Branding/Controllers/InvoiceBrandingSettingsController.cs"] = "governance: invoice branding settings administration",
            ["modules/Branding/Controllers/InvoiceLogoController.cs"] = "governance: invoice logo administration",
            ["modules/Branding/Services/BrandingAuthorization.cs"] = "governance: branding authorization role check",
            ["modules/Branding/Services/InvoiceLogoUploadAdmissionAttribute.cs"] = "governance: logo upload admission check",
            ["modules/Email/Controllers/EmailSettingsController.cs"] = "governance: email settings administration",
            ["modules/Email/Services/EmailAuthorization.cs"] = "governance: email configuration authorization",
            ["modules/Email/Services/HostedEmailCapabilities.cs"] = "governance: email capabilities role check",
            ["modules/Security/Controllers/SecurityPolicyController.cs"] = "governance: security policy administration",
            ["modules/Security/Entitlements/MfaEntitlements.cs"] = "governance: MFA policy enforcement",

            // Commercial Module Tests (if present)
            ["modules/Billing.Tests/HostedAnnouncementSourceTests.cs"] = "test: announcement test fixture",
            ["modules/Billing.Tests/HostedCreateReceiveWorkspaceTests.cs"] = "test: workspace creation fixture",
            ["modules/Billing.Tests/HostedEntitlementServiceContractTests.cs"] = "test: entitlement contract fixture",
            ["modules/Billing.Tests/HostedFreeWorkspaceSlotTests.cs"] = "test: workspace slot fixture",
            ["modules/Billing.Tests/PersonalFreePlanTests.cs"] = "test: personal free plan fixture",
            ["modules/Branding.Tests/BrandingApiFixture.cs"] = "test: branding test fixture",
            ["modules/Branding.Tests/InvoiceBrandingPolicyTests.cs"] = "test: branding policy fixture",
            ["modules/Branding.Tests/InvoiceLogoTests.cs"] = "test: invoice logo fixture",
            ["modules/Email.Tests/EmailAccessControlConformanceTests.cs"] = "test: email access control fixture",
            ["modules/Email.Tests/EmailFixture.cs"] = "test: email fixture",
            ["modules/Integrations.Tests/IntegrationsFixture.cs"] = "test: integrations fixture",
            ["modules/Integrations.Tests/RelationalDatabaseTests.cs"] = "test: relational database fixture",
            ["modules/Security.Tests/HostedMfaGateAndPolicyTests.cs"] = "test: MFA gate fixture",
            ["modules/Security.Tests/TotpAndRecoveryTests.cs"] = "test: recovery codes fixture",
        };

        private static readonly IReadOnlyDictionary<string, string> AllowedWebFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Web/src/App.tsx"] = "governance: settings page route protection",
            ["src/Web/src/components/MobileBottomNav.tsx"] = "governance: mobile admin navigation visibility",
            ["src/Web/src/components/PersonalBooksDialog.tsx"] = "governance: personal books link authorization",
            ["src/Web/src/components/Sidebar.tsx"] = "governance: sidebar admin navigation visibility",
            ["src/Web/src/pages/CreateOrg/hooks/useCreateOrgWizard.ts"] = "governance: organization creation owner verification",
            ["src/Web/src/pages/Dashboard.tsx"] = "governance: workspace management widget visibility",
            ["src/Web/src/pages/JournalEntries.tsx"] = "governance: independent journal review requires Owner or Admin in addition to post permission",
            ["src/Web/src/pages/Members.tsx"] = "governance: workspace member management and ownership transfer",
            ["src/Web/src/pages/Settings.tsx"] = "governance: workspace administration settings tabs",
        };

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

            // 1. Scan C# files under src/ and modules/ (excluding bin, obj, .build)
            var csharpScanDirs = new[] { Path.Combine(RepoRoot, "src"), Path.Combine(RepoRoot, "modules") };
            var unapprovedCSharp = new List<string>();

            foreach (var dir in csharpScanDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
                    var parts = rel.Split('/');
                    if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                    || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                    || p.Equals(".build", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (content.Contains("CompanyMemberRole.Owner") || content.Contains("CompanyMemberRole.Admin"))
                    {
                        if (!AllowedCSharpFiles.ContainsKey(rel))
                        {
                            unapprovedCSharp.Add(rel);
                        }
                    }
                }
            }

            // Check for stale C# allowlist entries
            var staleCSharp = new List<string>();
            foreach (var kvp in AllowedCSharpFiles)
            {
                var full = Path.Combine(RepoRoot, kvp.Key);
                if (File.Exists(full))
                {
                    var content = File.ReadAllText(full);
                    if (!content.Contains("CompanyMemberRole.Owner") && !content.Contains("CompanyMemberRole.Admin"))
                    {
                        staleCSharp.Add(kvp.Key);
                    }
                }
            }

            // 2. Scan Web TS/TSX files under src/Web/src
            var webScanDir = Path.Combine(RepoRoot, "src", "Web", "src");
            var webRoleRegex = new Regex(@"\brole\s*(===|!==|==|!=)\s*['""](Owner|Admin)['""]", RegexOptions.Compiled);
            var unapprovedWeb = new List<string>();

            if (Directory.Exists(webScanDir))
            {
                foreach (var file in Directory.EnumerateFiles(webScanDir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
                    var parts = rel.Split('/');
                    if (parts.Any(p => p.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                                    || p.Equals("dist", StringComparison.OrdinalIgnoreCase)
                                    || p.Equals(".build", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (webRoleRegex.IsMatch(content))
                    {
                        if (!AllowedWebFiles.ContainsKey(rel))
                        {
                            unapprovedWeb.Add(rel);
                        }
                    }
                }
            }

            // Check for stale Web allowlist entries
            var staleWeb = new List<string>();
            foreach (var kvp in AllowedWebFiles)
            {
                var full = Path.Combine(RepoRoot, kvp.Key);
                if (File.Exists(full))
                {
                    var content = File.ReadAllText(full);
                    if (!webRoleRegex.IsMatch(content))
                    {
                        staleWeb.Add(kvp.Key);
                    }
                }
            }

            Assert.True(
                unapprovedCSharp.Count == 0,
                $"Unapproved C# files containing CompanyMemberRole.Owner or CompanyMemberRole.Admin shortcuts:\n{string.Join("\n", unapprovedCSharp)}");

            Assert.True(
                staleCSharp.Count == 0,
                $"Stale C# allowlist entries (no longer match Owner/Admin pattern):\n{string.Join("\n", staleCSharp)}");

            Assert.True(
                unapprovedWeb.Count == 0,
                $"Unapproved Web files containing role === 'Owner' / 'Admin' shortcuts:\n{string.Join("\n", unapprovedWeb)}");

            Assert.True(
                staleWeb.Count == 0,
                $"Stale Web allowlist entries (no longer match role === 'Owner' / 'Admin'):\n{string.Join("\n", staleWeb)}");
        }
    }
}
