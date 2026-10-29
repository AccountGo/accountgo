// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Billing;
using Core.Data;
using Core.Domain;
using Core.Entitlements;

namespace Services.Billing;

/// <summary>
/// Self-hosted / community default: billing is off, plan changes apply immediately elsewhere,
/// and no customer portal is available.
/// </summary>
public sealed class CommunityBillingProvider : IBillingProvider
{
    private readonly IRepository<Company> _companies;
    private readonly IEntitlementService _entitlements;

    public CommunityBillingProvider(IRepository<Company> companies, IEntitlementService entitlements)
    {
        _companies = companies;
        _entitlements = entitlements;
    }

    public bool Enabled => false;

    public bool RequiresProviderCheckout => false;

    public Task<PlanChangeResult> RequestPlanChangeAsync(
        int companyId,
        string planKey,
        string returnUrl,
        CancellationToken ct)
    {
        // Community never redirects; AdministrationService applies the plan immediately.
        return Task.FromResult(new PlanChangeResult(AppliedImmediately: true, RedirectUrl: null, ErrorReason: null));
    }

    public Task<string?> GetPortalUrlAsync(int companyId, string returnUrl, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task<BillingStateDto> GetBillingStateAsync(int companyId, CancellationToken ct)
    {
        var company = _companies.GetById(companyId)
            ?? throw new Core.Tenancy.TenantEntityNotFoundException(nameof(Company), companyId);
        var snapshot = _entitlements.Get(companyId);
        var limits = snapshot.Usage.ToDictionary(
            u => MeteredResources.Key(u.Resource),
            u => (object?)new Dictionary<string, object?>
            {
                ["limit"] = u.Limit,
                ["used"] = u.Used,
                ["period"] = u.Monthly ? "month" : null
            });

        return Task.FromResult(new BillingStateDto(
            BillingEnabled: false,
            Plan: company.Plan ?? snapshot.Plan,
            Tier: CompanyTiers.Normalize(company.Tier),
            Status: company.SubscriptionStatus ?? "active",
            CurrentPeriodEnd: company.CurrentPeriodEnd ?? company.SubscriptionValidUntil,
            CancelAtPeriodEnd: company.CancelAtPeriodEnd,
            Limits: limits,
            Features: snapshot.Features.ToList(),
            PortalAvailable: false));
    }
}
