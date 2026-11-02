#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Authorization
{
    /// <summary>
    /// Default implementation of <see cref="IAccessAuthorizationService"/> enforcing the versioned
    /// operation/policy catalog across HTTP, MCP, application services, modules, and jobs.
    /// </summary>
    public sealed class AccessAuthorizationService : IAccessAuthorizationService
    {
        public const string ApprovalAuditTableName = "__AccessDelegationApproval";

        private static readonly IReadOnlySet<string> EmptyStringSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static long _globalSecurityRevision = 1;

        public static long GlobalSecurityRevision => Interlocked.Read(ref _globalSecurityRevision);

        public static long BumpGlobalSecurityRevision() => Interlocked.Increment(ref _globalSecurityRevision);

        public static string ComputeJournalFingerprint(
            DateTime date,
            string? memo,
            int voucherType,
            IEnumerable<(int AccountId, DrOrCrSide DrCr, decimal Amount, string? Memo)> lines)
        {
            return AccessJournalFingerprint.Compute(date, memo, voucherType, lines);
        }

        /// <summary>
        /// High-risk financial and administrative mutation permissions that cannot be unilaterally delegated
        /// to another account by an administrator who does not already possess them (or an independent approver).
        /// </summary>
        private static readonly HashSet<string> SensitiveDelegatedPermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            SystemPermissions.FinancialsAccountsManage,
            SystemPermissions.FinancialsJournalCreate,
            SystemPermissions.FinancialsJournalPost,
            SystemPermissions.FinancialsPeriodsClose,
            SystemPermissions.BankingAccountsManage,
            SystemPermissions.BankingTransactionsCreate,
            SystemPermissions.BankingReconcile,
            SystemPermissions.BankingReconcileUndo,
            SystemPermissions.SalesInvoicesCreate,
            SystemPermissions.SalesInvoicesPost,
            SystemPermissions.SalesReceiptsCreate,
            SystemPermissions.PurchasesBillsCreate,
            SystemPermissions.PurchasesBillsPost,
            SystemPermissions.PurchasesPaymentsCreate,
            SystemPermissions.InventoryAdjustmentsPost,
            SystemPermissions.DonationsManage,
            SystemPermissions.AdminTaxesManage,
            SystemPermissions.AdminRolesManage
        };

        private readonly ITenantContext _tenantContext;
        private readonly IEntitlementService? _entitlements;
        private readonly ApiDbContext? _db;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly IMfaGate? _mfaGate;
        private readonly IWorkspaceApprovalPolicyResolver? _approvalPolicyResolver;
        private readonly ILogger<AccessAuthorizationService>? _logger;

        private AccessActorContext? _cachedActor;
        private long _cachedTenantRevision = -1;
        private int? _cachedCompanyId;
        private string? _cachedUserId;

        public AccessAuthorizationService(
            ITenantContext tenantContext,
            IEntitlementService? entitlements = null,
            ApiDbContext? db = null,
            IHttpContextAccessor? httpContextAccessor = null,
            IMfaGate? mfaGate = null,
            IWorkspaceApprovalPolicyResolver? approvalPolicyResolver = null,
            ILogger<AccessAuthorizationService>? logger = null)
        {
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
            _entitlements = entitlements;
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _mfaGate = mfaGate;
            _approvalPolicyResolver = approvalPolicyResolver;
            _logger = logger;
        }

        public string CatalogVersion => AccessOperationCatalog.CurrentVersion;

        public AccessOperationPolicy? GetPolicy(string operationKey) => AccessOperationCatalog.GetPolicy(operationKey);

        public void InvalidateContextCache()
        {
            _cachedActor = null;
            _cachedTenantRevision = -1;
            _cachedCompanyId = null;
            _cachedUserId = null;
            BumpGlobalSecurityRevision();
            if (_tenantContext is TenantContext tc)
            {
                tc.BumpRevision();
            }
        }

        public AccessActorContext ResolveActorContext(AccessActorContext? actorOverride = null)
        {
            if (actorOverride != null)
            {
                if (actorOverride.TrustDomain == ActorTrustDomain.WorkspaceUser &&
                    !actorOverride.MembershipStatus.HasValue &&
                    !string.IsNullOrWhiteSpace(actorOverride.UserId) &&
                    _db != null)
                {
                    var targetCompanyId = actorOverride.CompanyId ?? _tenantContext.CompanyId;
                    var resolvedOverride = BuildActorContextFromCurrentState(
                        _httpContextAccessor?.HttpContext?.User,
                        actorOverride.UserId,
                        targetCompanyId,
                        _tenantContext.Revision);
                    if (resolvedOverride.FunctionalPermissions.Count == 0 && actorOverride.FunctionalPermissions.Count > 0)
                    {
                        return resolvedOverride with { FunctionalPermissions = actorOverride.FunctionalPermissions };
                    }
                    return resolvedOverride;
                }

                return actorOverride;
            }

            var currentRevision = _tenantContext.Revision;
            var currentCompanyId = _tenantContext.CompanyId;
            var httpContext = _httpContextAccessor?.HttpContext;
            var principal = httpContext?.User;
            var claimUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue("sub");
            var effectiveUserId = string.IsNullOrWhiteSpace(_tenantContext.UserId) ? claimUserId : _tenantContext.UserId;

            // Always resolve live membership and functional grants from the database when available so
            // external revocations in another scope or request take effect immediately without stale cache reuse.
            var resolved = BuildActorContextFromCurrentState(principal, effectiveUserId, currentCompanyId, currentRevision);
            _cachedActor = resolved;
            _cachedTenantRevision = currentRevision;
            _cachedCompanyId = currentCompanyId;
            _cachedUserId = effectiveUserId;
            return resolved;
        }

        public AccessDecision Authorize(
            string operationKey,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null)
        {
            return AuthorizeAsync(operationKey, resource, actorOverride, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public AccessDecision AuthorizePermission(
            string permission,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null)
        {
            return Authorize(AccessOperationCatalog.ForPermission(permission), resource, actorOverride);
        }

        public AccessDecision AuthorizeWorkspaceRole(
            CompanyMemberRole minimumRole,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null)
        {
            return Authorize(AccessOperationCatalog.ForWorkspaceRole(minimumRole), resource, actorOverride);
        }

        public async Task<AccessDecision> AuthorizeAsync(
            string operationKey,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationKey) || !AccessOperationCatalog.TryGet(operationKey, out var policy) || policy == null)
            {
                return AccessDecision.Deny(
                    AccessReasonCode.UnknownOperation,
                    operationKey ?? string.Empty,
                    "POL-UNKNOWN",
                    CatalogVersion,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "The requested operation is not registered in the authorization policy catalog.");
            }

            try
            {
                var actor = ResolveActorContext(actorOverride);
                return await EvaluatePolicyAsync(policy, resource, actor, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Authorization dependency failure while evaluating operation '{OperationKey}' (policy '{PolicyId}' v'{PolicyVersion}'). Failing closed.",
                    policy.OperationKey,
                    policy.PolicyId,
                    policy.PolicyVersion);

                return AccessDecision.Deny(
                    AccessReasonCode.PolicyDependencyFailure,
                    policy.OperationKey,
                    policy.PolicyId,
                    policy.PolicyVersion,
                    StatusCodes.Status503ServiceUnavailable,
                    "Authorization Unavailable",
                    "Access could not be verified due to an internal policy dependency error.");
            }
        }

        public AccessDecision AuthorizeRequirement(
            IReadOnlyList<string>? requiredPermissionsAll = null,
            IReadOnlyList<string>? requiredPermissionsAny = null,
            IReadOnlyList<CompanyMemberRole>? allowedGovernanceRoles = null,
            IReadOnlyList<string>? requiredModulesAny = null,
            string? requiredFeature = null,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null,
            string? operationKey = null)
        {
            var hasGov = allowedGovernanceRoles is { Count: > 0 };
            var hasPerm = (requiredPermissionsAll is { Count: > 0 }) || (requiredPermissionsAny is { Count: > 0 });
            var domain = hasGov && hasPerm
                ? AccessPolicyDomain.WorkspaceComposite
                : hasGov
                    ? AccessPolicyDomain.WorkspaceGovernance
                    : AccessPolicyDomain.WorkspaceFunctional;

            // S5: Fail closed immediately if an operationKey is explicitly specified but not registered in catalog
            if (!string.IsNullOrWhiteSpace(operationKey) && !AccessOperationCatalog.TryGet(operationKey, out _))
            {
                return AccessDecision.Deny(
                    AccessReasonCode.UnknownOperation,
                    operationKey,
                    "POL-UNKNOWN",
                    CatalogVersion,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    $"The specified operation '{operationKey}' is not registered in the catalog.");
            }

            var syntheticKey = !string.IsNullOrWhiteSpace(operationKey)
                ? operationKey!
                : hasPerm
                    ? (requiredPermissionsAll?.FirstOrDefault() ?? requiredPermissionsAny?.FirstOrDefault() ?? "workspace.functional.adhoc")
                    : "workspace.governance.adhoc";

            // Always start from the registered catalog policy when available.
            // S4: Evaluate catalog policy and additional requirements conjunctively (AND), never erasing requirements.
            AccessOperationPolicy? catalogPolicy = null;
            if (!string.IsNullOrWhiteSpace(operationKey))
            {
                AccessOperationCatalog.TryGet(operationKey, out catalogPolicy);
            }
            else if (requiredPermissionsAll is { Count: 1 })
            {
                AccessOperationCatalog.TryGet(AccessOperationCatalog.ForPermission(requiredPermissionsAll[0]), out catalogPolicy);
            }

            try
            {
                var actor = ResolveActorContext(actorOverride);

                if (catalogPolicy != null)
                {
                    var catalogDecision = EvaluatePolicyAsync(catalogPolicy, resource, actor, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    if (!catalogDecision.Allowed)
                    {
                        return catalogDecision;
                    }
                }

                // Evaluate additional ad-hoc requirement constraints
                var hasExtraGov = allowedGovernanceRoles is { Count: > 0 };
                var hasExtraPermsAll = requiredPermissionsAll is { Count: > 0 };
                var hasExtraPermsAny = requiredPermissionsAny is { Count: > 0 };
                var hasExtraModules = requiredModulesAny is { Count: > 0 };
                var hasExtraFeature = !string.IsNullOrWhiteSpace(requiredFeature);

                if (catalogPolicy == null || hasExtraGov || hasExtraPermsAll || hasExtraPermsAny || hasExtraModules || hasExtraFeature)
                {
                    var effectiveDomain = (hasExtraGov && (hasExtraPermsAll || hasExtraPermsAny))
                        ? AccessPolicyDomain.WorkspaceComposite
                        : (hasExtraGov ? AccessPolicyDomain.WorkspaceGovernance : domain);

                    var adhocPolicy = new AccessOperationPolicy
                    {
                        OperationKey = syntheticKey,
                        PolicyId = $"POL-REQ-{syntheticKey.ToUpperInvariant()}",
                        PolicyVersion = CatalogVersion,
                        Domain = effectiveDomain,
                        AllowedGovernanceRoles = allowedGovernanceRoles ?? Array.Empty<CompanyMemberRole>(),
                        RequiredPermissionsAll = requiredPermissionsAll ?? Array.Empty<string>(),
                        RequiredPermissionsAny = requiredPermissionsAny ?? Array.Empty<string>(),
                        RequiredModulesAny = requiredModulesAny ?? Array.Empty<string>(),
                        RequiredFeature = requiredFeature,
                        RequiresResolvedTenant = true
                    };

                    return EvaluatePolicyAsync(adhocPolicy, resource, actor, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                return AccessDecision.Allow(catalogPolicy.OperationKey, catalogPolicy.PolicyId, catalogPolicy.PolicyVersion);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Authorization dependency failure during requirement check '{OperationKey}'. Failing closed.", syntheticKey);
                return AccessDecision.Deny(
                    AccessReasonCode.PolicyDependencyFailure,
                    syntheticKey,
                    catalogPolicy?.PolicyId ?? $"POL-REQ-{syntheticKey.ToUpperInvariant()}",
                    catalogPolicy?.PolicyVersion ?? CatalogVersion,
                    StatusCodes.Status503ServiceUnavailable,
                    "Authorization Unavailable",
                    "Access could not be verified due to an internal policy dependency error.");
            }
        }

        public IReadOnlySet<string> GetAuthorizedDashboardDomains(AccessActorContext? actorOverride = null)
        {
            var actor = ResolveActorContext(actorOverride);
            if (actor.TrustDomain != ActorTrustDomain.WorkspaceUser ||
                actor.MembershipStatus != CompanyMemberStatus.Active ||
                !actor.CompanyId.HasValue ||
                _entitlements == null)
            {
                return EmptyStringSet;
            }

            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perms = actor.FunctionalPermissions;

            bool IsModuleAllowed(params string[] modules)
            {
                if (_entitlements == null || !actor.CompanyId.HasValue || modules.Length == 0)
                    return false;
                return _entitlements.Evaluate(
                    actor.CompanyId.Value,
                    new EntitlementAction.Module(ModuleAccess.Read, modules)).Allowed;
            }

            if ((perms.Contains(SystemPermissions.FinancialsAccountsView) ||
                 perms.Contains(SystemPermissions.FinancialsJournalView) ||
                 perms.Contains(SystemPermissions.ReportsView)) &&
                IsModuleAllowed(SystemModules.Financials, SystemModules.Reports))
            {
                domains.Add("financials");
            }

            if (perms.Contains(SystemPermissions.SalesInvoicesView) &&
                IsModuleAllowed(SystemModules.Sales))
            {
                domains.Add("sales");
            }

            if (perms.Contains(SystemPermissions.PurchasesBillsView) &&
                IsModuleAllowed(SystemModules.Purchases))
            {
                domains.Add("purchases");
            }

            if (perms.Contains(SystemPermissions.BankingAccountsView) &&
                IsModuleAllowed(SystemModules.Financials, SystemModules.Sales, SystemModules.Purchases))
            {
                domains.Add("banking");
            }

            if (perms.Contains(SystemPermissions.FinancialsPeriodsClose) &&
                IsModuleAllowed(SystemModules.Financials))
            {
                domains.Add("periods_close");
            }

            return domains;
        }

        private AccessActorContext BuildActorContextFromCurrentState(
            ClaimsPrincipal? principal,
            string? effectiveUserId,
            int? companyId,
            long revision)
        {
            var isAuthenticated = principal?.Identity?.IsAuthenticated == true;
            var authType = principal?.Identity?.AuthenticationType;

            if (isAuthenticated)
            {
                var tokenUse = principal!.FindFirstValue("token_use");
                if (string.Equals(tokenUse, "mfa_pending", StringComparison.Ordinal) ||
                    string.Equals(tokenUse, "mfa_enroll", StringComparison.Ordinal))
                {
                    return new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.PendingAuthentication,
                        UserId = effectiveUserId,
                        MembershipRevision = revision
                    };
                }

                if (string.Equals(authType, "StaffSession", StringComparison.Ordinal) ||
                    principal.HasClaim(c => c.Type == "staff_session_id"))
                {
                    return new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.StaffOperator,
                        UserId = effectiveUserId,
                        MembershipRevision = revision
                    };
                }

                if (string.Equals(authType, "McpGate", StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(effectiveUserId))
                {
                    return new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.TransportGate,
                        CompanyId = companyId,
                        CompanySlug = _tenantContext.CompanySlug,
                        MembershipRevision = revision
                    };
                }
            }

            var isPlatformAdmin = principal?.IsInRole("SystemAdministrators") == true;
            DateTimeOffset? mfaVerifiedAt = null;
            var isMfaStepUpVerified = string.Equals(principal?.FindFirstValue("step_up"), "true", StringComparison.OrdinalIgnoreCase);
            var rawMfaVerifiedAt = principal?.FindFirstValue("mfa_verified_at");
            if (!string.IsNullOrWhiteSpace(rawMfaVerifiedAt) &&
                long.TryParse(rawMfaVerifiedAt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                mfaVerifiedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                if (DateTimeOffset.UtcNow - mfaVerifiedAt.Value <= TimeSpan.FromSeconds(900))
                {
                    isMfaStepUpVerified = true;
                }
            }

            // If tenant context is resolved, verify live membership when DbContext + UserId are available.
            var resolvedCompanyId = companyId ?? (_tenantContext.IsResolved && _db == null ? 1 : null);
            if (_tenantContext.IsResolved && resolvedCompanyId.HasValue)
            {
                if (_tenantContext.ActorDomain == ActorTrustDomain.ScopedService)
                {
                    var scopedPermissions = _tenantContext.Permissions != null
                        ? new HashSet<string>(_tenantContext.Permissions, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    return new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.ScopedService,
                        UserId = effectiveUserId ?? "system:scoped-service",
                        CompanyId = resolvedCompanyId.Value,
                        CompanySlug = _tenantContext.CompanySlug,
                        WorkspaceRole = _tenantContext.Role,
                        MembershipStatus = CompanyMemberStatus.Active,
                        FunctionalPermissions = scopedPermissions,
                        AssignedFunctionalRoles = new List<string> { "ScopedService" },
                        IsPlatformAdministrator = isPlatformAdmin,
                        IsMfaStepUpVerified = true,
                        MembershipRevision = revision
                    };
                }

                if (_db != null && !string.IsNullOrWhiteSpace(effectiveUserId))
                {
                    var isCompanyDeleted = _db.Companies.AsNoTracking()
                        .Any(c => c.Id == resolvedCompanyId.Value && c.DeletedAt != null);
                    if (isCompanyDeleted)
                    {
                        return new AccessActorContext
                        {
                            TrustDomain = isAuthenticated ? ActorTrustDomain.WorkspaceUser : ActorTrustDomain.Unauthenticated,
                            UserId = effectiveUserId,
                            CompanyId = null,
                            MembershipStatus = null,
                            IsPlatformAdministrator = isPlatformAdmin,
                            IsMfaStepUpVerified = isMfaStepUpVerified,
                            MfaVerifiedAt = mfaVerifiedAt,
                            MembershipRevision = revision
                        };
                    }

                    var member = _db.CompanyMembers.AsNoTracking()
                        .Include(m => m.SecurityRoles)
                        .FirstOrDefault(m => m.CompanyId == resolvedCompanyId.Value && m.UserId == effectiveUserId);

                    if (member == null || member.Status != CompanyMemberStatus.Active)
                    {
                        return new AccessActorContext
                        {
                            TrustDomain = isPlatformAdmin ? ActorTrustDomain.PlatformAdministrator : ActorTrustDomain.WorkspaceUser,
                            UserId = effectiveUserId,
                            CompanyId = resolvedCompanyId.Value,
                            CompanySlug = _tenantContext.CompanySlug,
                            MemberId = member?.Id,
                            WorkspaceRole = null,
                            MembershipStatus = member?.Status,
                            FunctionalPermissions = EmptyStringSet,
                            IsPlatformAdministrator = isPlatformAdmin,
                            IsMfaStepUpVerified = isMfaStepUpVerified,
                            MfaVerifiedAt = mfaVerifiedAt,
                            MembershipRevision = revision
                        };
                    }

                    // Resolve functional permissions strictly from CompanyMemberSecurityRoles (never from Owner/Admin).
                    var roleIds = member.SecurityRoles.Select(r => r.SecurityRoleId).ToList();
                    HashSet<string> livePermissions;
                    List<string> roleNames;
                    if (roleIds.Count > 0)
                    {
                        var perms = _db.Set<SecurityRolePermission>()
                            .AsNoTracking()
                            .Where(srp => roleIds.Contains(srp.SecurityRoleId) && srp.SecurityPermission != null)
                            .Select(srp => srp.SecurityPermission!.Name)
                            .Distinct()
                            .ToList();
                        livePermissions = new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);

                        roleNames = _db.SecurityRoles.AsNoTracking()
                            .Where(r => roleIds.Contains(r.Id))
                            .Select(r => r.Name)
                            .ToList();
                    }
                    else
                    {
                        livePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        roleNames = new List<string>();
                    }

                    return new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.WorkspaceUser,
                        UserId = effectiveUserId,
                        CompanyId = resolvedCompanyId.Value,
                        CompanySlug = _tenantContext.CompanySlug,
                        MemberId = member.Id,
                        WorkspaceRole = member.Role,
                        MembershipStatus = member.Status,
                        FunctionalPermissions = livePermissions,
                        AssignedFunctionalRoles = roleNames,
                        IsPlatformAdministrator = isPlatformAdmin,
                        IsMfaStepUpVerified = isMfaStepUpVerified,
                        MfaVerifiedAt = mfaVerifiedAt,
                        MembershipRevision = revision
                    };
                }

                // Fallback for unit-test / in-process impersonation contexts where no UserId or DbContext is attached:
                // Use only explicitly populated permissions on ITenantContext; never synthesize permissions from Owner/Admin.
                var isScopedService = _tenantContext.ActorDomain == ActorTrustDomain.ScopedService;
                var explicitPermissions = _tenantContext.Permissions != null
                    ? new HashSet<string>(_tenantContext.Permissions, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var fallbackDomain = isScopedService
                    ? ActorTrustDomain.ScopedService
                    : (_db == null
                        ? (isAuthenticated || !string.IsNullOrWhiteSpace(effectiveUserId) || _tenantContext.Permissions != null
                            ? ActorTrustDomain.WorkspaceUser
                            : ActorTrustDomain.Unauthenticated)
                        : ActorTrustDomain.Unauthenticated);

                return new AccessActorContext
                {
                    TrustDomain = fallbackDomain,
                    UserId = effectiveUserId,
                    CompanyId = resolvedCompanyId.Value,
                    CompanySlug = _tenantContext.CompanySlug,
                    MemberId = _tenantContext.MemberId,
                    WorkspaceRole = (isScopedService || _db == null) ? (_tenantContext.Role ?? CompanyMemberRole.Member) : null,
                    MembershipStatus = (isScopedService || _db == null) && (_tenantContext.Role.HasValue || _tenantContext.Permissions != null)
                        ? CompanyMemberStatus.Active
                        : null,
                    FunctionalPermissions = (isScopedService || _db == null) ? explicitPermissions : EmptyStringSet,
                    IsPlatformAdministrator = isPlatformAdmin,
                    IsMfaStepUpVerified = isMfaStepUpVerified,
                    MfaVerifiedAt = mfaVerifiedAt,
                    MembershipRevision = revision
                };
            }

            if (isPlatformAdmin)
            {
                return new AccessActorContext
                {
                    TrustDomain = ActorTrustDomain.PlatformAdministrator,
                    UserId = effectiveUserId,
                    IsPlatformAdministrator = true,
                    IsMfaStepUpVerified = isMfaStepUpVerified,
                    MfaVerifiedAt = mfaVerifiedAt,
                    MembershipRevision = revision
                };
            }

            if (isAuthenticated || !string.IsNullOrWhiteSpace(effectiveUserId))
            {
                return new AccessActorContext
                {
                    TrustDomain = ActorTrustDomain.WorkspaceUser,
                    UserId = effectiveUserId,
                    IsMfaStepUpVerified = isMfaStepUpVerified,
                    MfaVerifiedAt = mfaVerifiedAt,
                    MembershipRevision = revision
                };
            }

            return AccessActorContext.Unauthenticated();
        }

        private async Task<AccessDecision> EvaluatePolicyAsync(
            AccessOperationPolicy policy,
            AccessResourceContext? resource,
            AccessActorContext actor,
            CancellationToken cancellationToken)
        {
            // 1. Reject pre-authentication MFA tokens everywhere outside MFA challenge endpoints
            if (actor.TrustDomain == ActorTrustDomain.PendingAuthentication)
            {
                return AccessDecision.Deny(
                    AccessReasonCode.PendingAuthenticationToken,
                    policy.OperationKey,
                    policy.PolicyId,
                    policy.PolicyVersion,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Multi-factor authentication verification is required before accessing this resource.");
            }

            // 2. Domain-specific trust boundary checks
            switch (policy.Domain)
            {
                case AccessPolicyDomain.Public:
                    return AccessDecision.Allow(policy.OperationKey, policy.PolicyId, policy.PolicyVersion);

                case AccessPolicyDomain.ServicePrincipal:
                    if (actor.TrustDomain != ActorTrustDomain.ServicePrincipal ||
                        string.IsNullOrWhiteSpace(actor.ServicePrincipalId) ||
                        (!string.IsNullOrWhiteSpace(policy.AllowedServicePrincipalId) &&
                         !string.Equals(actor.ServicePrincipalId, policy.AllowedServicePrincipalId, StringComparison.Ordinal)))
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.ActorDomainMismatch,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "This operation requires an explicitly authorized service principal.");
                    }
                    break;

                case AccessPolicyDomain.StaffConsole:
                    if (actor.TrustDomain != ActorTrustDomain.StaffOperator)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.ActorDomainMismatch,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "Staff console authentication is required.");
                    }
                    return AccessDecision.Allow(policy.OperationKey, policy.PolicyId, policy.PolicyVersion);

                case AccessPolicyDomain.PlatformAdmin:
                    if (actor.TrustDomain == ActorTrustDomain.Unauthenticated)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.Unauthenticated,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status401Unauthorized,
                            "Unauthorized",
                            "Authentication is required.");
                    }
                    if (!actor.IsPlatformAdministrator ||
                        actor.TrustDomain is ActorTrustDomain.StaffOperator or ActorTrustDomain.TransportGate or ActorTrustDomain.ServicePrincipal)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.ActorDomainMismatch,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "Platform administrator authority is required for this global configuration action.");
                    }
                    return AccessDecision.Allow(policy.OperationKey, policy.PolicyId, policy.PolicyVersion);

                case AccessPolicyDomain.SelfService:
                case AccessPolicyDomain.WorkspaceFunctional:
                case AccessPolicyDomain.WorkspaceGovernance:
                case AccessPolicyDomain.WorkspaceComposite:
                    if (actor.TrustDomain == ActorTrustDomain.Unauthenticated)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.Unauthenticated,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status401Unauthorized,
                            "Unauthorized",
                            "Authentication is required.");
                    }
                    if (actor.TrustDomain is ActorTrustDomain.StaffOperator or ActorTrustDomain.TransportGate or ActorTrustDomain.ServicePrincipal)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.ActorDomainMismatch,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "Staff, service, and transport identities cannot access workspace operations.");
                    }
                    if (actor.TrustDomain == ActorTrustDomain.PlatformAdministrator &&
                        actor.MembershipStatus != CompanyMemberStatus.Active)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.ActorDomainMismatch,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "Platform administrator role does not grant access to workspace operations without an active workspace membership.");
                    }
                    break;
            }

            // 3. Workspace resolution & active membership check
            var targetCompanyId = actor.CompanyId ?? resource?.WorkspaceCompanyId;
            if (policy.RequiresResolvedTenant)
            {
                if (!targetCompanyId.HasValue)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.TenantNotResolved,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status404NotFound,
                        "Workspace not found",
                        "The requested workspace does not exist or you do not have access to it.");
                }

                if (_db != null)
                {
                    var isCompanyDeleted = await _db.Companies.AsNoTracking()
                        .AnyAsync(c => c.Id == targetCompanyId.Value && c.DeletedAt != null, cancellationToken);
                    if (isCompanyDeleted)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.TenantNotResolved,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status404NotFound,
                            "Workspace not found",
                            "The requested workspace does not exist or you do not have access to it.");
                    }
                }

                if (policy.Domain is AccessPolicyDomain.WorkspaceFunctional or AccessPolicyDomain.WorkspaceGovernance or AccessPolicyDomain.WorkspaceComposite)
                {
                    if (actor.MembershipStatus != CompanyMemberStatus.Active || !actor.WorkspaceRole.HasValue)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.MembershipInactiveOrMissing,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status404NotFound,
                            "Workspace not found",
                            "The requested workspace does not exist or you do not have access to it.");
                    }
                }
            }

            // 4. Foreign resource check (when resource company ID is supplied)
            if (resource?.ResourceCompanyId.HasValue == true)
            {
                if (!targetCompanyId.HasValue || resource.ResourceCompanyId.Value != targetCompanyId.Value)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.ForeignResource,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status404NotFound,
                        "Not Found",
                        "The requested resource was not found in the active workspace.");
                }
            }

            // 5. Governance & functional permission checks
            // Special case for WorkspaceBrandingLogoView: allowed for Owner/Admin governance OR sales.invoices.view functional permission.
            var isGovernanceOrFunctionalPolicy =
                policy.Domain == AccessPolicyDomain.WorkspaceFunctional &&
                policy.AllowedGovernanceRoles.Count > 0 &&
                policy.RequiredPermissionsAny.Count > 0;

            if (isGovernanceOrFunctionalPolicy)
            {
                var hasGov = actor.WorkspaceRole.HasValue && policy.AllowedGovernanceRoles.Contains(actor.WorkspaceRole.Value);
                var hasFunc = policy.RequiredPermissionsAny.Any(p => actor.FunctionalPermissions.Contains(p));
                if (!hasGov && !hasFunc)
                {
                    var missing = policy.RequiredPermissionsAny[0];
                    return AccessDecision.Deny(
                        AccessReasonCode.PermissionDenied,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        $"Permission '{missing}' is required for this action.",
                        missingPermission: missing,
                        requiredPermissions: policy.RequiredPermissionsAny);
                }
            }
            else
            {
                // 5a. Governance role check (for WorkspaceGovernance and WorkspaceComposite)
                if (policy.AllowedGovernanceRoles.Count > 0)
                {
                    if (!actor.WorkspaceRole.HasValue || !policy.AllowedGovernanceRoles.Contains(actor.WorkspaceRole.Value))
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.GovernanceRoleRequired,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            "Your workspace role does not allow this action.");
                    }
                }

                // 5b. Functional permissions check (All) — never satisfied by Owner/Admin governance role!
                if (policy.RequiredPermissionsAll.Count > 0)
                {
                    foreach (var requiredPerm in policy.RequiredPermissionsAll)
                    {
                        if (!actor.FunctionalPermissions.Contains(requiredPerm))
                        {
                            return AccessDecision.Deny(
                                AccessReasonCode.PermissionDenied,
                                policy.OperationKey,
                                policy.PolicyId,
                                policy.PolicyVersion,
                                StatusCodes.Status403Forbidden,
                                "Forbidden",
                                $"Permission '{requiredPerm}' is required for this action.",
                                missingPermission: requiredPerm,
                                requiredPermissions: policy.RequiredPermissionsAll);
                        }
                    }
                }

                // 5c. Functional permissions check (Any) — never satisfied by Owner/Admin governance role!
                if (policy.RequiredPermissionsAny.Count > 0)
                {
                    var anyMatch = policy.RequiredPermissionsAny.Any(p => actor.FunctionalPermissions.Contains(p));
                    if (!anyMatch)
                    {
                        var firstMissing = policy.RequiredPermissionsAny[0];
                        return AccessDecision.Deny(
                            AccessReasonCode.PermissionDenied,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Forbidden",
                            $"Permission '{firstMissing}' is required for this action.",
                            missingPermission: firstMissing,
                            requiredPermissions: policy.RequiredPermissionsAny);
                    }
                }
            }

            // 6. Mandatory resource ownership, workflow state, Separation-of-Duties, and Delegation checks
            if (policy.RequiresResourceOwnershipCheck && (resource == null || !resource.ResourceCompanyId.HasValue))
            {
                return AccessDecision.Deny(
                    AccessReasonCode.MissingContext,
                    policy.OperationKey,
                    policy.PolicyId,
                    policy.PolicyVersion,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Required resource ownership context is missing.");
            }

            if (policy.RequiresMutableWorkflowState)
            {
                if (resource == null || (!resource.IsPeriodLockedOrClosed.HasValue && string.IsNullOrWhiteSpace(resource.WorkflowState)))
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.MissingContext,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Required workflow state context is missing.");
                }
            }

            if (resource != null)
            {
                if (resource.IsPeriodLockedOrClosed == true)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.WorkflowStateInvalid,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Books are locked",
                        "This operation is not permitted because the accounting period or books are locked.");
                }

                if (policy.RequiresMutableWorkflowState &&
                    policy.DisallowedWorkflowStates.Count > 0 &&
                    string.IsNullOrWhiteSpace(resource.WorkflowState))
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.MissingContext,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Required resource workflow state is missing.");
                }

                if (!string.IsNullOrWhiteSpace(resource.WorkflowState) &&
                    policy.DisallowedWorkflowStates.Any(s => string.Equals(s, resource.WorkflowState, StringComparison.OrdinalIgnoreCase)))
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.WorkflowStateInvalid,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status400BadRequest,
                        "Invalid workflow state",
                        $"Operation '{policy.OperationKey}' cannot be executed when the resource is in state '{resource.WorkflowState}'.");
                }
            }

            if (policy.RequiresSeparationOfDuties)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.PreparedByUserId))
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.MissingContext,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Required preparer context for separation-of-duties verification is missing.");
                }

                var singleActorAllowed = false;
                if (string.Equals(policy.OperationKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal)
                    && targetCompanyId.HasValue
                    && _approvalPolicyResolver != null)
                {
                    var effectiveRule = _approvalPolicyResolver.Resolve(targetCompanyId.Value, policy.OperationKey);
                    // Unknown/missing policy preserves catalog SoD. Only an explicit SingleActorAllowed rule relaxes it.
                    singleActorAllowed = effectiveRule.Mode == WorkspaceApprovalMode.SingleActorAllowed;
                }

                if (!singleActorAllowed
                    && !string.IsNullOrWhiteSpace(actor.UserId) &&
                    string.Equals(resource.PreparedByUserId, actor.UserId, StringComparison.Ordinal))
                {
                    var hasVerifiedApproval = await HasValidIndependentApproverAsync(
                        targetCompanyId,
                        actor.UserId,
                        actor.UserId,
                        resource.IndependentApproverUserId,
                        resource.ApprovalRequestId,
                        policy.OperationKey,
                        resource,
                        cancellationToken);

                    if (!hasVerifiedApproval)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.SeparationOfDutiesViolation,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Separation of duties violation",
                            "A user who prepared a transaction cannot independently approve or post it without a verified independent approval.");
                    }
                }
            }

            if (policy.RequiresDelegationCheck)
            {
                if (resource == null)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.MissingContext,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Delegation resource context is required for role or invitation assignment operations.");
                }

                var delegationDecision = await EvaluateDelegationRulesAsync(policy, resource, actor, targetCompanyId, cancellationToken);
                if (!delegationDecision.Allowed)
                {
                    return delegationDecision;
                }
            }

            // 7. Subscription / Module / Feature Entitlement checks (fail closed if policy dependency is absent)
            EntitlementDecision? lastEntitlementDecision = null;
            if (policy.RequiredModulesAny.Count > 0 || !string.IsNullOrWhiteSpace(policy.RequiredFeature))
            {
                if (_entitlements == null || !targetCompanyId.HasValue)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.PolicyDependencyFailure,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status503ServiceUnavailable,
                        "Authorization Unavailable",
                        "Required entitlement policy dependency is unavailable.");
                }

                if (policy.RequiredModulesAny.Count > 0)
                {
                    var moduleDecision = _entitlements.Evaluate(
                        targetCompanyId.Value,
                        new EntitlementAction.Module(policy.RequiredModulesAny, policy.ModuleAccess));
                    lastEntitlementDecision = moduleDecision;

                    if (!moduleDecision.Allowed)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.EntitlementDenied,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Module not enabled",
                            moduleDecision.Detail ?? "The required module is not enabled for this workspace.",
                            requiredModules: policy.RequiredModulesAny,
                            entitlement: moduleDecision);
                    }
                }

                if (!string.IsNullOrWhiteSpace(policy.RequiredFeature))
                {
                    var featureDecision = _entitlements.Evaluate(
                        targetCompanyId.Value,
                        new EntitlementAction.Feature(policy.RequiredFeature));
                    lastEntitlementDecision = featureDecision;
                    if (!featureDecision.Allowed)
                    {
                        return AccessDecision.Deny(
                            AccessReasonCode.EntitlementDenied,
                            policy.OperationKey,
                            policy.PolicyId,
                            policy.PolicyVersion,
                            StatusCodes.Status403Forbidden,
                            "Feature not available",
                            featureDecision.Detail ?? $"Feature '{policy.RequiredFeature}' is not available on the current workspace plan.",
                            requiredFeature: policy.RequiredFeature,
                            entitlement: featureDecision);
                    }
                }
            }

            // 8. MFA Step-Up check
            if (policy.RequiresMfaStepUp)
            {
                if (!actor.IsMfaStepUpVerified)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.StepUpRequired,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "MFA step-up required",
                        "This action requires a recent multi-factor authentication check.");
                }
            }

            return AccessDecision.Allow(policy.OperationKey, policy.PolicyId, policy.PolicyVersion, lastEntitlementDecision);
        }

        private async Task<AccessDecision> EvaluateDelegationRulesAsync(
            AccessOperationPolicy policy,
            AccessResourceContext resource,
            AccessActorContext actor,
            int? targetCompanyId,
            CancellationToken cancellationToken)
        {
            // A. Self-approval check: an actor or target can never serve as their own independent approver.
            if (!string.IsNullOrWhiteSpace(resource.IndependentApproverUserId))
            {
                if ((!string.IsNullOrWhiteSpace(actor.UserId) &&
                     string.Equals(resource.IndependentApproverUserId, actor.UserId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(resource.TargetUserId) &&
                     string.Equals(resource.IndependentApproverUserId, resource.TargetUserId, StringComparison.Ordinal)))
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.DelegationSelfGrantDenied,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Self-approval denied",
                        "An actor cannot approve their own functional role elevation.");
                }
            }

            // B. Governance hierarchy limits (Admin cannot appoint/modify Owner or Admin; Owner cannot be invited).
            if (resource.RequestedWorkspaceRole == CompanyMemberRole.Owner &&
                resource.TargetCurrentWorkspaceRole != CompanyMemberRole.Owner)
            {
                return AccessDecision.Deny(
                    AccessReasonCode.DelegationScopeExceeded,
                    policy.OperationKey,
                    policy.PolicyId,
                    policy.PolicyVersion,
                    StatusCodes.Status400BadRequest,
                    "Invalid role",
                    "Owner role cannot be assigned through member updates or invitations; use ownership transfer.");
            }

            if (actor.WorkspaceRole == CompanyMemberRole.Admin)
            {
                if (resource.RequestedWorkspaceRole == CompanyMemberRole.Admin ||
                    resource.TargetCurrentWorkspaceRole is CompanyMemberRole.Owner or CompanyMemberRole.Admin)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.DelegationScopeExceeded,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Only a workspace Owner can grant or modify Admin or Owner memberships.");
                }
            }

            // C. Self-grant / self-elevation prohibition on member functional role assignment or invitation acceptance.
            var requestedRoleIds = resource.RequestedSecurityRoleIds ?? Array.Empty<int>();
            if (requestedRoleIds.Count > 0 &&
                !resource.IsWorkspaceInitialBootstrap &&
                !string.IsNullOrWhiteSpace(actor.UserId) &&
                !string.IsNullOrWhiteSpace(resource.TargetUserId) &&
                string.Equals(actor.UserId, resource.TargetUserId, StringComparison.Ordinal))
            {
                var hasValidIndependentApprover = await HasValidIndependentApproverAsync(
                    targetCompanyId,
                    actor.UserId,
                    resource.TargetUserId,
                    resource.IndependentApproverUserId,
                    resource.ApprovalRequestId,
                    policy.OperationKey,
                    resource,
                    cancellationToken);

                if (!hasValidIndependentApprover)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.DelegationSelfGrantDenied,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status403Forbidden,
                        "Self-grant denied",
                        "You cannot assign or elevate functional roles on your own membership without independent approval.");
                }
            }

            // D. Resolve permissions of requested functional roles and enforce delegation ceiling for both Owner and Admin.
            IReadOnlySet<string> requestedPermissions = resource.RequestedRolePermissions ?? EmptyStringSet;
            if (requestedPermissions.Count == 0 && requestedRoleIds.Count > 0 && _db != null)
            {
                var platformRoleSelected = await _db.SecurityRoles.AsNoTracking()
                    .AnyAsync(r => requestedRoleIds.Contains(r.Id) && r.Name == "SystemAdministrators", cancellationToken);
                if (platformRoleSelected)
                {
                    return AccessDecision.Deny(
                        AccessReasonCode.DelegationScopeExceeded,
                        policy.OperationKey,
                        policy.PolicyId,
                        policy.PolicyVersion,
                        StatusCodes.Status400BadRequest,
                        "Invalid security role",
                        "Platform administrator roles cannot be assigned to workspace memberships.");
                }

                var perms = await _db.Set<SecurityRolePermission>()
                    .AsNoTracking()
                    .Where(srp => requestedRoleIds.Contains(srp.SecurityRoleId) && srp.SecurityPermission != null)
                    .Select(srp => srp.SecurityPermission!.Name)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                requestedPermissions = new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);
            }

            if (requestedPermissions.Count > 0 && !resource.IsWorkspaceInitialBootstrap)
            {
                var sensitiveRequested = requestedPermissions
                    .Where(p => SensitiveDelegatedPermissions.Contains(p))
                    .ToList();

                // Both Owner and Admin are subject to the functional delegation ceiling: neither can unilaterally
                // grant sensitive financial or administrative mutation permissions they do not already possess
                // without a server-recorded independent approval artifact.
                if (sensitiveRequested.Count > 0 &&
                    (actor.WorkspaceRole == CompanyMemberRole.Owner || actor.WorkspaceRole == CompanyMemberRole.Admin))
                {
                    var grantorHoldsAllSensitive = sensitiveRequested.All(p => actor.FunctionalPermissions.Contains(p)) ||
                                                   actor.FunctionalPermissions.Contains(SystemPermissions.AdminRolesManage) ||
                                                   actor.FunctionalPermissions.Contains(SystemPermissions.AdminMembersManage);

                    if (!grantorHoldsAllSensitive)
                    {
                        var hasIndependentApprover = await HasValidIndependentApproverAsync(
                            targetCompanyId,
                            actor.UserId,
                            resource.TargetUserId,
                            resource.IndependentApproverUserId,
                            resource.ApprovalRequestId,
                            policy.OperationKey,
                            resource,
                            cancellationToken);

                        if (!hasIndependentApprover)
                        {
                            return AccessDecision.Deny(
                                AccessReasonCode.DelegationScopeExceeded,
                                policy.OperationKey,
                                policy.PolicyId,
                                policy.PolicyVersion,
                                StatusCodes.Status403Forbidden,
                                "Delegation scope exceeded",
                                "You cannot unilaterally delegate financial or administrative mutation roles that exceed your own functional grants without independent approval.");
                        }
                    }
                }
            }

            return AccessDecision.Allow(policy.OperationKey, policy.PolicyId, policy.PolicyVersion);
        }

        private async Task<bool> HasValidIndependentApproverAsync(
            int? companyId,
            string? actorUserId,
            string? targetUserId,
            string? approverUserId,
            string? approvalRequestId,
            string operationKey,
            AccessResourceContext? resource,
            CancellationToken cancellationToken)
        {
            // S7: Without a database connection, approval verification cannot be proved. Fail closed.
            if (_db == null || !companyId.HasValue)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(approverUserId) && string.IsNullOrWhiteSpace(approvalRequestId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(approverUserId))
            {
                if ((!string.IsNullOrWhiteSpace(actorUserId) && string.Equals(approverUserId, actorUserId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(targetUserId) && string.Equals(approverUserId, targetUserId, StringComparison.Ordinal)))
                {
                    return false;
                }
            }

            // Require a server-persisted approval record created by an authenticated independent approver.
            // Caller-supplied approvedByUserId strings without a matching persisted approval record are rejected.
            var candidates = await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId.Value &&
                            a.TableName == ApprovalAuditTableName &&
                            a.OriginalValue == "Approved")
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return false;
            }

            var requestedRolesSorted = (resource?.RequestedSecurityRoleIds ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            var now = DateTimeOffset.UtcNow;

            foreach (var row in candidates)
            {
                if (string.IsNullOrWhiteSpace(row.NewValue))
                    continue;

                PersistedAccessApprovalPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(row.NewValue);
                }
                catch
                {
                    continue;
                }

                if (payload == null ||
                    payload.CompanyId != companyId.Value ||
                    !string.Equals(payload.Status, "Approved", StringComparison.Ordinal) ||
                    payload.ExpiresAtUtc <= now ||
                    !string.Equals(payload.PolicyVersion, CatalogVersion, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(approvalRequestId) &&
                    !string.Equals(payload.RequestId, approvalRequestId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(approverUserId) &&
                    !string.Equals(payload.ApproverUserId, approverUserId, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((!string.IsNullOrWhiteSpace(actorUserId) && string.Equals(payload.ApproverUserId, actorUserId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(targetUserId) && string.Equals(payload.ApproverUserId, targetUserId, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (string.Equals(operationKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal))
                {
                    if (!string.Equals(payload.OperationKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal))
                        continue;

                    if (!string.IsNullOrWhiteSpace(resource?.ResourceId) &&
                        !string.Equals(payload.ResourceId, resource.ResourceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(resource?.ResourceFingerprint))
                    {
                        if (string.IsNullOrWhiteSpace(payload.ResourceFingerprint) ||
                            !string.Equals(payload.ResourceFingerprint, resource.ResourceFingerprint, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        var je = int.TryParse(resource?.ResourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jId)
                            ? await _db.JournalEntryHeaders.AsNoTracking()
                                .Include(j => j.JournalEntryLines)
                                .FirstOrDefaultAsync(j => j.CompanyId == companyId.Value && j.Id == jId, cancellationToken)
                            : null;

                        if (je != null)
                        {
                            if (string.IsNullOrWhiteSpace(payload.ResourceFingerprint))
                                continue;

                            var currentFingerprint = ComputeJournalFingerprint(
                                je.Date,
                                je.Memo,
                                (int)(je.VoucherType ?? 0),
                                je.JournalEntryLines.Select(l => (l.AccountId, l.DrCr, l.Amount, l.Memo)));
                            if (!string.Equals(payload.ResourceFingerprint, currentFingerprint, StringComparison.Ordinal))
                            {
                                continue;
                            }
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(operationKey) &&
                        !string.Equals(payload.OperationKey, operationKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var targetMatched =
                        (resource?.TargetMemberId.HasValue == true && payload.TargetMemberId == resource.TargetMemberId) ||
                        (!string.IsNullOrWhiteSpace(resource?.TargetUserId) && string.Equals(payload.TargetUserId, resource.TargetUserId, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(resource?.TargetEmail) && string.Equals(payload.TargetEmail, resource.TargetEmail, StringComparison.OrdinalIgnoreCase));

                    if (!targetMatched)
                        continue;

                    var approvedRolesSorted = (payload.SecurityRoleIds ?? Array.Empty<int>())
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();

                    if (!approvedRolesSorted.SequenceEqual(requestedRolesSorted))
                        continue;
                }

                var isApproverStillActiveGovernance = await _db.CompanyMembers.AsNoTracking()
                    .AnyAsync(m =>
                        m.CompanyId == companyId.Value &&
                        m.UserId == payload.ApproverUserId &&
                        m.Status == CompanyMemberStatus.Active &&
                        (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin),
                        cancellationToken);

                if (isApproverStillActiveGovernance)
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task<(string RequestId, DateTimeOffset ExpiresAtUtc)> RecordAuthenticatedApprovalAsync(
            ApiDbContext db,
            int companyId,
            string authenticatedApproverUserId,
            string operationKey,
            int? targetMemberId = null,
            string? targetUserId = null,
            string? targetEmail = null,
            IReadOnlyList<int>? securityRoleIds = null,
            string? resourceId = null,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(db);
            if (string.IsNullOrWhiteSpace(authenticatedApproverUserId))
                throw new InvalidOperationException("Authenticated approver user ID is required.");

            var approverMember = await db.CompanyMembers.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.CompanyId == companyId &&
                    m.UserId == authenticatedApproverUserId &&
                    m.Status == CompanyMemberStatus.Active &&
                    (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin),
                    cancellationToken);

            if (approverMember == null)
                throw new InvalidOperationException("Approver must be an active Owner or Admin in the workspace.");

            if (!string.IsNullOrWhiteSpace(targetUserId) &&
                string.Equals(authenticatedApproverUserId, targetUserId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An approver cannot approve functional role elevation for themselves.");
            }

            if (targetMemberId.HasValue)
            {
                var targetMember = await db.CompanyMembers.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == targetMemberId.Value && m.CompanyId == companyId, cancellationToken);
                if (targetMember == null)
                    throw new InvalidOperationException("Target member not found in workspace.");
                if (string.Equals(targetMember.UserId, authenticatedApproverUserId, StringComparison.Ordinal))
                    throw new InvalidOperationException("An approver cannot approve functional role elevation for themselves.");
                targetUserId ??= targetMember.UserId;
            }

            var requestId = "apr_" + Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.Add(ttl ?? TimeSpan.FromMinutes(30));
            var sortedRoles = (securityRoleIds ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            string? resourceFingerprint = null;
            if (string.Equals(operationKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal) &&
                int.TryParse(resourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jId))
            {
                var je = await db.JournalEntryHeaders.AsNoTracking()
                    .Include(j => j.JournalEntryLines)
                    .FirstOrDefaultAsync(j => j.CompanyId == companyId && j.Id == jId, cancellationToken);
                if (je != null)
                {
                    resourceFingerprint = ComputeJournalFingerprint(
                        je.Date,
                        je.Memo,
                        (int)(je.VoucherType ?? 0),
                        je.JournalEntryLines.Select(l => (l.AccountId, l.DrCr, l.Amount, l.Memo)));
                }
            }

            var payload = new PersistedAccessApprovalPayload
            {
                RequestId = requestId,
                CompanyId = companyId,
                ApproverUserId = authenticatedApproverUserId,
                OperationKey = operationKey,
                TargetMemberId = targetMemberId,
                TargetUserId = targetUserId,
                TargetEmail = targetEmail?.Trim().ToLowerInvariant(),
                ResourceId = resourceId,
                ResourceFingerprint = resourceFingerprint,
                SecurityRoleIds = sortedRoles,
                PolicyVersion = AccessOperationCatalog.CurrentVersion,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                Status = "Approved"
            };

            db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
            {
                CompanyId = companyId,
                UserName = authenticatedApproverUserId,
                AuditEventDateUTC = now.UtcDateTime,
                AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                TableName = ApprovalAuditTableName,
                RecordId = requestId,
                FieldName = "Status",
                OriginalValue = "Approved",
                NewValue = JsonSerializer.Serialize(payload)
            });

            await db.SaveChangesAsync(cancellationToken);
            BumpGlobalSecurityRevision();
            return (requestId, expiresAt);
        }

        public static async Task<bool> RevokeAuthenticatedApprovalAsync(
            ApiDbContext db,
            int companyId,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(db);
            if (string.IsNullOrWhiteSpace(requestId))
                return false;

            var row = await db.AuditLogs
                .FirstOrDefaultAsync(a =>
                    a.CompanyId == companyId &&
                    a.TableName == ApprovalAuditTableName &&
                    a.RecordId == requestId &&
                    a.OriginalValue == "Approved",
                    cancellationToken);

            if (row == null)
                return false;

            row.OriginalValue = "Revoked";
            if (!string.IsNullOrWhiteSpace(row.NewValue))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(row.NewValue);
                    if (payload != null)
                    {
                        payload.Status = "Revoked";
                        row.NewValue = JsonSerializer.Serialize(payload);
                    }
                }
                catch { }
            }

            await db.SaveChangesAsync(cancellationToken);
            BumpGlobalSecurityRevision();
            return true;
        }

        public static async Task InvalidateApprovalsForResourceAsync(
            ApiDbContext db,
            int companyId,
            string operationKey,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            var rows = await db.AuditLogs
                .Where(a => a.CompanyId == companyId &&
                            a.TableName == ApprovalAuditTableName &&
                            a.OriginalValue == "Approved")
                .ToListAsync(cancellationToken);

            var modified = false;
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.NewValue)) continue;
                try
                {
                    var p = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(row.NewValue);
                    if (p != null &&
                        string.Equals(p.OperationKey, operationKey, StringComparison.Ordinal) &&
                        string.Equals(p.ResourceId, resourceId, StringComparison.Ordinal))
                    {
                        row.OriginalValue = "Revoked";
                        p.Status = "Revoked";
                        row.NewValue = JsonSerializer.Serialize(p);
                        modified = true;
                    }
                }
                catch { }
            }

            if (modified)
            {
                await db.SaveChangesAsync(cancellationToken);
                BumpGlobalSecurityRevision();
            }
        }

        public static async Task<bool> ConsumeMatchingApprovalAsync(
            ApiDbContext db,
            int companyId,
            string? actorUserId,
            int? targetMemberId,
            string? targetUserId,
            string? targetEmail,
            IReadOnlyList<int>? requestedRoleIds,
            string? approvalRequestId = null,
            string? approverUserId = null,
            string? operationKey = null,
            CancellationToken cancellationToken = default)
        {
            var rows = await db.AuditLogs
                .Where(a => a.CompanyId == companyId &&
                            a.TableName == ApprovalAuditTableName &&
                            a.OriginalValue == "Approved")
                .OrderByDescending(a => a.Id)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                return false;

            var requestedSorted = (requestedRoleIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToArray();
            var now = DateTimeOffset.UtcNow;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.NewValue))
                    continue;

                PersistedAccessApprovalPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(row.NewValue);
                }
                catch
                {
                    continue;
                }

                if (payload == null || payload.Status != "Approved" || payload.ExpiresAtUtc <= now)
                    continue;

                if (!string.IsNullOrWhiteSpace(operationKey) &&
                    !string.Equals(payload.OperationKey, operationKey, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrWhiteSpace(approvalRequestId) &&
                    !string.Equals(payload.RequestId, approvalRequestId, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrWhiteSpace(approverUserId) &&
                    !string.Equals(payload.ApproverUserId, approverUserId, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrWhiteSpace(actorUserId) &&
                    string.Equals(payload.ApproverUserId, actorUserId, StringComparison.Ordinal))
                    continue;

                if (requestedSorted.Length > 0)
                {
                    var targetMatched =
                        (targetMemberId.HasValue && payload.TargetMemberId == targetMemberId) ||
                        (!string.IsNullOrWhiteSpace(targetUserId) && string.Equals(payload.TargetUserId, targetUserId, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(targetEmail) && string.Equals(payload.TargetEmail, targetEmail, StringComparison.OrdinalIgnoreCase));

                    if (!targetMatched)
                        continue;

                    var approvedSorted = (payload.SecurityRoleIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToArray();
                    if (!approvedSorted.SequenceEqual(requestedSorted))
                        continue;
                }

                row.OriginalValue = "Consumed";
                payload.Status = "Consumed";
                row.NewValue = JsonSerializer.Serialize(payload);
                await db.SaveChangesAsync(cancellationToken);
                BumpGlobalSecurityRevision();
                return true;
            }

            return false;
        }
    }
}
