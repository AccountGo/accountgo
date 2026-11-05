// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Billing;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

/// <summary>Community engine behaviour for create/transfer workspace actions and /me shape.</summary>
public sealed class WorkspaceActivationEntitlementTests
{
    [Fact]
    public void Community_CreateWorkspace_AllowsWithoutCheckout()
    {
        var svc = Build();
        var decision = svc.EvaluateForUser("u1", new UserAction.CreateWorkspace("anything"));
        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresCheckout);
    }

    [Fact]
    public void Community_ReceiveWorkspace_Allows()
    {
        var svc = Build();
        var decision = svc.EvaluateForUser("u1", new UserAction.ReceiveWorkspace(42));
        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresCheckout);
    }

    private static IEntitlementService Build()
    {
        var companies = new List<Company>
        {
            new() { Id = 1, Name = "Co", Plan = CommunityPlanCatalog.PlanKey, ActivationState = CompanyActivationStates.Active }
        };
        return new CommunityEntitlementService(
            new EntitlementsOptions(),
            new CommunityPlanCatalog(),
            Repo(companies).Object,
            Repo(new List<CompanyMember>()).Object,
            Repo(new List<CompanyInvitation>()).Object,
            Repo(new List<EntitlementUsage>()).Object,
            Repo(new List<Core.Domain.Financials.Bank>()).Object,
            new Mock<ITenantContext>(MockBehavior.Strict).Object);
    }

    private static Mock<Core.Data.IRepository<T>> Repo<T>(List<T> data) where T : Core.Domain.BaseEntity
    {
        var mock = new Mock<Core.Data.IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        return mock;
    }
}
