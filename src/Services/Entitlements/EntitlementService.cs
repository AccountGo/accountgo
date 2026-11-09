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
using Services.Billing;

namespace Services.Entitlements
{
    /// <summary>
    /// Community / self-host entitlement engine: all catalog modules minus owner opt-outs,
    /// unlimited quotas, no features, no lifecycle, no plan ladder. <see cref="Company.Plan"/>
    /// is ignored for gates (always the community plan).
    /// </summary>
    public class EntitlementService : IEntitlementService
    {
        private static readonly MeteredResource[] WorkspaceResources =
        {
            MeteredResource.SalesDocuments,
            MeteredResource.Bills,
            MeteredResource.Seats,
            MeteredResource.BankAccounts
        };

        private static readonly IReadOnlySet<string> NoFeatures = ImmutableHashSet<string>.Empty;

        private readonly EntitlementsOptions _options;
        private readonly IPlanCatalog _plans;
        private readonly IRepository<Company> _companies;
        private readonly IRepository<CompanyMember> _members;
        private readonly IRepository<CompanyInvitation> _invitations;
        private readonly IRepository<EntitlementUsage> _usage;
        private readonly IRepository<Bank> _banks;
        private readonly ITenantContext _tenant;
        private readonly TimeProvider _time;
        private readonly Dictionary<int, PolicyMemo> _policyMemo = new();
        private int _bypassDepth;

        public EntitlementService(
            EntitlementsOptions options,
            IPlanCatalog plans,
            IRepository<Company> companies,
            IRepository<CompanyMember> members,
            IRepository<CompanyInvitation> invitations,
            IRepository<EntitlementUsage> usage,
            IRepository<Bank> banks,
            ITenantContext tenant,
            TimeProvider? time = null)
        {
            _options = options;
            _plans = plans;
            _companies = companies;
            _members = members;
            _invitations = invitations;
            _usage = usage;
            _banks = banks;
            _tenant = tenant;
            _time = time ?? TimeProvider.System;
        }

        public EntitlementsOptions Options => _options;

        public EntitlementSnapshot Get(int companyId)
        {
            var policy = ResolvePolicy(companyId);
            var company = LoadCompany(companyId);
            var usage = WorkspaceResources
                .Select(r =>
                {
                    var limit = GetQuota(company, r);
                    var used = CountUsed(companyId, r);
                    return new ResourceUsage(r, limit, used, MeteredResources.IsMonthly(r), OverLimit: limit.HasValue && used > limit.Value);
                })
                .ToList();

            var limited = usage.Any(u => u.Limit.HasValue);
            var planKey = NormalizePlanKey(company.Plan);

            return new EntitlementSnapshot(
                Enabled: limited,
                Plan: planKey,
                Tier: policy.Tier,
                Limited: limited,
                Modules: policy.Modules.ToList(),
                SelfServicePlans: new List<string> { CommunityPlanCatalog.PlanKey },
                Usage: usage,
                SupportEmail: _options.SupportEmail,
                Features: NoFeatures,
                EffectivePlan: planKey,
                ReadOnlyModules: Array.Empty<string>(),
                Lifecycle: null,
                AllowedTransitions: Array.Empty<string>(),
                Version: policy.Version);
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

        public bool TryGetResolvedTier(int companyId, out string tier)
        {
            if (_policyMemo.TryGetValue(companyId, out var memo))
            {
                tier = memo.Tier;
                return true;
            }

            tier = "";
            return false;
        }

        public EntitlementDecision Evaluate(int companyId, EntitlementAction action) => action switch
        {
            EntitlementAction.Module module => EvaluateModule(companyId, module),
            EntitlementAction.Feature feature => EvaluateFeature(feature),
            EntitlementAction.Create create => EvaluateCreate(companyId, create),
            _ => new EntitlementDecision(true, null, null, UpgradeAvailable: false)
        };

        public void Ensure(int companyId, EntitlementAction action)
        {
            var decision = Evaluate(companyId, action);
            if (!decision.Allowed)
                throw EntitlementDecisions.ToException(decision);
        }

        public EntitlementDecision EvaluateForUser(string userId, UserAction action) => action switch
        {
            UserAction.OwnAnotherWorkspace => new EntitlementDecision(true, null, null, UpgradeAvailable: false),
            UserAction.CreateWorkspace => new EntitlementDecision(true, null, null, UpgradeAvailable: false, RequiresCheckout: false),
            UserAction.ReceiveWorkspace => new EntitlementDecision(true, null, null, UpgradeAvailable: false, RequiresCheckout: false),
            UserAction.FeatureAmongMemberships feature => new EntitlementDecision(
                false, "FEATURE_NOT_ENTITLED",
                $"The '{feature.FeatureKey}' feature is not available on any of this user's workspaces.",
                RequiredFeature: feature.FeatureKey, UpgradeAvailable: false),
            _ => new EntitlementDecision(true, null, null, UpgradeAvailable: false)
        };

        public void EnsureCanCreate(MeteredResource resource, int count = 1) =>
            EnsureCanCreate(_tenant.RequireCompanyId(), resource, count);

        public void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1) =>
            Ensure(companyId, new EntitlementAction.Create(resource, count));

        public void Record(MeteredResource resource, int? entityId) =>
            Record(_tenant.RequireCompanyId(), resource, entityId);

        public void Record(int companyId, MeteredResource resource, int? entityId)
        {
            if (!MeteredResources.IsMonthly(resource))
                return;
            _usage.Insert(new EntitlementUsage
            {
                CompanyId = companyId,
                Resource = MeteredResources.Key(resource),
                EntityId = entityId,
                OccurredAt = _time.GetUtcNow().UtcDateTime
            });
        }

        public void EnsureCanOwnAnotherWorkspace(string userId)
        {
            var decision = EvaluateForUser(userId, new UserAction.OwnAnotherWorkspace());
            if (!decision.Allowed)
                throw EntitlementDecisions.ToException(decision);
        }

        public void EnsureSelfServicePlan(string plan)
        {
            // Community has a single plan; every key resolves to it — never reject.
        }

        public IDisposable Bypass()
        {
            _bypassDepth++;
            return new Releaser(() => _bypassDepth--);
        }

        private EntitlementDecision EvaluateModule(int companyId, EntitlementAction.Module module)
        {
            if (_bypassDepth > 0)
                return new EntitlementDecision(true, null, null, UpgradeAvailable: false);

            var policy = ResolvePolicy(companyId);
            var hit = module.AnyOf.FirstOrDefault(m =>
                policy.Modules.Contains(m, StringComparer.OrdinalIgnoreCase));
            if (hit != null)
            {
                return new EntitlementDecision(true, null, null,
                    RequiredModule: hit,
                    EffectivePlan: CommunityPlanCatalog.PlanKey,
                    UpgradeAvailable: false);
            }

            var required = module.AnyOf.FirstOrDefault() ?? "";
            var detail = module.AnyOf.Length == 1
                ? $"The '{required}' module is not included in this organization's active plan."
                : $"None of the '{string.Join("', '", module.AnyOf)}' modules is included in this organization's active plan.";
            return new EntitlementDecision(
                false, "MODULE_NOT_SUBSCRIBED", detail,
                RequiredModule: required,
                EffectivePlan: CommunityPlanCatalog.PlanKey,
                UpgradeAvailable: false);
        }

        private EntitlementDecision EvaluateFeature(EntitlementAction.Feature feature)
        {
            // Community never emits feature keys (FeaturesEnabled is ignored for emission).
            return new EntitlementDecision(
                false, "FEATURE_NOT_ENTITLED",
                $"The '{feature.Key}' feature is not included in this plan.",
                RequiredFeature: feature.Key,
                EffectivePlan: CommunityPlanCatalog.PlanKey,
                UpgradeAvailable: false);
        }

        private EntitlementDecision EvaluateCreate(int companyId, EntitlementAction.Create create)
        {
            if (_bypassDepth > 0)
                return new EntitlementDecision(true, null, null, Resource: create.Resource, UpgradeAvailable: false);

            var company = LoadCompany(companyId);
            var limit = GetQuota(company, create.Resource);
            var used = CountUsed(companyId, create.Resource);
            var effectivePlan = NormalizePlanKey(company.Plan);

            if (limit is { } max && used + create.Count > max)
            {
                return new EntitlementDecision(
                    false, "QUOTA_EXCEEDED",
                    QuotaDescription(create.Resource, max),
                    Resource: create.Resource,
                    Limit: max,
                    Used: used,
                    Period: MeteredResources.PeriodOf(create.Resource),
                    EffectivePlan: effectivePlan,
                    UpgradeAvailable: true);
            }

            return new EntitlementDecision(
                true, null, null,
                Resource: create.Resource,
                Limit: limit,
                Used: used,
                Period: MeteredResources.PeriodOf(create.Resource),
                EffectivePlan: effectivePlan,
                UpgradeAvailable: false);
        }

        private static int? GetQuota(Company company, MeteredResource resource)
        {
            if (string.Equals(company.Tier, CompanyTiers.Comp, StringComparison.OrdinalIgnoreCase))
                return null;

            return (company.Plan?.Trim().ToLowerInvariant(), resource) switch
            {
                ("invoicing_free" or "invoicing", MeteredResource.SalesDocuments) => 5,
                ("invoicing_free" or "invoicing", MeteredResource.Bills) => 20,
                ("invoicing_free" or "invoicing", MeteredResource.Seats) => 1,

                ("accounting_free" or "accounting", MeteredResource.SalesDocuments) => 10,
                ("accounting_free" or "accounting", MeteredResource.Bills) => 10,
                ("accounting_free" or "accounting", MeteredResource.Seats) => 1,
                ("accounting_free" or "accounting", MeteredResource.BankAccounts) => 2,

                _ => null
            };
        }

        private static string QuotaDescription(MeteredResource resource, int limit) => resource switch
        {
            MeteredResource.SalesDocuments => $"The plan includes {limit} invoices and quotes per month.",
            MeteredResource.Bills => $"The plan includes {limit} bills per month.",
            MeteredResource.Seats => limit == 1
                ? "The plan includes 1 user per workspace."
                : $"The plan includes {limit} users per workspace.",
            MeteredResource.BankAccounts => $"The plan includes {limit} bank and cash accounts.",
            _ => "This workspace has reached a plan limit."
        };

        private static string NormalizePlanKey(string? plan) => plan?.Trim().ToLowerInvariant() switch
        {
            "invoicing" or "invoicing_free" => "invoicing_free",
            "accounting" or "accounting_free" => "accounting_free",
            "invoicing_solo" => "invoicing_solo",
            "invoicing_pro" => "invoicing_pro",
            "accounting_pro" => "accounting_pro",
            "enterprise" => "enterprise",
            _ => CommunityPlanCatalog.PlanKey
        };

        private PolicyMemo ResolvePolicy(int companyId)
        {
            if (_policyMemo.TryGetValue(companyId, out var cached))
                return cached;

            var company = LoadCompany(companyId);
            var modules = PlanModuleResolver.ResolveActiveModules(_plans, company);
            var tier = CompanyTiers.Derive(company.Tier, company.Plan ?? CommunityPlanCatalog.PlanKey);
            var version = HashPolicy(company.Plan, modules);
            var memo = new PolicyMemo(modules, tier, version);
            _policyMemo[companyId] = memo;
            return memo;
        }

        private static string HashPolicy(string? plan, IReadOnlyList<string> modules)
        {
            var payload = string.Join('|',
                NormalizePlanKey(plan),
                string.Join(',', modules.OrderBy(m => m, StringComparer.Ordinal)));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        }

        private int CountUsed(int companyId, MeteredResource resource)
        {
            var now = _time.GetUtcNow().UtcDateTime;
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
                    // D9 / #413: archived banks do not consume the plan slot.
                    return _banks.Table.Count(b => b.CompanyId == companyId && b.ArchivedAt == null);
                default:
                    return 0;
            }
        }

        private Company LoadCompany(int companyId) =>
            (_tenant is IResolvedCompanyContext { ResolvedCompany: { } company } && company.Id == companyId ? company : null) ?? _companies.Table.FirstOrDefault(c => c.Id == companyId)
            ?? throw new TenantEntityNotFoundException(nameof(Company), companyId);

        private sealed class PolicyMemo
        {
            public PolicyMemo(IReadOnlyList<string> modules, string tier, string version)
            {
                Modules = modules;
                Tier = tier;
                Version = version;
            }

            public IReadOnlyList<string> Modules { get; }
            public string Tier { get; }
            public string Version { get; }
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
