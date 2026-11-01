#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain;
using Core.Entitlements;

namespace Core.Authorization
{
    /// <summary>
    /// Identifies the verified trust domain of the caller.
    /// Workspace, platform, staff, transport-gate, and service principals occupy separate trust domains.
    /// </summary>
    public enum ActorTrustDomain
    {
        Unauthenticated = 0,
        PendingAuthentication = 1,
        WorkspaceUser = 2,
        PlatformAdministrator = 3,
        StaffOperator = 4,
        TransportGate = 5,
        ServicePrincipal = 6,
        ScopedService = 7
    }

    /// <summary>
    /// Identifies the policy authority domain of an operation in the versioned catalog.
    /// </summary>
    public enum AccessPolicyDomain
    {
        Public = 0,
        SelfService = 1,
        WorkspaceFunctional = 2,
        WorkspaceGovernance = 3,
        WorkspaceComposite = 4,
        PlatformAdmin = 5,
        StaffConsole = 6,
        ServicePrincipal = 7
    }

    /// <summary>
    /// Stable machine reason codes returned by authorization evaluations.
    /// </summary>
    public enum AccessReasonCode
    {
        Allowed = 0,
        Unauthenticated = 1,
        PendingAuthenticationToken = 2,
        ActorDomainMismatch = 3,
        TenantNotResolved = 4,
        MembershipInactiveOrMissing = 5,
        ForeignResource = 6,
        UnknownOperation = 7,
        MissingContext = 8,
        PolicyDependencyFailure = 9,
        GovernanceRoleRequired = 10,
        PermissionDenied = 11,
        EntitlementDenied = 12,
        StepUpRequired = 13,
        WorkflowStateInvalid = 14,
        SeparationOfDutiesViolation = 15,
        DelegationSelfGrantDenied = 16,
        DelegationScopeExceeded = 17,
        IndependentApprovalRequired = 18
    }

    public static class AccessReasonCodes
    {
        public static string ToWireCode(this AccessReasonCode code) => code switch
        {
            AccessReasonCode.Allowed => "ALLOWED",
            AccessReasonCode.Unauthenticated => "UNAUTHENTICATED",
            AccessReasonCode.PendingAuthenticationToken => "PENDING_AUTH_TOKEN",
            AccessReasonCode.ActorDomainMismatch => "ACTOR_DOMAIN_MISMATCH",
            AccessReasonCode.TenantNotResolved => "TENANT_NOT_RESOLVED",
            AccessReasonCode.MembershipInactiveOrMissing => "MEMBERSHIP_INACTIVE_OR_MISSING",
            AccessReasonCode.ForeignResource => "FOREIGN_RESOURCE",
            AccessReasonCode.UnknownOperation => "UNKNOWN_OPERATION",
            AccessReasonCode.MissingContext => "MISSING_CONTEXT",
            AccessReasonCode.PolicyDependencyFailure => "POLICY_DEPENDENCY_FAILURE",
            AccessReasonCode.GovernanceRoleRequired => "GOVERNANCE_ROLE_REQUIRED",
            AccessReasonCode.PermissionDenied => "PERMISSION_DENIED",
            AccessReasonCode.EntitlementDenied => "ENTITLEMENT_DENIED",
            AccessReasonCode.StepUpRequired => "STEP_UP_REQUIRED",
            AccessReasonCode.WorkflowStateInvalid => "WORKFLOW_STATE_INVALID",
            AccessReasonCode.SeparationOfDutiesViolation => "SEPARATION_OF_DUTIES_VIOLATION",
            AccessReasonCode.DelegationSelfGrantDenied => "DELEGATION_SELF_GRANT_DENIED",
            AccessReasonCode.DelegationScopeExceeded => "DELEGATION_SCOPE_EXCEEDED",
            AccessReasonCode.IndependentApprovalRequired => "INDEPENDENT_APPROVAL_REQUIRED",
            _ => "ACCESS_DENIED"
        };
    }

    /// <summary>
    /// Verified server-resolved actor and workspace membership context.
    /// Never trust caller-supplied role headers or unverified claims to construct this context.
    /// </summary>
    public sealed record AccessActorContext
    {
        public ActorTrustDomain TrustDomain { get; init; } = ActorTrustDomain.Unauthenticated;
        public string? UserId { get; init; }
        public string? ServicePrincipalId { get; init; }
        public int? CompanyId { get; init; }
        public string? CompanySlug { get; init; }
        public int? MemberId { get; init; }
        public CompanyMemberRole? WorkspaceRole { get; init; }
        public CompanyMemberStatus? MembershipStatus { get; init; }
        public IReadOnlySet<string> FunctionalPermissions { get; init; } = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<string> AssignedFunctionalRoles { get; init; } = Array.Empty<string>();
        public bool IsPlatformAdministrator { get; init; }
        public bool IsMfaStepUpVerified { get; init; }
        public DateTimeOffset? MfaVerifiedAt { get; init; }
        public long MembershipRevision { get; init; }

        public static AccessActorContext Unauthenticated() => new()
        {
            TrustDomain = ActorTrustDomain.Unauthenticated
        };

        public static AccessActorContext ForServicePrincipal(string servicePrincipalId, int? companyId = null, string? companySlug = null) => new()
        {
            TrustDomain = ActorTrustDomain.ServicePrincipal,
            ServicePrincipalId = servicePrincipalId,
            CompanyId = companyId,
            CompanySlug = companySlug
        };
    }

    /// <summary>
    /// Trusted server-resolved resource, workflow, and delegation context for an authorization check.
    /// </summary>
    public sealed record AccessResourceContext
    {
        public int? WorkspaceCompanyId { get; init; }
        public int? ResourceCompanyId { get; init; }
        public string? ResourceType { get; init; }
        public string? ResourceId { get; init; }
        public string? WorkflowState { get; init; }
        public bool? IsPeriodLockedOrClosed { get; init; }
        public string? PreparedByUserId { get; init; }
        public string? TargetUserId { get; init; }
        public int? TargetMemberId { get; init; }
        public string? TargetEmail { get; init; }
        public CompanyMemberRole? TargetCurrentWorkspaceRole { get; init; }
        public CompanyMemberRole? RequestedWorkspaceRole { get; init; }
        public IReadOnlyList<int>? RequestedSecurityRoleIds { get; init; }
        public IReadOnlySet<string>? RequestedRolePermissions { get; init; }
        public string? IndependentApproverUserId { get; init; }
        public string? ApprovalRequestId { get; init; }
        public string? ResourceFingerprint { get; init; }
        public bool IsWorkspaceInitialBootstrap { get; init; }
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }

        public static AccessResourceContext ForWorkspaceResource(
            int workspaceCompanyId,
            int resourceCompanyId,
            string? resourceType = null,
            string? resourceId = null) => new()
        {
            WorkspaceCompanyId = workspaceCompanyId,
            ResourceCompanyId = resourceCompanyId,
            ResourceType = resourceType,
            ResourceId = resourceId
        };
    }

    /// <summary>
    /// Immutable operation policy entry in the versioned authorization catalog.
    /// </summary>
    public sealed record AccessOperationPolicy
    {
        public required string OperationKey { get; init; }
        public required string PolicyId { get; init; }
        public required string PolicyVersion { get; init; }
        public required AccessPolicyDomain Domain { get; init; }
        public IReadOnlyList<CompanyMemberRole> AllowedGovernanceRoles { get; init; } = Array.Empty<CompanyMemberRole>();
        public IReadOnlyList<string> RequiredPermissionsAll { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredPermissionsAny { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredModulesAny { get; init; } = Array.Empty<string>();
        public ModuleAccess ModuleAccess { get; init; } = ModuleAccess.Read;
        public string? RequiredFeature { get; init; }
        public string? AllowedServicePrincipalId { get; init; }
        public bool RequiresResolvedTenant { get; init; } = true;
        public bool RequiresResourceOwnershipCheck { get; init; }
        public bool RequiresMfaStepUp { get; init; }
        public bool RequiresMutableWorkflowState { get; init; }
        public bool RequiresSeparationOfDuties { get; init; }
        public bool RequiresDelegationCheck { get; init; }
        public IReadOnlyList<string> DisallowedWorkflowStates { get; init; } = Array.Empty<string>();
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>
    /// Normalized authorization decision returned by <see cref="IAccessAuthorizationService"/>.
    /// </summary>
    public sealed record AccessDecision
    {
        public bool Allowed { get; init; }
        public AccessReasonCode ReasonCode { get; init; }
        public string ReasonCodeString => ReasonCode.ToWireCode();
        public required string OperationKey { get; init; }
        public required string PolicyId { get; init; }
        public required string PolicyVersion { get; init; }
        public int HttpStatus { get; init; }
        public string SafeTitle { get; init; } = string.Empty;
        public string SafeMessage { get; init; } = string.Empty;
        public string? MissingPermission { get; init; }
        public IReadOnlyList<string> RequiredPermissions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RequiredModules { get; init; } = Array.Empty<string>();
        public string? RequiredFeature { get; init; }
        public EntitlementDecision? EntitlementDecision { get; init; }

        public static AccessDecision Allow(
            string operationKey,
            string policyId,
            string policyVersion,
            EntitlementDecision? entitlement = null) => new()
        {
            Allowed = true,
            ReasonCode = AccessReasonCode.Allowed,
            OperationKey = operationKey,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            HttpStatus = 200,
            SafeTitle = "Allowed",
            SafeMessage = "Access granted.",
            EntitlementDecision = entitlement
        };

        public static AccessDecision Deny(
            AccessReasonCode reason,
            string operationKey,
            string policyId,
            string policyVersion,
            int httpStatus,
            string safeTitle,
            string safeMessage,
            string? missingPermission = null,
            IReadOnlyList<string>? requiredPermissions = null,
            IReadOnlyList<string>? requiredModules = null,
            string? requiredFeature = null,
            EntitlementDecision? entitlement = null) => new()
        {
            Allowed = false,
            ReasonCode = reason,
            OperationKey = operationKey,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            HttpStatus = httpStatus,
            SafeTitle = safeTitle,
            SafeMessage = safeMessage,
            MissingPermission = missingPermission,
            RequiredPermissions = requiredPermissions ?? Array.Empty<string>(),
            RequiredModules = requiredModules ?? Array.Empty<string>(),
            RequiredFeature = requiredFeature,
            EntitlementDecision = entitlement
        };
    }

    /// <summary>
    /// Tracks migration state of each application entrypoint onto the shared authorization contract.
    /// </summary>
    public enum AccessMigrationStatus
    {
        Migrated = 0,
        PendingMigration = 1
    }

    /// <summary>
    /// Typed inventory entry recording a migrated or pending-migration authorization path.
    /// </summary>
    public sealed record AccessPathMigrationEntry(
        string PathId,
        string AdapterSurface,
        string EndpointOrHandler,
        string OperationKey,
        AccessMigrationStatus Status,
        string TrackingScope);

    /// <summary>
    /// Shared authorization decision contract across HTTP, MCP, application services, modules, and background jobs.
    /// </summary>
    public interface IAccessAuthorizationService
    {
        string CatalogVersion { get; }

        AccessOperationPolicy? GetPolicy(string operationKey);

        AccessActorContext ResolveActorContext(AccessActorContext? actorOverride = null);

        AccessDecision Authorize(
            string operationKey,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null);

        Task<AccessDecision> AuthorizeAsync(
            string operationKey,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null,
            CancellationToken cancellationToken = default);

        AccessDecision AuthorizePermission(
            string permission,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null);

        AccessDecision AuthorizeWorkspaceRole(
            CompanyMemberRole minimumRole,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null);

        AccessDecision AuthorizeRequirement(
            IReadOnlyList<string>? requiredPermissionsAll = null,
            IReadOnlyList<string>? requiredPermissionsAny = null,
            IReadOnlyList<CompanyMemberRole>? allowedGovernanceRoles = null,
            IReadOnlyList<string>? requiredModulesAny = null,
            string? requiredFeature = null,
            AccessResourceContext? resource = null,
            AccessActorContext? actorOverride = null,
            string? operationKey = null);

        IReadOnlySet<string> GetAuthorizedDashboardDomains(AccessActorContext? actorOverride = null);

        void InvalidateContextCache();
    }

    public sealed record PersistedAccessApprovalPayload
    {
        public string RequestId { get; init; } = string.Empty;
        public int CompanyId { get; init; }
        public string ApproverUserId { get; init; } = string.Empty;
        public string OperationKey { get; init; } = string.Empty;
        public int? TargetMemberId { get; init; }
        public string? TargetUserId { get; init; }
        public string? TargetEmail { get; init; }
        public string? ResourceId { get; init; }
        public string? ResourceFingerprint { get; init; }
        public int[] SecurityRoleIds { get; init; } = Array.Empty<int>();
        public string PolicyVersion { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public string Status { get; set; } = "Approved";
    }

    public static class AccessJournalFingerprint
    {
        public static string Compute(
            DateTime date,
            string? memo,
            int voucherType,
            IEnumerable<(int AccountId, DrOrCrSide DrCr, decimal Amount, string? Memo)> lines)
        {
            var lineParts = (lines ?? Enumerable.Empty<(int, DrOrCrSide, decimal, string?)>())
                .OrderBy(l => l.AccountId)
                .ThenBy(l => l.DrCr)
                .ThenBy(l => l.Amount)
                .Select(l => $"{l.AccountId}:{l.DrCr}:{l.Amount.ToString("F4", CultureInfo.InvariantCulture)}:{l.Memo}");

            var raw = $"{date:yyyy-MM-dd}:{memo}:{voucherType}:{string.Join("|", lineParts)}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash);
        }
    }
}
