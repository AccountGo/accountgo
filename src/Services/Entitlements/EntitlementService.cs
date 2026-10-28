// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
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
    /// </summary>
    public class EntitlementService : IEntitlementService
    {
        private static readonly MeteredResource[] AllResources = (MeteredResource[])Enum.GetValues(typeof(MeteredResource));
        private static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);

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
            var transitions = Company.KnownPlans
                .Where(p => Company.IsAllowedSelfServicePlanChange(policy.StoredPlan, p))
                .ToList();

            return new EntitlementSnapshot(
                _options.Enabled,
                policy.StoredPlan,
                policy.Tier,
                limited,
                policy.Modules.ToList(),
                selfService,
                usage,
                _options.SupportEmail,
                Features: null,
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
                _ => throw new NotSupportedException($"Entitlement action '{action.GetType().Name}' is not implemented until a later phase.")
            };
        }

        public void EnsureCanCreate(MeteredResource resource, int count = 1) =>
            EnsureCanCreate(_tenant.RequireCompanyId(), resource, count);

        public void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1)
        {
            if (!_options.Enabled || _bypassDepth > 0 || resource == MeteredResource.OwnedWorkspaces)
                return;
            var policy = ResolvePolicy(companyId);
            if (!policy.Limited)
                return;
            var limit = LimitOf(resource);
            if (limit == null)
                return;
            var used = CountUsed(companyId, resource, policy);
            if (used + count > limit.Value)
                throw new QuotaExceededException(resource, limit.Value, used);
        }

        public void Record(MeteredResource resource, int? entityId)
        {
            if (!_options.Enabled || !MeteredResources.IsMonthly(resource))
                return;
            _usage.Insert(new EntitlementUsage
            {
                CompanyId = _tenant.RequireCompanyId(),
                Resource = MeteredResources.Key(resource),
                EntityId = entityId,
                OccurredAt = DateTime.UtcNow
            });
        }

        public void EnsureCanOwnAnotherWorkspace(string userId)
        {
            if (!_options.Enabled || _bypassDepth > 0)
                return;
            if (!_options.FreeLimits.TryGetValue(MeteredResource.OwnedWorkspaces, out var limit))
                return;

            var owned = OwnedWorkspaces(userId);
            if (owned.Any(c => !IsCompanyLimited(c)))
                return;
            if (owned.Count + 1 > limit)
                throw new QuotaExceededException(MeteredResource.OwnedWorkspaces, limit, owned.Count);
        }

        public void EnsureSelfServicePlan(string plan)
        {
            if (!_options.IsSelfServicePlan(plan))
                throw new PlanNotAvailableException(plan);
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
                    UpgradeAvailable: true);
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
                    UpgradeAvailable: true,
                    SubscriptionStatus: policy.Lifecycle.Status);
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "MODULE_NOT_SUBSCRIBED",
                Detail: keys.Length == 1
                    ? $"The '{keys[0]}' module is not included in this organization's active plan."
                    : $"None of the '{string.Join("', '", keys)}' modules is included in this organization's active plan.",
                RequiredModule: keys[0],
                UpgradeAvailable: true,
                SubscriptionStatus: policy.Lifecycle.Status);
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
            var limited = _options.Enabled
                && !string.Equals(tier, CompanyTiers.Comp, StringComparison.Ordinal)
                && !IsPaidStoredPlan(effectivePlan);

            var limitsFingerprint = string.Join(',',
                AllResources.Select(r => $"{MeteredResources.Key(r)}={(limited ? LimitOf(r)?.ToString() ?? "-" : "-")}"));
            var version = HashPolicy(effectivePlan, modules, readOnly, features: Array.Empty<string>(), limitsFingerprint);

            var lifecycle = new EntitlementLifecycle(
                Status: status,
                GraceEndsAt: graceEndsAt,
                CancelAtPeriodEnd: company.CancelAtPeriodEnd,
                PeriodEnd: periodEnd,
                ReducedBy: reducedBy);

            var memo = new PolicyMemo(
                company, storedPlan, effectivePlan, tier, limited, modules, readOnly, lifecycle, version,
                paidAccessEndedAt: company.PaidAccessEndedAt);
            _policyMemo[companyId] = memo;
            return memo;
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

        private sealed class PolicyMemo
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
                DateTime? paidAccessEndedAt)
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
            public string Version { get; }
            public DateTime? PaidAccessEndedAt { get; }

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
