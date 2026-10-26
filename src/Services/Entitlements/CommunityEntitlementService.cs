// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;

namespace Services.Entitlements;

/// <summary>
/// Community / self-hosted entitlement service. Never contains commercial features (such as "mfa").
/// </summary>
public class CommunityEntitlementService : EntitlementService
{
    public CommunityEntitlementService(
        EntitlementsOptions options,
        IRepository<Company> companies,
        IRepository<CompanyMember> members,
        IRepository<CompanyInvitation> invitations,
        IRepository<EntitlementUsage> usage,
        IRepository<Bank> banks,
        ITenantContext tenant)
        : base(options, companies, members, invitations, usage, banks, tenant)
    {
    }
}
