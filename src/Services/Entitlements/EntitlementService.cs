// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.Extensions.Logging;

namespace Services.Entitlements
{
    /// <summary>
    /// Configuration-driven entitlements. Limits apply only when <see cref="EntitlementsOptions.Enabled"/> and the
    /// workspace is on the free tier; everything else (self-host, pro, comp) is unlimited.
    /// Module <see cref="Evaluate"/> runs pipeline steps 1–6 (no usage counts).
    /// </summary>
    public class EntitlementService : IEntitlementService
    {
        private static readonly MeteredResource[] AllResources = (MeteredResource[])Enum.GetValues(typeof(MeteredResource));

        private readonly EntitlementsOptions _options;
        private readonly IRepository<Company> _companies;
        private readonly IRepository<CompanyMember> _members;
        private readonly IRepository<CompanyInvitation> _invitations;
        private readonly IRepository<EntitlementUsage> _usage;
        private readonly IRepository<Bank> _banks;
        private readonly ITenantContext _tenant;
        private readonly TimeProvider _time;
        private readonly ILogger<EntitlementService>? _logger;
        // Assumption: Plan / ActiveModulesJson / status / tier / billing flags are immutable for the
        // request after the first ResolvePolicy; writers must ClearPolicyMemo so a later Evaluate sees fresh state.
        private readonly Dictionary<int, PolicyMemo> _policyMemo = new();
        private int _bypassDepth;

        private static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);

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
            var company = LoadCompany(companyId);
            var limited = IsLimited(company);
            var usage = AllResources
                .Where(r => r != MeteredResource.OwnedWorkspaces) // per user, not per workspace
                .Select(r => new ResourceUsage(r, limited ? LimitOf(r) : null, CountUsed(companyId, r), MeteredResources.IsMonthly(r)))
                .ToList();
            var selfService = _options.Enabled && _options.SelfServicePlans.Count > 0
                ? _options.SelfServicePlans.Where(Company.IsKnownPlan).Select(p => p.ToLowerInvariant()).ToList()
                : Company.KnownPlans.ToList();

            return new EntitlementSnapshot(
                _options.Enabled,
                company.Plan ?? "accounting",
                CompanyTiers.Normalize(company.Tier),
                limited,
                company.GetActiveModules(),
                selfService,
                usage,
                _options.SupportEmail);
        }

        public EntitlementDecision Evaluate(int companyId, EntitlementAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return action switch
            {
                EntitlementAction.Module module => EvaluateModule(companyId, module.AnyOf),
                _ => throw new NotSupportedException($"Entitlement action '{action.GetType().Name}' is not implemented until a later phase.")
            };
        }

        public void EnsureCanCreate(MeteredResource resource, int count = 1) =>
            EnsureCanCreate(_tenant.RequireCompanyId(), resource, count);

        public void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1)
        {
            if (!_options.Enabled || _bypassDepth > 0 || resource == MeteredResource.OwnedWorkspaces)
                return;
            var limit = LimitOf(resource);
            if (limit == null || !IsLimited(LoadCompany(companyId)))
                return;
            var used = CountUsed(companyId, resource);
            if (used + count > limit.Value)
                throw new QuotaExceededException(resource, limit.Value, used);
        }

        public void Record(MeteredResource resource, int? entityId)
        {
            // Self-hosted builds keep no usage history; counting starts when limits are turned on.
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

            // Owning a paid or comped workspace lifts the per-user limit.
            var owned = OwnedWorkspaces(userId);
            if (owned.Any(c => !IsLimited(c)))
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

        private EntitlementDecision EvaluateModule(int companyId, IReadOnlyList<string> anyOf)
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
            var allowedKey = keys.FirstOrDefault(k => policy.IsModuleAvailable(k));
            if (allowedKey != null)
            {
                return new EntitlementDecision(
                    Allowed: true,
                    Code: null,
                    Detail: null,
                    RequiredModule: allowedKey);
            }

            // SUBSCRIPTION_INACTIVE only when the module is under the stored plan (incl. owner toggles)
            // but not under the lifecycle-reduced effective set — same rule as E0.5 RequireModule.
            var lapsedKey = keys.FirstOrDefault(k =>
                policy.HasModuleUnderStoredPlan(k) && !policy.IsModuleAvailable(k));
            if (lapsedKey != null)
            {
                return new EntitlementDecision(
                    Allowed: false,
                    Code: "SUBSCRIPTION_INACTIVE",
                    Detail: "This workspace's subscription is inactive. Update billing to restore paid modules.",
                    RequiredModule: lapsedKey,
                    UpgradeAvailable: true);
            }

            return new EntitlementDecision(
                Allowed: false,
                Code: "MODULE_NOT_SUBSCRIBED",
                Detail: keys.Length == 1
                    ? $"The '{keys[0]}' module is not included in this organization's active plan."
                    : $"None of the '{string.Join("', '", keys)}' modules is included in this organization's active plan.",
                RequiredModule: keys[0],
                UpgradeAvailable: true);
        }

        public void ClearPolicyMemo(int companyId) => _policyMemo.Remove(companyId);

        /// <summary>Pipeline steps 1–6; memoized per request (scoped service) by companyId.</summary>
        private PolicyMemo ResolvePolicy(int companyId)
        {
            if (_policyMemo.TryGetValue(companyId, out var cached))
                return cached;

            // 1. Load
            var company = LoadCompany(companyId);

            // 2. Normalize plan key (unknown → default free row + error log)
            var storedPlan = NormalizeStoredPlan(companyId, company.Plan);

            // 3. Lifecycle → effective plan (§6.2 table a–h; independent of SubscriptionAccess)
            var now = _time.GetUtcNow().UtcDateTime;
            var effectivePlan = ResolveEffectivePlan(company, storedPlan, now);

            // 4. Catalog lookup for effective plan
            var effectiveCatalog = Company.ModulesForPlan(effectivePlan);

            // 5. Allow-list relative to the *stored* plan (same as Company.GetActiveModules today).
            // TODO(E1b): migrate to DisabledModulesJson = ModulesForPlan(Plan) − stored at migration time.
            var storedActive = company.GetActiveModules();

            // 6. Fold add-ons — no-op until E4 (CompanyAddOn); leave named seam.
            FoldAddOnsNoOp();

            var memo = new PolicyMemo(storedPlan, effectivePlan, storedActive, effectiveCatalog);
            _policyMemo[companyId] = memo;
            return memo;
        }

        /// <summary>Spec §6.2 step 3 — ordered a–h; first match wins. Paid detection uses the step-2 key.</summary>
        private string ResolveEffectivePlan(Company company, string storedPlan, DateTime nowUtc)
        {
            // a) Tier = comp or BillingManagedManually → stored plan (ignore provider status)
            if (string.Equals(CompanyTiers.Normalize(company.Tier), CompanyTiers.Comp, StringComparison.Ordinal)
                || company.BillingManagedManually)
                return storedPlan;

            // b) Stored plan is free → stored plan
            if (!IsPaidStoredPlan(storedPlan))
                return storedPlan;

            // §6.2 row c (amended): null / empty / whitespace status normalizes to active.
            var status = string.IsNullOrWhiteSpace(company.SubscriptionStatus)
                ? "active"
                : company.SubscriptionStatus.Trim().ToLowerInvariant();

            // c) active / trialing
            if (status is "active" or "trialing")
                return storedPlan;

            // d/e) past_due — grace inclusive at periodEnd + 14d
            if (status == "past_due")
            {
                var periodEnd = company.CurrentPeriodEnd ?? company.SubscriptionValidUntil;
                if (periodEnd != null)
                {
                    var periodEndUtc = periodEnd.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(periodEnd.Value, DateTimeKind.Utc)
                        : periodEnd.Value.ToUniversalTime();
                    if (nowUtc <= periodEndUtc.Add(PastDueGrace))
                        return storedPlan; // d
                }
                return Company.FreePlanFor(storedPlan); // e
            }

            // f) canceled / unpaid / incomplete_expired
            if (status is "canceled" or "unpaid" or "incomplete_expired")
                return Company.FreePlanFor(storedPlan);

            // g) incomplete (first payment never succeeded)
            if (status == "incomplete")
                return Company.FreePlanFor(storedPlan);

            // h) paused or any non-empty unrecognized status → free + error log
            EntitlementEngineShadow.LogErrorOnce(
                _logger,
                $"unrecognized-status|{company.Id}|{status}",
                "Unrecognized subscription status '{Status}' for company {CompanyId} — treating as free-plan entitlements",
                status, company.Id);
            return Company.FreePlanFor(storedPlan);
        }

        /// <summary>Paid detection from the step-2 normalized key (not <see cref="Company.IsPaidPlan"/>).</summary>
        private static bool IsPaidStoredPlan(string normalizedStoredPlan) =>
            normalizedStoredPlan is "enterprise"
            || normalizedStoredPlan.EndsWith("_pro", StringComparison.Ordinal);

        private string NormalizeStoredPlan(int companyId, string? plan)
        {
            var key = Company.NormalizePlanKey(string.IsNullOrWhiteSpace(plan) ? "accounting" : plan);
            if (Company.IsKnownPlan(key))
                return key;

            EntitlementEngineShadow.LogErrorOnce(
                _logger,
                $"unknown-plan|{companyId}|{plan}",
                "Unknown company plan key '{Plan}' — treating as accounting_free for entitlements",
                plan);
            return "accounting_free";
        }

        /// <summary>Step 6 seam: add-on quota deltas land in E4.</summary>
        private static void FoldAddOnsNoOp() { }

        private Company LoadCompany(int companyId) =>
            _companies.Table.FirstOrDefault(c => c.Id == companyId)
            ?? throw new TenantEntityNotFoundException(nameof(Company), companyId);

        private bool IsLimited(Company company) =>
            _options.Enabled && CompanyTiers.Normalize(company.Tier) == CompanyTiers.Free;

        private int? LimitOf(MeteredResource resource) =>
            _options.FreeLimits.TryGetValue(resource, out var limit) ? limit : null;

        private int CountUsed(int companyId, MeteredResource resource)
        {
            var now = DateTime.UtcNow;
            switch (resource)
            {
                case MeteredResource.SalesDocuments:
                case MeteredResource.Bills:
                    var key = MeteredResources.Key(resource);
                    var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return _usage.Table.Count(u => u.CompanyId == companyId && u.Resource == key && u.OccurredAt >= monthStart);
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

        private List<Company> OwnedWorkspaces(string userId)
        {
            var companyIds = _members.Table
                .Where(m => m.UserId == userId && m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active)
                .Select(m => m.CompanyId)
                .ToList();
            // Company's query filter hides deleted workspaces.
            return _companies.Table.Where(c => companyIds.Contains(c.Id)).ToList();
        }

        private sealed class PolicyMemo
        {
            private readonly HashSet<string> _storedActive;
            private readonly HashSet<string> _effectiveModules;

            public PolicyMemo(
                string storedPlan,
                string effectivePlan,
                IReadOnlyList<string> storedActive,
                IReadOnlyList<string> effectiveCatalog)
            {
                StoredPlan = storedPlan;
                EffectivePlan = effectivePlan;
                _storedActive = new HashSet<string>(storedActive, StringComparer.OrdinalIgnoreCase);
                // Steps 4∩5: modules present after catalog lookup for the effective plan and the
                // stored allow-list (GetActiveModules). Independent of SubscriptionAccess.IsModuleAvailable.
                _effectiveModules = new HashSet<string>(
                    storedActive.Where(m => effectiveCatalog.Contains(m, StringComparer.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase);
            }

            public string StoredPlan { get; }
            public string EffectivePlan { get; }

            public bool HasModuleUnderStoredPlan(string moduleKey) =>
                _storedActive.Contains(moduleKey);

            public bool IsModuleAvailable(string moduleKey) =>
                _effectiveModules.Contains(moduleKey);
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
