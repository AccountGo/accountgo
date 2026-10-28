// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Billing;
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
/// E1a parity: old gate (SubscriptionAccess + E0.5 deny-code rule) must equal Evaluate(Module).
/// Expected-divergence list is intentionally empty — any failure is a real bug, not a documented gap.
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

    private static readonly string[] Modules =
    {
        "sales", "purchases", "financials", "reports", "inventory", "donations", "audit"
    };

    // Trimmed to two sets so the expanded axes stay under ~30s.
    private static readonly string?[] StoredModuleSets =
    {
        null,
        "[\"sales\",\"purchases\",\"financials\"]" // owner opted out of reports on accounting family
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

    public static IEnumerable<object[]> ParityCases()
    {
        foreach (var plan in Plans)
        foreach (var status in Statuses)
        foreach (var module in Modules)
        foreach (var stored in StoredModuleSets)
        foreach (var (periodLabel, periodEnd) in PeriodEnds)
        foreach (var tier in Tiers)
        foreach (var billingManaged in BillingManaged)
        {
            yield return new object[]
            {
                plan, status, module, stored ?? "", periodLabel, periodEnd!, tier ?? "", billingManaged
            };
        }
    }

    [Theory]
    [MemberData(nameof(ParityCases))]
    public void OldGate_Equals_Evaluate(
        string plan,
        string status,
        string module,
        string storedJson,
        string periodLabel,
        DateTime? periodEnd,
        string tier,
        bool billingManagedManually)
    {
        _ = periodLabel;
        var company = new Company
        {
            Id = 42,
            Name = "Parity",
            Plan = plan,
            SubscriptionStatus = status,
            CurrentPeriodEnd = periodEnd,
            ActiveModulesJson = string.IsNullOrEmpty(storedJson) ? null : storedJson,
            Tier = string.IsNullOrEmpty(tier) ? null : tier,
            BillingManagedManually = billingManagedManually
        };

        var oldAllowed = SubscriptionAccess.IsModuleAvailable(company, module, NowUtc);
        string? oldCode = null;
        if (!oldAllowed)
        {
            if (Company.IsPaidPlan(company.Plan) && SubscriptionAccess.IsInactive(company, NowUtc) && company.HasModule(module))
                oldCode = "SUBSCRIPTION_INACTIVE";
            else
                oldCode = "MODULE_NOT_SUBSCRIBED";
        }

        var engine = CreateEngine(company, NowUtc);
        var decision = engine.Evaluate(42, new EntitlementAction.Module(module));

        Assert.Equal(oldAllowed, decision.Allowed);
        Assert.Equal(oldCode, decision.Code);
    }

    [Fact]
    public void ShadowFailure_DoesNotChangeOldOutcome()
    {
        var company = new Company { Id = 7, Name = "X", Plan = "accounting_pro", SubscriptionStatus = "active" };
        var oldAllowed = SubscriptionAccess.IsModuleAvailable(company, "sales");
        Assert.True(oldAllowed);

        var throwing = new Mock<IEntitlementService>();
        throwing.SetupGet(e => e.Options).Returns(new EntitlementsOptions { EngineShadow = true });
        throwing.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Throws(new InvalidOperationException("shadow boom"));

        // Must not throw — old answer remains authoritative.
        EntitlementEngineShadow.CompareModuleAllow(throwing.Object, logger: null, 7, "sales", oldAllowed);
        Assert.True(oldAllowed);
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
        var tenant = new Mock<ITenantContext>();
        return new EntitlementService(
            new EntitlementsOptions { EngineShadow = false },
            companies.Object, members.Object, invites.Object, empty.Object, banks.Object, tenant.Object,
            new FixedTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedTimeProvider(DateTimeOffset utc) => _utc = utc;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
