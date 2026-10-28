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

public class EntitlementEnginePipelineTests
{
    private static readonly DateTime GraceAnchor =
        DateTime.Parse("2026-06-15T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    [Fact]
    public void Comp_OverCanceled_KeepsStoredPlanModules()
    {
        var company = Company(1, "enterprise", "canceled", tier: "comp",
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company);

        var inventory = engine.Evaluate(1, new EntitlementAction.Module("inventory"));
        Assert.True(inventory.Allowed);
        Assert.Null(inventory.Code);
    }

    [Fact]
    public void BillingManagedManually_OverCanceled_KeepsStoredPlanModules()
    {
        var company = Company(1, "accounting_pro", "canceled", billingManagedManually: true);
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("financials")).Allowed);
    }

    [Fact]
    public void FreeStoredPlan_IsNotLifecycleReduced()
    {
        var company = Company(1, "accounting_free", "canceled");
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("reports")).Allowed);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    public void ActiveOrTrialing_KeepsPaidModules(string status)
    {
        var company = Company(1, "enterprise", status,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
    }

    [Fact]
    public void PastDue_WithinGrace_KeepsPaidModules()
    {
        var periodEnd = GraceAnchor.AddDays(-3);
        var company = Company(1, "enterprise", "past_due", periodEnd: periodEnd,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company, utcNow: GraceAnchor);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
    }

    [Fact]
    public void PastDue_GraceBoundary_PlusOneSecond_IsReduced()
    {
        var periodEnd = GraceAnchor;
        var company = Company(1, "enterprise", "past_due", periodEnd: periodEnd,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var justAfterGrace = periodEnd.AddDays(14).AddSeconds(1);
        var engine = CreateEngine(company, utcNow: justAfterGrace);
        var decision = engine.Evaluate(1, new EntitlementAction.Module("inventory"));
        Assert.False(decision.Allowed);
        Assert.Equal("SUBSCRIPTION_INACTIVE", decision.Code);
    }

    [Fact]
    public void PastDue_GraceBoundary_Inclusive_KeepsPaidModules()
    {
        var periodEnd = GraceAnchor;
        var company = Company(1, "enterprise", "past_due", periodEnd: periodEnd,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var atGraceEnd = periodEnd.AddDays(14);
        var engine = CreateEngine(company, utcNow: atGraceEnd);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
    }

    [Fact]
    public void PastDue_NullPeriodEnd_IsReduced()
    {
        var company = Company(1, "enterprise", "past_due", periodEnd: null,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company);
        Assert.Equal("SUBSCRIPTION_INACTIVE",
            engine.Evaluate(1, new EntitlementAction.Module("inventory")).Code);
    }

    [Fact]
    public void PastDue_UnspecifiedPeriodEnd_TreatedAsUtc()
    {
        var periodEnd = DateTime.SpecifyKind(GraceAnchor.AddDays(-3), DateTimeKind.Unspecified);
        var company = Company(1, "enterprise", "past_due", periodEnd: periodEnd,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company, utcNow: GraceAnchor);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData("incomplete_expired")]
    [InlineData("incomplete")]
    [InlineData("paused")]
    [InlineData("weird_status")]
    public void TerminalStatuses_ReducePaidModules(string status)
    {
        var company = Company(1, "enterprise", status,
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\"]");
        var engine = CreateEngine(company);
        var inventory = engine.Evaluate(1, new EntitlementAction.Module("inventory"));
        Assert.False(inventory.Allowed);
        Assert.Equal("SUBSCRIPTION_INACTIVE", inventory.Code);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("sales")).Allowed);
    }

    [Fact]
    public void UnknownPlanKey_DefaultsToAccountingFree_Modules()
    {
        var company = Company(1, "not_a_real_plan", "active");
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("financials")).Allowed);
        Assert.False(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        Assert.Equal("MODULE_NOT_SUBSCRIBED",
            engine.Evaluate(1, new EntitlementAction.Module("inventory")).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankOrWhitespaceStatus_TreatedAsActive_KeepsEnterpriseModules(string status)
    {
        var company = Company(1, "enterprise", status);
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("donations")).Allowed);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("audit")).Allowed);
    }

    [Fact]
    public void UnrecognizedStatus_FallsToAccountingFree()
    {
        var company = Company(1, "enterprise", "weird_status",
            modulesJson: "[\"sales\",\"purchases\",\"financials\",\"reports\",\"inventory\",\"donations\",\"audit\"]");
        var engine = CreateEngine(company);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("sales")).Allowed);
        Assert.True(engine.Evaluate(1, new EntitlementAction.Module("financials")).Allowed);
        Assert.False(engine.Evaluate(1, new EntitlementAction.Module("inventory")).Allowed);
        Assert.Equal("SUBSCRIPTION_INACTIVE",
            engine.Evaluate(1, new EntitlementAction.Module("inventory")).Code);
    }

    [Fact]
    public void Lapsed_OwnerOptedOutModule_IsModuleNotSubscribed()
    {
        var company = Company(1, "accounting_pro", "canceled",
            modulesJson: "[\"sales\",\"purchases\",\"financials\"]");
        var engine = CreateEngine(company);
        var reports = engine.Evaluate(1, new EntitlementAction.Module("reports"));
        Assert.False(reports.Allowed);
        Assert.Equal("MODULE_NOT_SUBSCRIBED", reports.Code);
    }

    [Fact]
    public void ModuleDecision_DoesNotQueryUsageMembersInvitationsOrBanks()
    {
        var company = Company(1, "accounting_pro", "active");
        var usageHits = 0;
        var memberHits = 0;
        var inviteHits = 0;
        var bankHits = 0;

        var companies = Repo(new List<Company> { company });
        var usage = CountingRepo<EntitlementUsage>(() => usageHits++);
        var members = CountingRepo<CompanyMember>(() => memberHits++);
        var invites = CountingRepo<CompanyInvitation>(() => inviteHits++);
        var banks = CountingRepo<Bank>(() => bankHits++);
        var tenant = new Mock<ITenantContext>();
        var engine = new EntitlementService(
            new EntitlementsOptions(), companies.Object, members.Object, invites.Object,
            usage.Object, banks.Object, tenant.Object);

        _ = engine.Evaluate(1, new EntitlementAction.Module("sales", "financials"));

        Assert.Equal(0, usageHits);
        Assert.Equal(0, memberHits);
        Assert.Equal(0, inviteHits);
        Assert.Equal(0, bankHits);
    }

    private static Company Company(
        int id,
        string plan,
        string status,
        string? tier = null,
        bool billingManagedManually = false,
        DateTime? periodEnd = null,
        string? modulesJson = null,
        string? disabledJson = null) =>
        new()
        {
            Id = id,
            Name = "Test",
            Plan = plan,
            Tier = tier,
            SubscriptionStatus = status,
            BillingManagedManually = billingManagedManually,
            CurrentPeriodEnd = periodEnd,
            // Prefer DisabledModulesJson; modulesJson is legacy ActiveModulesJson fallback only.
            DisabledModulesJson = disabledJson ?? (modulesJson == null ? "[]" : null),
            ActiveModulesJson = modulesJson
        };

    private static EntitlementService CreateEngine(Company company, DateTime? utcNow = null)
    {
        var companies = Repo(new List<Company> { company });
        var emptyUsage = Repo(new List<EntitlementUsage>());
        var emptyMembers = Repo(new List<CompanyMember>());
        var emptyInvites = Repo(new List<CompanyInvitation>());
        var emptyBanks = Repo(new List<Bank>());
        var tenant = new Mock<ITenantContext>();
        TimeProvider? time = utcNow == null ? null : new FixedTimeProvider(new DateTimeOffset(utcNow.Value, TimeSpan.Zero));
        return new EntitlementService(
            new EntitlementsOptions { EngineShadow = false },
            companies.Object, emptyMembers.Object, emptyInvites.Object,
            emptyUsage.Object, emptyBanks.Object, tenant.Object, time);
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        mock.Setup(r => r.GetById(It.IsAny<object>())).Returns((object id) =>
            data.FirstOrDefault(e => e.Id.Equals(Convert.ToInt32(id, CultureInfo.InvariantCulture)))!);
        return mock;
    }

    private static Mock<IRepository<T>> CountingRepo<T>(Action onTable) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() =>
        {
            onTable();
            return new List<T>().AsQueryable();
        });
        return mock;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedTimeProvider(DateTimeOffset utc) => _utc = utc;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
