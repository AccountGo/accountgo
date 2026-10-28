// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.Extensions.Logging;

namespace Services.Entitlements
{
    /// <summary>
    /// Configuration-driven entitlements. Quotas apply when Enabled and the effective plan is free
    /// (or Tier=comp overrides to unlimited). Module <see cref="Evaluate"/> runs pipeline steps 1–6.
    /// Feature / EvaluateForUser arrive in E2. Community never emits feature keys;
    /// subclasses may override <see cref="ResolveFeatures"/> for tests.
    /// </summary>
    public class EntitlementService : IEntitlementService
    {
        private static readonly MeteredResource[] AllResources = (MeteredResource[])Enum.GetValues(typeof(MeteredResource));
        private static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);
        private static readonly IReadOnlySet<string> NoFeatures = ImmutableHashSet<string>.Empty;

        private readonly EntitlementsOptions _options;
        private readonly IRepository<Company> _companies;
        private readonly IRepository<CompanyMember> _members;
        private readonly IRepository<CompanyInvitation> _invitations;
        private readonly IRepository<EntitlementUsage> _usage;
        private readonly IRepository<Bank> _banks;
        private readonly ITenantContext _tenant;
        private readonly TimeProvider _time;
        private readonly ILogger<EntitlementService>? _logger;
        // Assumption: Plan / modules / status / tier / billing flags are immutable for the
        // request after the first ResolvePolicy; writers must ClearPolicyMemo so a later Evaluate sees fresh state.
        private readonly Dictionary<int, PolicyMemo> _policyMemo = new();
        private int _bypassDepth;

        public EntitlementService(
            EntitlementsOptions options,
            IRepository<Company> companies,
            IRepository<CompanyMember> members,
            IRepository<CompanyInvitation> invitations,
            IRepository<EntitlementUsage> usage,
            IRepository<Bank> banks,
            ITenantContext tenant,
            TimeProvider? time = null,
            ILogger<EntitlementService>? logger = null)
        {
            _options = options;
            _companies = companies;
            _members = members;
            _invitations = invitations;
            _usage = usage;
            _banks = banks;
            _tenant = tenant;
            _time = time ?? TimeProvider.System;
            _logger = logger;
        }

        public EntitlementsOptions Options => _options;

        public EntitlementSnapshot Get(int companyId)
        {
            var policy = ResolvePolicy(companyId);
            var limited = policy.Limited;
            var usage = AllResources
                .Where(r => r != MeteredResource.OwnedWorkspaces)
                .Select(r =>
                {
                    var limit = limited ? LimitOf(r) : null;
                    var used = CountUsed(companyId, r, policy);
                    return new ResourceUsage(r, limit, used, MeteredResources.IsMonthly(r),
                        OverLimit: limit != null && used > limit.Value);
                })
                .ToList();

            var selfService = ResolveSelfServicePlans();
            var transitions = AllowedTransitionsFor(policy.StoredPlan);

            return new EntitlementSnapshot(
                _options.Enabled,
                policy.StoredPlan,
                policy.Tier,
                limited,
                policy.Modules.ToList(),
                selfService,
                usage,
                _options.SupportEmail,
                Features: policy.Features,
                EffectivePlan: policy.EffectivePlan,
                ReadOnlyModules: policy.ReadOnlyModules.ToList(),
                Lifecycle: policy.Lifecycle,
                AllowedTransitions: transitions,
                Version: policy.Version);
        }

        public EntitlementDecision Evaluate(int companyId, EntitlementAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return action switch
            {
                EntitlementAction.Module module => EvaluateModule(companyId, module.AnyOf, module.Access),
                EntitlementAction.Feature feature => EvaluateFeature(companyId, feature.Key),
                EntitlementAction.Create create => EvaluateCreate(companyId, create.Resource, create.Count),
                _ => throw new NotSupportedException($"Entitlement action '{action.GetType().Name}' is not supported.")
            };
        }

        public void Ensure(int companyId, EntitlementAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            switch (action)
            {
                case EntitlementAction.Create create:
                    EnsureCanCreate(companyId, create.Resource, create.Count);
                    return;
                default:
                    var decision = Evaluate(companyId, action);
                    if (!decision.Allowed)
                        throw ToException(decision);
                    return;
            }
        }

        public EntitlementDecision EvaluateForUser(string userId, UserAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("A user id is required.", nameof(userId));

            return action switch
            {
                UserAction.OwnAnotherWorkspace => EvaluateOwnAnotherWorkspace(userId),
                UserAction.FeatureAmongMemberships feature => EvaluateFeatureAmongMemberships(userId, feature.FeatureKey),
                _ => throw new NotSupportedException($"User action '{action.GetType().Name}' is not supported.")
            };
        }

        public void EnsureCanCreate(MeteredResource resource, int count = 1) =>
            EnsureCanCreate(_tenant.RequireCompanyId(), resource, count);

        public void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1)
        {
            var decision = EvaluateCreate(companyId, resource, count);
            if (!decision.Allowed)
                throw ToException(decision);
        }

        public void Record(MeteredResource resource, int? entityId) =>
            Record(_tenant.RequireCompanyId(), resource, entityId);

        public void Record(int companyId, MeteredResource resource, int? entityId)
        {
            if (!_options.Enabled || !MeteredResources.IsMonthly(resource))
                return;
            _usage.Insert(new EntitlementUsage
            {
                CompanyId = companyId,
                Resource = MeteredResources.Key(resource),
                EntityId = entityId,
                OccurredAt = DateTime.UtcNow
            });
        }

        public void EnsureCanOwnAnotherWorkspace(string userId)
        {
            var decision = EvaluateForUser(userId, new UserAction.OwnAnotherWorkspace());
            if (!decision.Allowed)
                throw ToException(decision);
        }

        public void EnsureSelfServicePlan(string plan)
        {
            if (!_options.IsSelfServicePlan(plan))
                throw new PlanNotAvailableException(plan) { UpgradeAvailable = false };
        }

        public IDisposable Bypass()
        {
            _bypassDepth++;
            return new Releaser(() => _bypassDepth--);
        }

        public void ClearPolicyMemo(int companyId) => _policyMemo.Remove(companyId);

        public bool TryGetResolvedPolicyVersion(int companyId, out string version)
        {
            if (_policyMemo.TryGetValue(companyId, out var memo))
            {
                version = memo.Version;
                return true;
            }
            version = "";
            return false;
        }

        /// <summary>
        /// Community emits no feature keys. Test subclasses may override to inject keys
        /// when verifying <see cref="EntitlementsOptions.FeaturesEnabled"/> kill-switch behaviour.
        /// </summary>
        protected virtual IReadOnlySet<string> ResolveFeatures(PolicyMemo policy)
        {
            _ = policy;
            return NoFeatures;
        }

        private EntitlementDecision EvaluateFeature(int companyId, string featureKey)
        {
            var key = (featureKey ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                return new EntitlementDecision(
                    Allowed: false,
                    Code: "FEATURE_NOT_ENTITLED",
                    Detail: "No feature key was supplied.",
                    RequiredFeature: featureKey,
                    UpgradeAvailable: false);
            }

            var policy = ResolvePolicy(companyId);
            if (policy.Features.Contains(key))
            {
                return new EntitlementDecision(
                    Allowed: true,
                    Code: null,
                    Detail: null,
                    RequiredFeature: key,
                    EffectivePlan: policy.EffectivePlan,
                    SubscriptionStatus: policy.Lifecycle.Status);
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "FEATURE_NOT_ENTITLED",
                Detail: $"The '{key}' feature is not included in this organization's plan.",
                RequiredFeature: key,
                EffectivePlan: policy.EffectivePlan,
                UpgradeAvailable: ComputeUpgradeAvailable("FEATURE_NOT_ENTITLED", policy, requiredFeature: key),
                SubscriptionStatus: policy.Lifecycle.Status);
        }

        private EntitlementDecision EvaluateCreate(int companyId, MeteredResource resource, int count)
        {
            if (!_options.Enabled || _bypassDepth > 0 || resource == MeteredResource.OwnedWorkspaces)
            {
                return new EntitlementDecision(true, null, null, Resource: resource, UpgradeAvailable: false);
            }

            var policy = ResolvePolicy(companyId);
            if (!policy.Limited)
            {
                return new EntitlementDecision(
                    true, null, null,
                    Resource: resource,
                    EffectivePlan: policy.EffectivePlan,
                    UpgradeAvailable: false);
            }

            var limit = LimitOf(resource);
            if (limit == null)
            {
                return new EntitlementDecision(
                    true, null, null,
                    Resource: resource,
                    EffectivePlan: policy.EffectivePlan,
                    UpgradeAvailable: false);
            }

            var used = CountUsed(companyId, resource, policy);
            if (used + count <= limit.Value)
            {
                return new EntitlementDecision(
                    true, null, null,
                    Resource: resource,
                    Limit: limit,
                    Used: used,
                    Period: MeteredResources.PeriodOf(resource),
                    EffectivePlan: policy.EffectivePlan,
                    UpgradeAvailable: false);
            }

            var planName = Company.PlanDisplayName(policy.EffectivePlan);
            return new EntitlementDecision(
                Allowed: false,
                Code: "QUOTA_EXCEEDED",
                Detail: DescribeQuota(planName, resource, limit.Value),
                Resource: resource,
                Limit: limit,
                Used: used,
                Period: MeteredResources.PeriodOf(resource),
                EffectivePlan: policy.EffectivePlan,
                UpgradeAvailable: ComputeUpgradeAvailable("QUOTA_EXCEEDED", policy, resource: resource),
                SubscriptionStatus: policy.Lifecycle.Status);
        }

        private EntitlementDecision EvaluateOwnAnotherWorkspace(string userId)
        {
            if (!_options.Enabled || _bypassDepth > 0)
                return new EntitlementDecision(true, null, null, UpgradeAvailable: false);

            if (!_options.FreeLimits.TryGetValue(MeteredResource.OwnedWorkspaces, out var limit))
                return new EntitlementDecision(true, null, null, UpgradeAvailable: false);

            var owned = OwnedWorkspaces(userId);
            if (owned.Any(c => !IsCompanyLimited(c)))
                return new EntitlementDecision(true, null, null, UpgradeAvailable: false);

            if (owned.Count + 1 <= limit)
            {
                return new EntitlementDecision(
                    true, null, null,
                    Resource: MeteredResource.OwnedWorkspaces,
                    Limit: limit,
                    Used: owned.Count,
                    UpgradeAvailable: false);
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "QUOTA_EXCEEDED",
                Detail: DescribeQuota(planDisplayName: null, MeteredResource.OwnedWorkspaces, limit),
                Resource: MeteredResource.OwnedWorkspaces,
                Limit: limit,
                Used: owned.Count,
                Period: MeteredResources.PeriodOf(MeteredResource.OwnedWorkspaces),
                UpgradeAvailable: _options.Enabled);
        }

        private EntitlementDecision EvaluateFeatureAmongMemberships(string userId, string featureKey)
        {
            var key = (featureKey ?? string.Empty).Trim();
            var companyIds = _members.Table
                .Where(m => m.UserId == userId && m.Status == CompanyMemberStatus.Active)
                .Select(m => m.CompanyId)
                .Distinct()
                .ToList();

            foreach (var companyId in companyIds)
            {
                var decision = EvaluateFeature(companyId, key);
                if (decision.Allowed)
                    return decision;
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "FEATURE_NOT_ENTITLED",
                Detail: $"The '{key}' feature is not available on any of this user's workspaces.",
                RequiredFeature: key,
                UpgradeAvailable: false);
        }

        private EntitlementDecision EvaluateModule(int companyId, IReadOnlyList<string> anyOf, ModuleAccess access)
        {
            var keys = (anyOf ?? Array.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToArray();
            if (keys.Length == 0)
            {
                return new EntitlementDecision(
                    Allowed: false,
                    Code: "MODULE_NOT_SUBSCRIBED",
                    Detail: "No module key was supplied.",
                    UpgradeAvailable: false);
            }

            var policy = ResolvePolicy(companyId);

            // Fully available (write or read).
            var fullKey = keys.FirstOrDefault(k => policy.IsModuleAvailable(k));
            if (fullKey != null)
            {
                return new EntitlementDecision(
                    Allowed: true,
                    Code: null,
                    Detail: null,
                    RequiredModule: fullKey,
                    EffectivePlan: policy.EffectivePlan,
                    SubscriptionStatus: policy.Lifecycle.Status);
            }

            // Read may use a read-only (lapsed paid) module.
            if (access == ModuleAccess.Read)
            {
                var readOnlyKey = keys.FirstOrDefault(k => policy.IsReadOnlyModule(k));
                if (readOnlyKey != null)
                {
                    return new EntitlementDecision(
                        Allowed: true,
                        Code: null,
                        Detail: null,
                        RequiredModule: readOnlyKey,
                        EffectivePlan: policy.EffectivePlan,
                        SubscriptionStatus: policy.Lifecycle.Status);
                }
            }

            // Write (or Read with no read-only hit): SUBSCRIPTION_INACTIVE when under stored plan.
            var lapsedKey = keys.FirstOrDefault(k =>
                policy.HasModuleUnderStoredPlan(k) && !policy.IsModuleAvailable(k));
            if (lapsedKey != null)
            {
                return new EntitlementDecision(
                    Allowed: false,
                    Code: "SUBSCRIPTION_INACTIVE",
                    Detail: "This workspace's subscription is inactive. Update billing to restore paid modules.",
                    RequiredModule: lapsedKey,
                    EffectivePlan: policy.EffectivePlan,
                    UpgradeAvailable: ComputeUpgradeAvailable("SUBSCRIPTION_INACTIVE", policy, requiredModule: lapsedKey),
                    SubscriptionStatus: policy.Lifecycle.Status);
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "MODULE_NOT_SUBSCRIBED",
                Detail: keys.Length == 1
                    ? $"The '{keys[0]}' module is not included in this organization's active plan."
                    : $"None of the '{string.Join("', '", keys)}' modules is included in this organization's active plan.",
                RequiredModule: keys[0],
                EffectivePlan: policy.EffectivePlan,
                UpgradeAvailable: ComputeUpgradeAvailable("MODULE_NOT_SUBSCRIBED", policy, requiredModule: keys[0]),
                SubscriptionStatus: policy.Lifecycle.Status);
        }

        /// <summary>
        /// upgradeAvailable: true for SUBSCRIPTION_INACTIVE when entitlements are on;
        /// otherwise true only when AllowedTransitions contains a plan that would lift the deny;
        /// always false when Enabled=false (self-host).
        /// </summary>
        private bool ComputeUpgradeAvailable(
            string code,
            PolicyMemo policy,
            string? requiredModule = null,
            string? requiredFeature = null,
            MeteredResource? resource = null)
        {
            if (!_options.Enabled)
                return false;

            if (string.Equals(code, "SUBSCRIPTION_INACTIVE", StringComparison.Ordinal))
                return true;

            foreach (var toPlan in AllowedTransitionsFor(policy.StoredPlan))
            {
                if (WouldLiftDeny(code, policy, toPlan, requiredModule, requiredFeature, resource))
                    return true;
            }

            return false;
        }

        private bool WouldLiftDeny(
            string code,
            PolicyMemo policy,
            string toPlan,
            string? requiredModule,
            string? requiredFeature,
            MeteredResource? resource)
        {
            if (string.Equals(code, "QUOTA_EXCEEDED", StringComparison.Ordinal))
            {
                // Paid / enterprise plans are unlimited until E4; free→paid lifts quotas.
                return Company.IsPaidPlan(toPlan);
            }

            if (string.Equals(code, "MODULE_NOT_SUBSCRIBED", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(requiredModule))
            {
                return Company.ModulesForPlan(toPlan)
                    .Contains(requiredModule, StringComparer.OrdinalIgnoreCase);
            }

            if (string.Equals(code, "FEATURE_NOT_ENTITLED", StringComparison.Ordinal))
            {
                // Community emits no features; no self-serve transition lifts a feature deny here.
                _ = requiredFeature;
                _ = policy;
                return false;
            }

            return false;
        }

        private static List<string> AllowedTransitionsFor(string storedPlan) =>
            Company.KnownPlans
                .Where(p => Company.IsAllowedSelfServicePlanChange(storedPlan, p))
                .ToList();

        private static string DescribeQuota(string? planDisplayName, MeteredResource resource, int limit) => resource switch
        {
            MeteredResource.SalesDocuments => $"The {planDisplayName} plan includes {limit} invoices and quotes per month.",
            MeteredResource.Bills => $"The {planDisplayName} plan includes {limit} bills per month.",
            MeteredResource.Seats => limit == 1
                ? $"The {planDisplayName} plan includes 1 user per workspace."
                : $"The {planDisplayName} plan includes {limit} users per workspace.",
            MeteredResource.BankAccounts => $"The {planDisplayName} plan includes {limit} bank and cash accounts.",
            // User-scoped: no single workspace plan name.
            MeteredResource.OwnedWorkspaces => limit == 1
                ? "Free plans include 1 workspace per owner."
                : $"Free plans include {limit} workspaces per owner.",
            _ => $"The {planDisplayName} plan has reached a limit."
        };

        private static EntitlementException ToException(EntitlementDecision decision)
        {
            if (decision.Code == "QUOTA_EXCEEDED" && decision.Resource is { } resource
                && decision.Limit is { } limit && decision.Used is { } used)
            {
                return new QuotaExceededException(resource, limit, used, decision.Detail ?? "Quota exceeded")
                {
                    EffectivePlan = decision.EffectivePlan,
                    UpgradeAvailable = decision.UpgradeAvailable,
                    SubscriptionStatus = decision.SubscriptionStatus
                };
            }

            if (decision.Code == "FEATURE_NOT_ENTITLED")
            {
                return new FeatureNotEntitledException(
                    decision.RequiredFeature ?? "",
                    decision.Detail ?? "Feature not entitled")
                {
                    EffectivePlan = decision.EffectivePlan,
                    UpgradeAvailable = decision.UpgradeAvailable,
                    SubscriptionStatus = decision.SubscriptionStatus
                };
            }

            return new ModuleNotSubscribedException(
                decision.Code ?? "MODULE_NOT_SUBSCRIBED",
                decision.Detail ?? "Not entitled",
                decision.RequiredModule)
            {
                EffectivePlan = decision.EffectivePlan,
                UpgradeAvailable = decision.UpgradeAvailable,
                SubscriptionStatus = decision.SubscriptionStatus
            };
        }

        private PolicyMemo ResolvePolicy(int companyId)
        {
            if (_policyMemo.TryGetValue(companyId, out var cached))
                return cached;

            var company = LoadCompany(companyId);
            var storedPlan = NormalizeStoredPlan(companyId, company.Plan);
            var now = _time.GetUtcNow().UtcDateTime;
            var (effectivePlan, graceEndsAt, reducedBy) = ResolveEffectivePlan(company, storedPlan, now);
            var effectiveCatalog = Company.ModulesForPlan(effectivePlan);
            // Step 5: GetActiveModules reads DisabledModulesJson (with ActiveModulesJson fallback until E5).
            var storedActive = company.GetActiveModules();
            FoldAddOnsNoOp();

            var modules = storedActive
                .Where(m => effectiveCatalog.Contains(m, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var readOnly = string.Equals(storedPlan, effectivePlan, StringComparison.Ordinal)
                ? new List<string>()
                : storedActive
                    .Where(m => !modules.Contains(m, StringComparer.OrdinalIgnoreCase))
                    .ToList();

            var status = string.IsNullOrWhiteSpace(company.SubscriptionStatus)
                ? "active"
                : company.SubscriptionStatus.Trim().ToLowerInvariant();
            var periodEnd = company.CurrentPeriodEnd ?? company.SubscriptionValidUntil;
            var tier = CompanyTiers.Derive(company.Tier, effectivePlan);
            // ENTITLEMENTS_ENABLED=false ⇒ quotas unlimited only; modules/lifecycle unchanged.
            var limited = _options.Enabled
                && !string.Equals(tier, CompanyTiers.Comp, StringComparison.Ordinal)
                && !IsPaidStoredPlan(effectivePlan);

            var draft = new PolicyMemo(
                company, storedPlan, effectivePlan, tier, limited, modules, readOnly,
                lifecycle: new EntitlementLifecycle(
                    Status: status,
                    GraceEndsAt: graceEndsAt,
                    CancelAtPeriodEnd: company.CancelAtPeriodEnd,
                    PeriodEnd: periodEnd,
                    ReducedBy: reducedBy),
                version: "",
                paidAccessEndedAt: company.PaidAccessEndedAt,
                features: NoFeatures);

            var features = _options.FeaturesEnabled ? ResolveFeatures(draft) : NoFeatures;
            draft = draft.WithFeatures(features);

            var limitsFingerprint = string.Join(',',
                AllResources.Select(r => $"{MeteredResources.Key(r)}={(limited ? LimitOf(r)?.ToString() ?? "-" : "-")}"));
            var version = HashPolicy(effectivePlan, modules, readOnly, features.ToList(), limitsFingerprint);
            draft = draft.WithVersion(version);

            _policyMemo[companyId] = draft;
            return draft;
        }

        private (string EffectivePlan, DateTime? GraceEndsAt, string? ReducedBy) ResolveEffectivePlan(
            Company company, string storedPlan, DateTime nowUtc)
        {
            if (string.Equals(CompanyTiers.Normalize(company.Tier), CompanyTiers.Comp, StringComparison.Ordinal)
                || company.BillingManagedManually)
                return (storedPlan, null, null);

            if (!IsPaidStoredPlan(storedPlan))
                return (storedPlan, null, null);

            // §6.2 row c (amended): null / empty / whitespace status normalizes to active.
            var status = string.IsNullOrWhiteSpace(company.SubscriptionStatus)
                ? "active"
                : company.SubscriptionStatus.Trim().ToLowerInvariant();

            if (status is "active" or "trialing")
                return (storedPlan, null, null);

            if (status == "past_due")
            {
                var periodEnd = company.CurrentPeriodEnd ?? company.SubscriptionValidUntil;
                if (periodEnd != null)
                {
                    var periodEndUtc = periodEnd.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(periodEnd.Value, DateTimeKind.Utc)
                        : periodEnd.Value.ToUniversalTime();
                    var graceEnds = periodEndUtc.Add(PastDueGrace);
                    if (nowUtc <= graceEnds)
                        return (storedPlan, graceEnds, null);
                }
                return (Company.FreePlanFor(storedPlan), null, "lifecycle");
            }

            if (status is "canceled" or "unpaid" or "incomplete_expired" or "incomplete")
                return (Company.FreePlanFor(storedPlan), null, "lifecycle");

            // h) paused or any non-empty unrecognized status
            EntitlementEngineShadow.LogErrorOnce(
                _logger,
                $"unrecognized-status|{company.Id}|{status}",
                "Unrecognized subscription status '{Status}' for company {CompanyId} — treating as free-plan entitlements",
                status, company.Id);
            return (Company.FreePlanFor(storedPlan), null, "lifecycle");
        }

        private static bool IsPaidStoredPlan(string normalizedStoredPlan) =>
            normalizedStoredPlan is "enterprise"
            || normalizedStoredPlan.EndsWith("_pro", StringComparison.Ordinal);

        private string NormalizeStoredPlan(int companyId, string? plan)
        {
            var key = Company.NormalizePlanKey(string.IsNullOrWhiteSpace(plan) ? "accounting_free" : plan);
            if (Company.IsKnownPlan(key))
                return key;

            EntitlementEngineShadow.LogErrorOnce(
                _logger,
                $"unknown-plan|{companyId}|{plan}",
                "Unknown company plan key '{Plan}' — treating as accounting_free for entitlements",
                plan);
            return "accounting_free";
        }

        private static void FoldAddOnsNoOp() { }

        private static string HashPolicy(
            string effectivePlan,
            IReadOnlyList<string> modules,
            IReadOnlyList<string> readOnly,
            IReadOnlyList<string> features,
            string limitsFingerprint)
        {
            var payload = string.Join('|',
                effectivePlan,
                string.Join(',', modules.OrderBy(m => m, StringComparer.Ordinal)),
                string.Join(',', readOnly.OrderBy(m => m, StringComparer.Ordinal)),
                string.Join(',', features.OrderBy(f => f, StringComparer.Ordinal)),
                limitsFingerprint);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        }

        private List<string> ResolveSelfServicePlans()
        {
            if (_options.Enabled && _options.SelfServicePlans.Count > 0)
            {
                return _options.SelfServicePlans
                    .Where(Company.IsKnownPlan)
                    .Select(Company.NormalizePlanKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            return Company.KnownPlans.ToList();
        }

        private Company LoadCompany(int companyId) =>
            _companies.Table.FirstOrDefault(c => c.Id == companyId)
            ?? throw new TenantEntityNotFoundException(nameof(Company), companyId);

        private bool IsCompanyLimited(Company company)
        {
            if (!_options.Enabled) return false;
            if (string.Equals(CompanyTiers.Normalize(company.Tier), CompanyTiers.Comp, StringComparison.Ordinal))
                return false;
            var plan = Company.NormalizePlanKey(company.Plan ?? "accounting_free");
            return !IsPaidStoredPlan(plan);
        }

        private int? LimitOf(MeteredResource resource) =>
            _options.FreeLimits.TryGetValue(resource, out var limit) ? limit : null;

        private int CountUsed(int companyId, MeteredResource resource, PolicyMemo policy)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            switch (resource)
            {
                case MeteredResource.SalesDocuments:
                case MeteredResource.Bills:
                    var key = MeteredResources.Key(resource);
                    var windowStart = MonthlyMeterWindowStart(now, policy);
                    return _usage.Table.Count(u => u.CompanyId == companyId && u.Resource == key && u.OccurredAt >= windowStart);
                case MeteredResource.Seats:
                    var members = _members.Table.Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);
                    var pending = _invitations.Table.Count(i => i.CompanyId == companyId && i.AcceptedAt == null
                        && i.RevokedAt == null && i.ExpiresAt > now);
                    return members + pending;
                case MeteredResource.BankAccounts:
                    return _banks.Table.Count(b => b.CompanyId == companyId);
                default:
                    return 0;
            }
        }

        /// <summary>§6.7 monthly meter window: max(monthStart, paidAccessEndedAt) when ended in current month.</summary>
        private static DateTime MonthlyMeterWindowStart(DateTime nowUtc, PolicyMemo policy)
        {
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var ended = DerivePaidAccessEndedAt(policy, nowUtc);
            if (ended == null)
                return monthStart;
            var endedUtc = ended.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(ended.Value, DateTimeKind.Utc)
                : ended.Value.ToUniversalTime();
            if (endedUtc.Year == monthStart.Year && endedUtc.Month == monthStart.Month && endedUtc > monthStart)
                return endedUtc;
            return monthStart;
        }

        private static DateTime? DerivePaidAccessEndedAt(PolicyMemo policy, DateTime nowUtc)
        {
            if (policy.Lifecycle.ReducedBy == null)
                return null;

            var status = policy.Lifecycle.Status ?? "";
            // incomplete never had paid access — calendar month only.
            if (status == "incomplete")
                return null;

            if (status == "past_due" && policy.Lifecycle.PeriodEnd != null)
            {
                var periodEnd = policy.Lifecycle.PeriodEnd.Value;
                var periodEndUtc = periodEnd.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(periodEnd, DateTimeKind.Utc)
                    : periodEnd.ToUniversalTime();
                return periodEndUtc.Add(PastDueGrace);
            }

            // canceled / unpaid / incomplete_expired / paused / unknown → stored ?? periodEnd
            return policy.PaidAccessEndedAt ?? policy.Lifecycle.PeriodEnd;
        }

        private List<Company> OwnedWorkspaces(string userId)
        {
            var companyIds = _members.Table
                .Where(m => m.UserId == userId && m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active)
                .Select(m => m.CompanyId)
                .ToList();
            return _companies.Table.Where(c => companyIds.Contains(c.Id)).ToList();
        }

        /// <summary>Per-request policy memo; features may be swapped after ResolveFeatures.</summary>
        protected sealed class PolicyMemo
        {
            public PolicyMemo(
                Company company,
                string storedPlan,
                string effectivePlan,
                string tier,
                bool limited,
                IReadOnlyList<string> modules,
                IReadOnlyList<string> readOnlyModules,
                EntitlementLifecycle lifecycle,
                string version,
                DateTime? paidAccessEndedAt,
                IReadOnlySet<string> features)
            {
                Company = company;
                StoredPlan = storedPlan;
                EffectivePlan = effectivePlan;
                Tier = tier;
                Limited = limited;
                Modules = new HashSet<string>(modules, StringComparer.OrdinalIgnoreCase);
                ReadOnlyModules = new HashSet<string>(readOnlyModules, StringComparer.OrdinalIgnoreCase);
                StoredActive = new HashSet<string>(company.GetActiveModules(), StringComparer.OrdinalIgnoreCase);
                Lifecycle = lifecycle;
                Version = version;
                PaidAccessEndedAt = paidAccessEndedAt;
                Features = features;
            }

            public Company Company { get; }
            public string StoredPlan { get; }
            public string EffectivePlan { get; }
            public string Tier { get; }
            public bool Limited { get; }
            public HashSet<string> Modules { get; }
            public HashSet<string> ReadOnlyModules { get; }
            public HashSet<string> StoredActive { get; }
            public EntitlementLifecycle Lifecycle { get; }
            public string Version { get; private set; }
            public DateTime? PaidAccessEndedAt { get; }
            public IReadOnlySet<string> Features { get; private set; }

            public PolicyMemo WithFeatures(IReadOnlySet<string> features) =>
                new(Company, StoredPlan, EffectivePlan, Tier, Limited, Modules.ToList(), ReadOnlyModules.ToList(),
                    Lifecycle, Version, PaidAccessEndedAt, features);

            public PolicyMemo WithVersion(string version) =>
                new(Company, StoredPlan, EffectivePlan, Tier, Limited, Modules.ToList(), ReadOnlyModules.ToList(),
                    Lifecycle, version, PaidAccessEndedAt, Features);

            public bool HasModuleUnderStoredPlan(string moduleKey) => StoredActive.Contains(moduleKey);
            public bool IsModuleAvailable(string moduleKey) => Modules.Contains(moduleKey);
            public bool IsReadOnlyModule(string moduleKey) => ReadOnlyModules.Contains(moduleKey);
        }

        private sealed class Releaser : IDisposable
        {
            private Action? _release;
            public Releaser(Action release) => _release = release;
            public void Dispose()
            {
                _release?.Invoke();
                _release = null;
            }
        }
    }
}
