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

namespace Services.Entitlements
{
    /// <summary>
    /// Configuration-driven entitlements. Limits apply only when <see cref="EntitlementsOptions.Enabled"/> and the
    /// workspace is on the free tier; everything else (self-host, pro, comp) is unlimited.
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
        private int _bypassDepth;

        public EntitlementService(
            EntitlementsOptions options,
            IRepository<Company> companies,
            IRepository<CompanyMember> members,
            IRepository<CompanyInvitation> invitations,
            IRepository<EntitlementUsage> usage,
            IRepository<Bank> banks,
            ITenantContext tenant)
        {
            _options = options;
            _companies = companies;
            _members = members;
            _invitations = invitations;
            _usage = usage;
            _banks = banks;
            _tenant = tenant;
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
