// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Dto.Dashboard;
using Moq;
using Services.Dashboard;
using Services.Financial;
using Services.Purchasing;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>Phase 3H: dashboard money-in-bank split per bank account.</summary>
public class DashboardMoneyInBankTests
{
    private const int UnassignedCashAccountId = 10;
    private static readonly DateTime PostingDate = BankingTestHarness.Utc(2026, 3, 1);

    private static BankingTestHarness NewHarness()
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        return harness;
    }

    private static void PostBalance(BankingTestHarness harness, int accountId, decimal amount)
    {
        var line = harness.Post(accountId, DrOrCrSide.Dr, amount, PostingDate);
        harness.Repos.Accounts.GetById(accountId).GeneralLedgerLines.Add(line);
    }

    private static DashboardKpiDto BuildKpis(BankingTestHarness harness, bool canViewBanks = true)
    {
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        financial.Setup(f => f.GetAccounts()).Returns(() => harness.Repos.Accounts.Table.ToList());
        financial.Setup(f => f.GetCashAndBanks()).Returns(() => harness.Repos.Banks.Table.ToList());
        financial
            .Setup(f => f.MasterGeneralLedger(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<MasterGeneralLedger>());
        financial
            .Setup(f => f.BalanceSheet(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Core.Domain.AccountingBasis>()))
            .Returns(new List<BalanceSheet>());

        var permissions = canViewBanks
            ? new HashSet<string> { SystemPermissions.BankingAccountsView }
            : new HashSet<string>();
        var service = new DashboardService(
            financial.Object,
            new Mock<ISalesService>(MockBehavior.Loose).Object,
            new Mock<IPurchasingService>(MockBehavior.Loose).Object,
            harness.Banks,
            new BankingTestHarness.FakeTenant(1, permissions));
        return service.GetDashboardSummary().Kpis;
    }

    [Fact]
    public void SplitsMoneyInBankPerActiveAccount_AndTotalsMatch()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        var savings = harness.CreateBank("Savings");
        PostBalance(harness, checking.AccountId!.Value, 1250.50m);
        PostBalance(harness, savings.AccountId!.Value, 300m);
        PostBalance(harness, UnassignedCashAccountId, 49.50m);

        var kpis = BuildKpis(harness);

        Assert.Equal(new[] { "Checking", "Savings" }, kpis.BankBalances!.Select(b => b.Name).ToArray());
        Assert.Equal(1250.50m, kpis.BankBalances!.Single(b => b.BankId == checking.Id).Balance);
        Assert.Equal(300m, kpis.BankBalances!.Single(b => b.BankId == savings.Id).Balance);
        Assert.Equal(49.50m, kpis.UnassignedCash);
        Assert.Equal(1600m, kpis.MoneyInBank);
        Assert.Equal(kpis.BankBalances!.Sum(b => b.Balance) + kpis.UnassignedCash, kpis.MoneyInBank);
    }

    [Fact]
    public void ArchivedBankWithABalance_StaysInTheSplitAndTheTotal()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        var closed = harness.CreateBank("Closed account");
        PostBalance(harness, checking.AccountId!.Value, 500m);
        harness.Banks.Archive(closed.Id, includeFullNumbers: false);
        // Late postings can still land on an archived bank's ledger account; the money is there, so it counts.
        PostBalance(harness, closed.AccountId!.Value, 200m);

        var kpis = BuildKpis(harness);

        var archived = kpis.BankBalances!.Single(b => b.BankId == closed.Id);
        Assert.True(archived.IsArchived);
        Assert.Equal(200m, archived.Balance);
        Assert.False(kpis.BankBalances!.Single(b => b.BankId == checking.Id).IsArchived);
        Assert.Equal(0m, kpis.UnassignedCash);
        Assert.Equal(700m, kpis.MoneyInBank);
        Assert.Equal(kpis.BankBalances!.Sum(b => b.Balance) + kpis.UnassignedCash, kpis.MoneyInBank);
    }

    [Fact]
    public void ArchivedBankWithALateNegativeBalance_IsCountedToo()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        var closed = harness.CreateBank("Closed account");
        PostBalance(harness, checking.AccountId!.Value, 500m);
        harness.Banks.Archive(closed.Id, includeFullNumbers: false);
        // Late postings can still land on an archived bank's ledger account (here an overdraft).
        var overdraft = harness.Post(closed.AccountId!.Value, DrOrCrSide.Cr, 30m, PostingDate);
        harness.Repos.Accounts.GetById(closed.AccountId!.Value).GeneralLedgerLines.Add(overdraft);

        var kpis = BuildKpis(harness);

        Assert.Equal(-30m, kpis.BankBalances!.Single(b => b.BankId == closed.Id).Balance);
        Assert.Equal(470m, kpis.MoneyInBank);
    }

    [Fact]
    public void ArchivedBankWithNothingInIt_IsLeftOutOfTheSplit()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        var closed = harness.CreateBank("Closed account");
        PostBalance(harness, checking.AccountId!.Value, 500m);
        harness.Banks.Archive(closed.Id, includeFullNumbers: false);

        var kpis = BuildKpis(harness);

        Assert.DoesNotContain(kpis.BankBalances!, b => b.BankId == closed.Id);
        Assert.Equal(0m, kpis.UnassignedCash);
        Assert.Equal(500m, kpis.MoneyInBank);
    }

    [Fact]
    public void ArchivedBanksListAfterActiveOnes()
    {
        using var harness = NewHarness();
        var archived = harness.CreateBank("Alpha (closed)");
        var active = harness.CreateBank("Zulu");
        PostBalance(harness, active.AccountId!.Value, 20m);
        harness.Banks.Archive(archived.Id, includeFullNumbers: false);
        PostBalance(harness, archived.AccountId!.Value, 10m);

        var kpis = BuildKpis(harness);

        Assert.Equal(new[] { "Zulu", "Alpha (closed)" }, kpis.BankBalances!.Select(b => b.Name).ToArray());
    }

    [Fact]
    public void NoBanks_AllCashIsUnassigned()
    {
        using var harness = NewHarness();
        PostBalance(harness, UnassignedCashAccountId, 75m);

        var kpis = BuildKpis(harness);

        Assert.Empty(kpis.BankBalances!);
        Assert.Equal(75m, kpis.UnassignedCash);
        Assert.Equal(75m, kpis.MoneyInBank);
    }

    [Fact]
    public void WithoutBankViewPermission_HidesAccountNamesButKeepsTheTotal()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        PostBalance(harness, checking.AccountId!.Value, 120m);

        var kpis = BuildKpis(harness, canViewBanks: false);

        Assert.Null(kpis.BankBalances);
        Assert.Equal(120m, kpis.MoneyInBank);
    }

    [Fact]
    public void BalancesAreRoundedAwayFromZeroToCents()
    {
        using var harness = NewHarness();
        var checking = harness.CreateBank("Checking");
        PostBalance(harness, checking.AccountId!.Value, 10.005m);

        var kpis = BuildKpis(harness);

        Assert.Equal(10.01m, kpis.BankBalances!.Single().Balance);
        Assert.Equal(10.01m, kpis.MoneyInBank);
    }
}
