// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain;
using Dto.Subscription;
using Moq;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Modules;

/// <summary>
/// Active modules come from IPlanCatalog IncludedModules minus opt-outs — not from Company helpers.
/// Uses neutral fake plan keys only.
/// </summary>
public sealed class PlanModuleResolverTests
{
    [Theory]
    [InlineData("plan_a", false)]
    [InlineData("plan_a", true)]
    [InlineData("plan_b", false)]
    [InlineData("plan_b", true)]
    public void ResolveActiveModules_UsesCatalogIncludedMinusOptOuts(string planKey, bool withOptOut)
    {
        var planA = new PlanDefinitionDto
        {
            Key = "plan_a",
            Name = "Plan A",
            IncludedModules = new List<string> { "sales", "alpha", "beta" },
            RequiredModules = new List<string> { "sales" }
        };
        var planB = new PlanDefinitionDto
        {
            Key = "plan_b",
            Name = "Plan B",
            IncludedModules = new List<string> { "sales", "gamma" },
            RequiredModules = new List<string> { "sales" }
        };

        var catalog = new Mock<IPlanCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.ResolveStoredPlan("plan_a")).Returns(planA);
        catalog.Setup(c => c.ResolveStoredPlan("plan_b")).Returns(planB);

        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = planKey,
            DisabledModulesJson = withOptOut ? "[\"beta\",\"gamma\"]" : "[]"
        };

        var active = PlanModuleResolver.ResolveActiveModules(catalog.Object, company);
        var expectedPlan = planKey == "plan_a" ? planA : planB;

        foreach (var required in expectedPlan.RequiredModules)
            Assert.Contains(required, active, StringComparer.OrdinalIgnoreCase);

        if (withOptOut)
        {
            Assert.DoesNotContain("beta", active, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("gamma", active, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(expectedPlan.IncludedModules, active);
        }
    }
}
