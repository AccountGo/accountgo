// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Core.Billing;
using Core.Data;
using Core.Domain;
using Core.Entitlements;
using Moq;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Billing;

public class CommunityBillingProviderTests
{
    [Fact]
    public async Task CommunityProvider_IsDisabled_AndAppliesImmediately()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Acme",
            Plan = "community",
            SubscriptionStatus = "active"
        };
        var companies = new Mock<IRepository<Company>>();
        companies.Setup(r => r.GetById(It.IsAny<object>())).Returns(company);

        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.Get(1)).Returns(new EntitlementSnapshot(
            Enabled: false,
            Plan: CommunityPlanCatalog.PlanKey,
            Tier: CompanyTiers.Free,
            Limited: false,
            Modules: CommunityPlanCatalog.AllModules,
            SelfServicePlans: new[] { CommunityPlanCatalog.PlanKey },
            Usage: System.Array.Empty<ResourceUsage>(),
            SupportEmail: null));

        var provider = new CommunityBillingProvider(companies.Object, entitlements.Object);

        Assert.False(provider.Enabled);
        var change = await provider.RequestPlanChangeAsync(1, "community", "/", CancellationToken.None);
        Assert.True(change.AppliedImmediately);
        Assert.Null(change.RedirectUrl);
        Assert.Null(await provider.GetPortalUrlAsync(1, "/", CancellationToken.None));

        var state = await provider.GetBillingStateAsync(1, CancellationToken.None);
        Assert.False(state.BillingEnabled);
        Assert.False(state.PortalAvailable);
        Assert.Equal("community", state.Plan);
    }
}
