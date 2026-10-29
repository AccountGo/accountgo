// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Modules;

public sealed class CommunityPlanCatalogResolveTests
{
    private readonly CommunityPlanCatalog _catalog = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("accounting_free")]
    [InlineData("invoicing")]
    [InlineData("community")]
    public void TryResolvePlan_EveryKey_IsCommunity(string? key)
    {
        Assert.True(_catalog.TryResolvePlan(key, out var plan));
        Assert.Equal(CommunityPlanCatalog.PlanKey, plan.Key);
        Assert.Equal(CommunityPlanCatalog.AllModules, plan.IncludedModules);
        Assert.Equal(new[] { "sales" }, plan.RequiredModules);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("weird")]
    [InlineData("accounting_pro")]
    public void ResolveStoredPlan_NeverThrows_AndIsCommunity(string? key)
    {
        var plan = _catalog.ResolveStoredPlan(key);
        Assert.Equal(CommunityPlanCatalog.PlanKey, plan.Key);
        Assert.Equal("Community", plan.Name);
    }

    [Fact]
    public void GetPlans_ServesOneUnpricedPlan()
    {
        var plan = Assert.Single(_catalog.GetPlans());
        Assert.Equal(CommunityPlanCatalog.PlanKey, plan.Key);
        Assert.Null(plan.MonthlyPrice);
        Assert.False(plan.SelfService);
        Assert.Equal(
            CommunityPlanCatalog.AllModules.Count.ToString(CultureInfo.InvariantCulture),
            plan.IncludedModules.Count.ToString(CultureInfo.InvariantCulture));
    }
}
