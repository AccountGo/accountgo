// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Dashboard;
using Services.Financial;
using Services.Purchasing;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankCloseStatusTests
{
    private static DateTime D(int y, int m, int d) => BankingTestHarness.Utc(y, m, d);

    private static void AddReconciliation(
        BankingTestHarness harness,
        int bankId,
        DateTime statementEnd,
        BankReconciliationStatuses status = BankReconciliationStatuses.Finished)
    {
        harness.Repos.Reconciliations.Insert(new BankReconciliation
        {
            CompanyId = 1,
            BankId = bankId,
            StatementEndDate = statementEnd,
            Status = status,
            CreatedAt = statementEnd
        });
    }

    private static BankingTestHarness NewHarness()
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        return harness;
    }

    [Fact]
    public void CountsBanksReconciledThroughLastDayOfPreviousMonth()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        var savings = harness.CreateBank("Savings");
        AddReconciliation(harness, checking.Id, D(2026, 9, 30));
        AddReconciliation(harness, savings.Id, D(2026, 9, 29));

        var status = harness.Banks.GetCloseStatus(D(2026, 10, 15))!;

        Assert.Equal(D(2026, 9, 30), status.PeriodEnd);
        Assert.Equal("September 2026", status.PeriodLabel);
        Assert.Equal(1, status.ReconciledCount);
        Assert.Equal(2, status.TotalCount);
        Assert.False(status.IsComplete);
        Assert.True(status.Banks.Single(b => b.BankId == checking.Id).IsReconciled);
        Assert.False(status.Banks.Single(b => b.BankId == savings.Id).IsReconciled);
    }

    [Fact]
    public void MonthBoundary_FirstAndLastDayOfMonthUseTheSamePreviousMonth()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2026, 9, 30));

        Assert.Equal(1, harness.Banks.GetCloseStatus(D(2026, 10, 1))!.ReconciledCount);
        Assert.Equal(1, harness.Banks.GetCloseStatus(D(2026, 10, 31))!.ReconciledCount);
        // Rolling into November moves the target to October 31, which the September statement no longer covers.
        Assert.Equal(0, harness.Banks.GetCloseStatus(D(2026, 11, 1))!.ReconciledCount);
    }

    [Fact]
    public void JanuaryLooksBackToDecemberOfThePreviousYear()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2026, 12, 31));

        var status = harness.Banks.GetCloseStatus(D(2027, 1, 10))!;

        Assert.Equal(D(2026, 12, 31), status.PeriodEnd);
        Assert.Equal("December 2026", status.PeriodLabel);
        Assert.Equal(1, status.ReconciledCount);
        Assert.True(status.IsComplete);
    }

    [Fact]
    public void LeapYearFebruaryEnds_OnTheTwentyNinth()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2028, 2, 28));

        var status = harness.Banks.GetCloseStatus(D(2028, 3, 5))!;

        Assert.Equal(D(2028, 2, 29), status.PeriodEnd);
        Assert.Equal(0, status.ReconciledCount);
    }

    [Fact]
    public void StatementEndingAfterThePeriod_StillCounts()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2026, 10, 12));

        Assert.Equal(1, harness.Banks.GetCloseStatus(D(2026, 10, 20))!.ReconciledCount);
    }

    [Fact]
    public void UsesLatestFinished_IgnoringDraftAndUndone()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2026, 8, 31));
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Draft);
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Undone);

        var status = harness.Banks.GetCloseStatus(D(2026, 10, 5))!;

        Assert.Equal(0, status.ReconciledCount);
        Assert.Equal(D(2026, 8, 31), status.Banks.Single().ReconciledThrough);
    }

    [Fact]
    public void ArchivedBanksAreNotCounted()
    {
        using var harness = NewHarness();
        var active = harness.CreateBank("Checking");
        var archived = harness.CreateBank("Old");
        harness.Repos.Banks.GetById(archived.Id).ArchivedAt = D(2026, 1, 1);
        AddReconciliation(harness, active.Id, D(2026, 9, 30));

        var status = harness.Banks.GetCloseStatus(D(2026, 10, 5))!;

        Assert.Equal(1, status.TotalCount);
        Assert.Equal(1, status.ReconciledCount);
        Assert.True(status.IsComplete);
    }

    [Fact]
    public void NoActiveBanks_IsNotComplete()
    {
        using var harness = NewHarness();

        var status = harness.Banks.GetCloseStatus(D(2026, 10, 5))!;

        Assert.Equal(0, status.TotalCount);
        Assert.False(status.IsComplete);
    }

    [Fact]
    public void ReturnsNull_WhenFinancialsModuleIsOff()
    {
        using var harness = NewHarness();
        harness.CreateBank("Checking");
        harness.Entitlements
            .Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(false, null, null));

        Assert.Null(harness.Banks.GetCloseStatus(D(2026, 10, 5)));
    }

    [Fact]
    public void OtherTenantsReconciliations_AreIgnored()
    {
        using var harness = NewHarness();
        var bank = harness.CreateBank("Checking");
        AddReconciliation(harness, bank.Id, D(2026, 9, 30));
        harness.Repos.Reconciliations.Table.Single().CompanyId = 2;
        // The in-memory repository does not filter by tenant; the bank id stays company 1's.
        var other = harness.BuildServices(companyId: 2);

        var status = other.Banks.GetCloseStatus(D(2026, 10, 5))!;

        Assert.Equal(0, status.TotalCount);
    }

    private static DashboardService BuildDashboard(
        BankingTestHarness harness,
        IReadOnlySet<string> permissions)
    {
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        financial
            .Setup(f => f.MasterGeneralLedger(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<MasterGeneralLedger>());
        financial
            .Setup(f => f.BalanceSheet(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Core.Domain.AccountingBasis>()))
            .Returns(new List<BalanceSheet>());
        var sales = new Mock<ISalesService>(MockBehavior.Loose);
        var purchasing = new Mock<IPurchasingService>(MockBehavior.Loose);
        return new DashboardService(
            financial.Object,
            sales.Object,
            purchasing.Object,
            harness.Banks,
            new BankingTestHarness.FakeTenant(1, permissions));
    }

    [Fact]
    public void Dashboard_IncludesCloseStatus_ForBankViewers()
    {
        using var harness = NewHarness();
        harness.CreateBank("Checking");

        var summary = BuildDashboard(harness, new HashSet<string> { SystemPermissions.BankingAccountsView })
            .GetDashboardSummary();

        Assert.NotNull(summary.BankCloseStatus);
        Assert.Equal(1, summary.BankCloseStatus!.TotalCount);
    }

    [Fact]
    public void Dashboard_OmitsCloseStatus_WithoutBankViewPermission()
    {
        using var harness = NewHarness();
        harness.CreateBank("Checking");

        var summary = BuildDashboard(harness, new HashSet<string>()).GetDashboardSummary();

        Assert.Null(summary.BankCloseStatus);
    }
}
