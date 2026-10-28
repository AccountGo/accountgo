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
/// Hand-written expected table for §6.2 rows a–h plus read-only / opt-out cases.
/// Expectations are literals — not derived from engine-like helpers.
/// </summary>
public class EntitlementEngineDeterminationTests
{
    private static readonly DateTime Now =
        DateTime.Parse("2026-06-20T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static IEnumerable<object[]> LifecycleTable()
    {
        // plan, status, tier, billingManual, periodEndLabel, module, access, expectAllow, expectCode, expectReadOnlyContains
        // a) comp over canceled keeps enterprise inventory (write)
        yield return Row("enterprise", "canceled", "comp", false, "null", "inventory", ModuleAccess.Write, true, null, false);
        // a) BillingManagedManually over canceled
        yield return Row("accounting_pro", "canceled", null, true, "null", "financials", ModuleAccess.Write, true, null, false);
        // b) free stored plan not reduced
        yield return Row("accounting_free", "canceled", null, false, "null", "reports", ModuleAccess.Write, true, null, false);
        // c) active / trialing / blank
        yield return Row("enterprise", "active", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
        yield return Row("enterprise", "trialing", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
        yield return Row("enterprise", "", null, false, "null", "inventory", ModuleAccess.Write, true, null, false);
        yield return Row("enterprise", "   ", null, false, "null", "audit", ModuleAccess.Write, true, null, false);
        // d) past_due within grace
        yield return Row("enterprise", "past_due", null, false, "within", "inventory", ModuleAccess.Write, true, null, false);
        // e) past_due beyond grace → read-only inventory
        yield return Row("enterprise", "past_due", null, false, "beyond", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
        yield return Row("enterprise", "past_due", null, false, "beyond", "inventory", ModuleAccess.Read, true, null, true);
        yield return Row("enterprise", "past_due", null, false, "beyond", "sales", ModuleAccess.Write, true, null, false);
        // f/g) terminal unpaid statuses
        yield return Row("enterprise", "canceled", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
        yield return Row("enterprise", "unpaid", null, false, "null", "donations", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
        yield return Row("enterprise", "incomplete", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
        // h) unrecognized
        yield return Row("enterprise", "weird_status", null, false, "null", "inventory", ModuleAccess.Write, false, "SUBSCRIPTION_INACTIVE", true);
        // opt-out: reports disabled on accounting_pro → MODULE_NOT_SUBSCRIBED even when canceled
        yield return Row("accounting_pro", "canceled", null, false, "null", "reports", ModuleAccess.Write, false, "MODULE_NOT_SUBSCRIBED", false, disabled: "[\"reports\"]");
        // upgrade grant: invoicing → accounting with empty disabled ⇒ financials
        yield return Row("accounting_free", "active", null, false, "null", "financials", ModuleAccess.Write, true, null, false, disabled: "[]");
    }

    private static object[] Row(
        string plan, string status, string? tier, bool billingManual, string periodLabel,
        string module, ModuleAccess access, bool allow, string? code, bool readOnlyContains,
        string? disabled = null) =>
        new object[] { plan, status, tier ?? "", billingManual, periodLabel, module, access, allow, code ?? "", readOnlyContains, disabled ?? "" };

    [Theory]
    [MemberData(nameof(LifecycleTable))]
    public void HandWrittenTable_MatchesEvaluate(
        string plan, string status, string tier, bool billingManual, string periodLabel,
        string module, ModuleAccess access, bool expectAllow, string expectCode, bool expectReadOnlyContains,
        string disabledJson)
    {
        DateTime? periodEnd = periodLabel switch
        {
            "within" => Now.AddDays(-3),
            "beyond" => Now.AddDays(-20),
            _ => null
        };

        var company = new Company
        {
            Id = 1,
            Name = "T",
            Plan = plan,
            SubscriptionStatus = status,
            Tier = string.IsNullOrEmpty(tier) ? null : tier,
            BillingManagedManually = billingManual,
            CurrentPeriodEnd = periodEnd,
            DisabledModulesJson = string.IsNullOrEmpty(disabledJson) ? null : disabledJson
        };

        var engine = CreateEngine(company, Now);
        var decision = engine.Evaluate(1, new EntitlementAction.Module(access, module));
        var snapshot = engine.Get(1);

        Assert.Equal(expectAllow, decision.Allowed);
        Assert.Equal(string.IsNullOrEmpty(expectCode) ? null : expectCode, decision.Code);
        Assert.Equal(expectReadOnlyContains, snapshot.ReadOnlyModules.Contains(module, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvoicingToAccounting_EmptyDisabled_GrantsFinancials()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Upgrade",
            Plan = "accounting_free",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]",
            ActiveModulesJson = "[\"sales\",\"purchases\"]" // stale allow-list must not hide financials
        };
        var engine = CreateEngine(company, Now);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("financials")).Allowed);
        Assert.Contains("financials", engine.Get(1).Modules);
    }

    private static EntitlementService CreateEngine(Company company, DateTime utcNow)
    {
        var companies = new Mock<IRepository<Company>>();
        companies.Setup(r => r.Table).Returns(new List<Company> { company }.AsQueryable());
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
