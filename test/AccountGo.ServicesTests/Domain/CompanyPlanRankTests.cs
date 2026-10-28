// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain;
using Xunit;

namespace AccountGo.ServicesTests.Domain;

public class CompanyPlanRankTests
{
    [Theory]
    [InlineData("invoicing_free", "invoicing_pro", true)]
    [InlineData("invoicing_pro", "accounting_pro", true)]
    [InlineData("accounting_free", "accounting_pro", true)]
    [InlineData("invoicing_free", "accounting_pro", true)]
    [InlineData("accounting_pro", "invoicing_pro", false)]
    [InlineData("accounting_free", "invoicing_pro", false)]
    [InlineData("accounting_pro", "accounting_pro", false)]
    [InlineData("enterprise", "accounting_pro", false)]
    public void IsUpgrade_FollowsCapabilityLadder(string from, string to, bool expected) =>
        Assert.Equal(expected, Company.IsUpgrade(from, to));

    [Theory]
    // Allowed self-serve edges
    [InlineData("accounting_free", "accounting_pro", true)]
    [InlineData("accounting_free", "invoicing_free", true)]
    [InlineData("invoicing_free", "invoicing_pro", true)]
    [InlineData("invoicing_free", "accounting_free", true)]
    [InlineData("invoicing_pro", "accounting_pro", true)]
    // Cancel / downgrade is portal-only — not a self-serve plan change
    [InlineData("accounting_pro", "accounting_free", false)]
    [InlineData("invoicing_pro", "invoicing_free", false)]
    // Blocked product jumps
    [InlineData("accounting_pro", "invoicing_pro", false)]
    [InlineData("accounting_pro", "invoicing_free", false)]
    [InlineData("accounting_free", "invoicing_pro", false)]
    [InlineData("invoicing_free", "accounting_pro", false)]
    [InlineData("invoicing_pro", "accounting_free", false)]
    public void IsAllowedSelfServicePlanChange_CapabilityLadder(string from, string to, bool expected) =>
        Assert.Equal(expected, Company.IsAllowedSelfServicePlanChange(from, to));

    [Fact]
    public void CancelLandingPlan_SameProductFree()
    {
        Assert.Equal("accounting_free", Company.CancelLandingPlan("accounting_pro"));
        Assert.Equal("invoicing_free", Company.CancelLandingPlan("invoicing_pro"));
    }

    [Fact]
    public void PlanRank_OrdersProducts()
    {
        Assert.True(Company.PlanRank("invoicing_free") < Company.PlanRank("invoicing_pro"));
        Assert.True(Company.PlanRank("invoicing_pro") < Company.PlanRank("accounting_free"));
        Assert.True(Company.PlanRank("accounting_free") < Company.PlanRank("accounting_pro"));
        Assert.True(Company.PlanRank("accounting_pro") < Company.PlanRank("enterprise"));
    }
}
