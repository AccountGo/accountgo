// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Services.Billing;

namespace Services.Entitlements;

/// <summary>
/// Community / self-hosted entitlement service. Never emits feature keys (such as "mfa").
/// </summary>
public class CommunityEntitlementService : EntitlementService
{
    public CommunityEntitlementService(
        EntitlementsOptions options,
        IPlanCatalog plans,
        IRepository<Company> companies,
        IRepository<CompanyMember> members,
        IRepository<CompanyInvitation> invitations,
        IRepository<EntitlementUsage> usage,
        IRepository<Bank> banks,
        ITenantContext tenant,
        TimeProvider? time = null)
        : base(options, plans, companies, members, invitations, usage, banks, tenant, time)
    {
    }
}
