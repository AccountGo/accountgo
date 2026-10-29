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
using Moq;
using Services.Billing;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

public sealed class CommunityEntitlementServiceContractTests : EntitlementServiceContractTests
{
    protected override IEntitlementService CreateService(ContractHarness harness)
    {
        ApplyHarnessFreeLimits(harness);
        var tenant = harness.Tenant ?? new Mock<ITenantContext>(MockBehavior.Strict);
        return new CommunityEntitlementService(
            harness.Options,
            new CommunityPlanCatalog(),
            Repo(harness.Companies).Object,
            Repo(harness.Members).Object,
            Repo(harness.Invitations).Object,
            UsageRepo(harness.Usage).Object,
            Repo(harness.Banks).Object,
            tenant.Object,
            harness.Time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown_plan")]
    [InlineData("accounting_free")]
    [InlineData("invoicing")]
    [InlineData("enterprise")]
    public void AnyStoredPlanKey_YieldsAllModulesMinusOptOuts(string? plan)
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = plan,
            DisabledModulesJson = "[\"inventory\"]"
        };
        var svc = CreateService(new EntitlementsOptions { Enabled = true }, company);
        var snap = svc.Get(1);

        Assert.Equal(CommunityPlanCatalog.PlanKey, snap.Plan);
        Assert.Equal(CommunityPlanCatalog.PlanKey, snap.EffectivePlan);
        Assert.False(snap.Limited);
        Assert.Empty(snap.Features);
        Assert.Empty(snap.AllowedTransitions);
        Assert.False(svc.Evaluate(1, new EntitlementAction.Module("sales")).UpgradeAvailable);
        Assert.Null(snap.Lifecycle);
        Assert.DoesNotContain("inventory", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sales", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("financials", snap.Modules, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(snap.Version));
    }

    [Fact]
    public void RequiredModule_CannotBeOptedOut()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "community",
            DisabledModulesJson = "[\"sales\",\"inventory\"]"
        };
        var svc = CreateService(new EntitlementsOptions(), company);
        Assert.Contains("sales", svc.Get(1).Modules, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory", svc.Get(1).Modules, StringComparer.OrdinalIgnoreCase);
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        mock.Setup(r => r.GetById(It.IsAny<object>())).Returns((object id) =>
            data.FirstOrDefault(e => e.Id.Equals(Convert.ToInt32(id)))!);
        return mock;
    }

    private static Mock<IRepository<EntitlementUsage>> UsageRepo(List<EntitlementUsage> data)
    {
        var mock = Repo(data);
        mock.Setup(r => r.Insert(It.IsAny<EntitlementUsage>())).Callback<EntitlementUsage>(u =>
        {
            u.Id = data.Count + 1;
            data.Add(u);
        });
        return mock;
    }
}
