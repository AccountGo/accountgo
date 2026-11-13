// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Core.Entitlements;
using Dto.Subscription;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

public class CompanyTiersTests
{
    [Theory]
    [InlineData("community")]
    [InlineData("enterprise")]
    [InlineData("anything_pro")]
    [InlineData("")]
    public void Derive_WithoutComp_IsFree_WhateverThePlanKey(string plan)
    {
        Assert.Equal(CompanyTiers.Free, CompanyTiers.Derive(null, plan));
        Assert.Equal(CompanyTiers.Free, CompanyTiers.Derive("pro", plan));
    }

    [Fact]
    public void Derive_HonoursCompOverride()
    {
        Assert.Equal(CompanyTiers.Comp, CompanyTiers.Derive(" Comp ", "community"));
    }

    [Fact]
    public void CommunityCatalog_SetsNoDisplayHighlights()
    {
        var plan = Assert.Single(new CommunityPlanCatalog().GetPlans());
        Assert.Null(plan.ProductLabel);
        Assert.Null(plan.Badge);
    }

    [Fact]
    public void PlanDefinition_CarriesOptionalDisplayFields()
    {
        var plan = new PlanDefinitionDto { Key = "k", Name = "K", ProductLabel = "Group", Badge = "Highlighted" };
        var json = System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"productLabel\":\"Group\"", json);
        Assert.Contains("\"badge\":\"Highlighted\"", json);
    }
}
