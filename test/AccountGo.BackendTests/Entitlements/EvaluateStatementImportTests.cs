// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
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

public class EvaluateStatementImportTests
{
    [Fact]
    public void CommunityEdition_IsAllowed_EvenWithoutTheFeatureKey()
    {
        var company = Company(1, CommunityPlanCatalog.PlanKey, disabledModules: "[]");
        var service = CommunityService(company);

        var decision = StatementImportAccess.Evaluate(service, 1);

        Assert.True(decision.Allowed);
        Assert.False(service.Get(1).HasFeature(FeatureKeys.BankingStatementImport));
    }

    [Fact]
    public void CommunityEdition_WithFinancialsOptedOut_IsStillAllowed()
    {
        var company = Company(1, CommunityPlanCatalog.PlanKey, disabledModules: "[\"financials\"]");
        var service = CommunityService(company);

        Assert.True(StatementImportAccess.Evaluate(service, 1).Allowed);
        Assert.DoesNotContain("financials", service.Get(1).Modules);
    }

    [Fact]
    public void HostedStyleDeny_WhenNoFinancialsNoFeatureNotCommunity()
    {
        // Community engine always uses plan "community"; for the compound gate's feature/module
        // branches we stub Evaluate/Get via a thin fake.
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) => action switch
            {
                EntitlementAction.Module => new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "no financials",
                    RequiredModule: "financials", EffectivePlan: "invoicing_free", UpgradeAvailable: true),
                EntitlementAction.Feature => new EntitlementDecision(false, "FEATURE_NOT_ENTITLED", "no import",
                    RequiredFeature: FeatureKeys.BankingStatementImport, EffectivePlan: "invoicing_free",
                    UpgradeAvailable: true),
                _ => new EntitlementDecision(false, "DENIED", "unexpected")
            });
        entitlements.Setup(e => e.Get(1)).Returns(new EntitlementSnapshot(
            Enabled: true, Plan: "invoicing_free", Tier: "free", Limited: true,
            Modules: new[] { "sales", "purchases" }, SelfServicePlans: new[] { "invoicing_pro" },
            Usage: System.Array.Empty<ResourceUsage>(), SupportEmail: null,
            Features: ImmutableHashSet<string>.Empty, EffectivePlan: "invoicing_free"));

        var decision = StatementImportAccess.Evaluate(entitlements.Object, 1);

        Assert.False(decision.Allowed);
        Assert.Equal("FEATURE_NOT_ENTITLED", decision.Code);
        Assert.Equal(FeatureKeys.BankingStatementImport, decision.RequiredFeature);
        Assert.True(decision.UpgradeAvailable);
    }

    [Fact]
    public void HostedStyleAllow_ViaFeatureKeyWithoutFinancials()
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) => action switch
            {
                EntitlementAction.Module => new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "no financials",
                    RequiredModule: "financials", EffectivePlan: "invoicing_pro"),
                EntitlementAction.Feature => new EntitlementDecision(true, null, null,
                    RequiredFeature: FeatureKeys.BankingStatementImport, EffectivePlan: "invoicing_pro"),
                _ => new EntitlementDecision(false, "DENIED", "unexpected")
            });
        entitlements.Setup(e => e.Get(1)).Returns(new EntitlementSnapshot(
            Enabled: true, Plan: "invoicing_pro", Tier: "pro", Limited: true,
            Modules: new[] { "sales", "purchases" }, SelfServicePlans: new[] { "invoicing_pro" },
            Usage: System.Array.Empty<ResourceUsage>(), SupportEmail: null,
            Features: ImmutableHashSet.Create(FeatureKeys.BankingStatementImport),
            EffectivePlan: "invoicing_pro"));

        Assert.True(StatementImportAccess.Evaluate(entitlements.Object, 1).Allowed);
    }

    private static Company Company(int id, string plan, string disabledModules) => new()
    {
        Id = id,
        Name = "Co",
        Plan = plan,
        SubscriptionStatus = "active",
        DisabledModulesJson = disabledModules
    };

    private static CommunityEntitlementService CommunityService(Company company)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(company.Id);
        return new CommunityEntitlementService(
            new EntitlementsOptions(),
            new CommunityPlanCatalog(),
            Repo(new List<Company> { company }).Object,
            Repo(new List<CompanyMember>()).Object,
            Repo(new List<CompanyInvitation>()).Object,
            Repo(new List<EntitlementUsage>()).Object,
            Repo(new List<Bank>()).Object,
            tenant.Object);
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        return mock;
    }
}
