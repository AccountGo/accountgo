// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

/// <summary>
/// E1b property checks over the E1a axis grid. Expectations are invariants, not engine-derived oracles.
/// </summary>
public class EntitlementEngineShadowParityTests
{
    private static readonly DateTime NowUtc =
        DateTime.Parse("2026-06-20T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static readonly string[] Plans =
    {
        "invoicing_free", "invoicing_pro", "accounting_free", "accounting_pro", "enterprise",
        "invoicing", "accounting", "not_a_real_plan"
    };

    private static readonly string[] Statuses =
    {
        "active", "trialing", "past_due", "canceled", "unpaid", "incomplete",
        "incomplete_expired", "paused", "weird_status", ""
    };

    private static readonly string?[] DisabledSets =
    {
        null,
        "[]",
        "[\"reports\"]"
    };

    private static readonly (string Label, DateTime? Value)[] PeriodEnds =
    {
        ("null", null),
        ("within_grace", NowUtc.AddDays(-3)),
        ("beyond_grace", NowUtc.AddDays(-20)),
        ("unspecified_kind", DateTime.SpecifyKind(NowUtc.AddDays(-3), DateTimeKind.Unspecified))
    };

    private static readonly string?[] Tiers = { null, "free", "pro", "comp" };
    private static readonly bool[] BillingManaged = { false, true };

    public static IEnumerable<object[]> AxisCases()
    {
        foreach (var plan in Plans)
        foreach (var status in Statuses)
        foreach (var disabled in DisabledSets)
        foreach (var (periodLabel, periodEnd) in PeriodEnds)
        foreach (var tier in Tiers)
        foreach (var billingManaged in BillingManaged)
        {
            yield return new object[]
            {
                plan, status, disabled ?? "", periodLabel, periodEnd!, tier ?? "", billingManaged
            };
        }
    }

    [Theory]
    [MemberData(nameof(AxisCases))]
    public void PropertyInvariants_Hold(
        string plan, string status, string disabledJson, string periodLabel, DateTime? periodEnd,
        string tier, bool billingManagedManually)
    {
        _ = periodLabel;
        var company = new Company
        {
            Id = 42,
            Name = "Props",
            Plan = plan,
            SubscriptionStatus = status,
            CurrentPeriodEnd = periodEnd,
            DisabledModulesJson = string.IsNullOrEmpty(disabledJson) ? null : disabledJson,
            Tier = string.IsNullOrEmpty(tier) ? null : tier,
            BillingManagedManually = billingManagedManually
        };

        var engine = CreateEngine(company, NowUtc);
        var snapshot = engine.Get(42);
        var storedPlanModules = Company.ModulesForPlan(Company.NormalizePlanKey(
            Company.IsKnownPlan(plan) ? plan : "accounting_free"));

        // Comp and free plans are never lifecycle-reduced.
        var isCompOrManual = string.Equals(tier, "comp", StringComparison.OrdinalIgnoreCase) || billingManagedManually;
        var normalized = Company.NormalizePlanKey(Company.IsKnownPlan(plan) ? plan : "accounting_free");
        var isFree = !normalized.EndsWith("_pro", StringComparison.Ordinal) && normalized != "enterprise";
        if (isCompOrManual || isFree)
            Assert.Null(snapshot.Lifecycle?.ReducedBy);

        // ReadOnlyModules ⊆ stored-plan modules (after owner toggles via GetActiveModules).
        var storedActive = company.GetActiveModules();
        Assert.All(snapshot.ReadOnlyModules, m => Assert.Contains(m, storedActive, StringComparer.OrdinalIgnoreCase));

        // Modules ∩ ReadOnlyModules = ∅
        Assert.Empty(snapshot.Modules.Intersect(snapshot.ReadOnlyModules, StringComparer.OrdinalIgnoreCase));

        // Required modules are always in Modules.
        foreach (var required in storedPlanModules.Where(m => Company.IsRequiredModule(company.Plan, m)))
        {
            if (storedActive.Contains(required, StringComparer.OrdinalIgnoreCase))
                Assert.Contains(required, snapshot.Modules, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ShadowFailure_DoesNotChangeOldOutcome()
    {
        var throwing = new Mock<IEntitlementService>();
        throwing.SetupGet(e => e.Options).Returns(new EntitlementsOptions { EngineShadow = true, LegacyModuleGate = true });
        throwing.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Throws(new InvalidOperationException("shadow boom"));

        EntitlementEngineShadow.CompareModuleAllow(throwing.Object, logger: null, 7, "sales", oldAllowed: true);
    }

    private static EntitlementService CreateEngine(Company company, DateTime utcNow)
    {
        var list = new List<Company> { company };
        var companies = new Mock<IRepository<Company>>();
        companies.Setup(r => r.Table).Returns(() => list.AsQueryable());
        var empty = new Mock<IRepository<EntitlementUsage>>();
        empty.Setup(r => r.Table).Returns(new List<EntitlementUsage>().AsQueryable());
        var members = new Mock<IRepository<CompanyMember>>();
        members.Setup(r => r.Table).Returns(new List<CompanyMember>().AsQueryable());
        var invites = new Mock<IRepository<CompanyInvitation>>();
        invites.Setup(r => r.Table).Returns(new List<CompanyInvitation>().AsQueryable());
        var banks = new Mock<IRepository<Bank>>();
        banks.Setup(r => r.Table).Returns(new List<Bank>().AsQueryable());
        return new EntitlementService(
            new EntitlementsOptions { EngineShadow = false },
            companies.Object, members.Object, invites.Object, empty.Object, banks.Object,
            new Mock<ITenantContext>().Object,
            new FixedTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedTimeProvider(DateTimeOffset utc) => _utc = utc;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
