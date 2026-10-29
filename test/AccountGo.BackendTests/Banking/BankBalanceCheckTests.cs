// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankBalanceCheckTests
{
    private static DateTime Today => DateTime.UtcNow.Date;

    private static BankingTestHarness NewInvoicingHarness(out int bankId, out int accountId, bool financialsOn = false)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var created = harness.CreateBank("Checking");
        bankId = created.Id;
        accountId = created.AccountId!.Value;
        harness.Entitlements
            .Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) =>
            {
                var module = Assert.IsType<EntitlementAction.Module>(action);
                var allowed = financialsOn || !module.AnyOf.Contains("financials");
                return allowed
                    ? new EntitlementDecision(true, null, null)
                    : new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "denied");
            });
        return harness;
    }

    [Fact]
    public void Matching_Balance_ReportsMatch_AndPostsNothing()
    {
        using var harness = NewInvoicingHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 500m, Today.AddDays(-5));
        harness.Post(accountId, DrOrCrSide.Cr, 120.50m, Today.AddDays(-2));
        var headersBefore = harness.Repos.GlHeaders.Table.Count();
        var linesBefore = harness.Repos.GlLines.Table.Count();

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 379.50m });

        Assert.True(result.IsMatch);
        Assert.Equal(0m, result.Difference);
        Assert.Equal(379.50m, result.BookBalance);
        Assert.Equal(2, result.RecentLines.Count);
        Assert.Equal(headersBefore, harness.Repos.GlHeaders.Table.Count());
        Assert.Equal(linesBefore, harness.Repos.GlLines.Table.Count());
    }

    [Fact]
    public void Difference_IsBankMinusBooks_AndFlagsLinesWithTheSameAmount()
    {
        using var harness = NewInvoicingHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 500m, Today.AddDays(-5));
        var suspect = harness.Post(accountId, DrOrCrSide.Dr, 75m, Today.AddDays(-1));

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 500m });

        Assert.False(result.IsMatch);
        Assert.Equal(-75m, result.Difference);
        Assert.Contains("less than your books", result.Message);
        Assert.True(result.RecentLines.Single(l => l.LineId == suspect.Id).MayExplainDifference);
        Assert.False(result.RecentLines.Single(l => l.LineId != suspect.Id).MayExplainDifference);
    }

    [Fact]
    public void BankHigherThanBooks_ExplainsTheOtherDirection()
    {
        using var harness = NewInvoicingHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 100m, Today.AddDays(-3));

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 130.255m });

        Assert.Equal(30.26m, result.Difference);
        Assert.Contains("more than your books", result.Message);
    }

    [Fact]
    public void AsOfDate_ExcludesLaterActivity()
    {
        using var harness = NewInvoicingHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 100m, Today.AddDays(-10));
        harness.Post(accountId, DrOrCrSide.Dr, 40m, Today.AddDays(-2));

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest
        {
            AsOfBalance = 100m,
            AsOfDate = Today.AddDays(-5)
        });

        Assert.True(result.IsMatch);
        Assert.Equal(Today.AddDays(-5), result.AsOfDate);
        Assert.Single(result.RecentLines);
    }

    [Fact]
    public void RecentLines_AreCappedAndNewestFirst()
    {
        using var harness = NewInvoicingHarness(out var bankId, out var accountId);
        for (var day = 1; day <= BankAccountService.BalanceCheckRecentLineCount + 5; day++)
            harness.Post(accountId, DrOrCrSide.Dr, 1m, Today.AddDays(-day));

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 0m });

        Assert.Equal(BankAccountService.BalanceCheckRecentLineCount, result.RecentLines.Count);
        Assert.Equal(Today.AddDays(-1), result.RecentLines[0].Date);
        Assert.True(result.RecentLines.Zip(result.RecentLines.Skip(1)).All(p => p.First.Date >= p.Second.Date));
    }

    [Fact]
    public void ZeroBooks_WithNoActivity_ComparesAgainstZero()
    {
        using var harness = NewInvoicingHarness(out var bankId, out _);

        var result = harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 25m });

        Assert.Equal(0m, result.BookBalance);
        Assert.Equal(25m, result.Difference);
        Assert.Empty(result.RecentLines);
    }

    [Fact]
    public void RefusedWhenFinancialsIsOn()
    {
        using var harness = NewInvoicingHarness(out var bankId, out _, financialsOn: true);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 1m }));

        Assert.Contains("reconciliation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FutureAsOfDate_IsRefused()
    {
        using var harness = NewInvoicingHarness(out var bankId, out _);

        Assert.Throws<InvalidOperationException>(() =>
            harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 1m, AsOfDate = Today.AddDays(30) }));
    }

    [Fact]
    public void AbsurdBalance_IsRefused()
    {
        using var harness = NewInvoicingHarness(out var bankId, out _);

        Assert.Throws<InvalidOperationException>(() =>
            harness.Banks.CheckBalance(bankId, new BankBalanceCheckRequest
            {
                AsOfBalance = BankAccountService.BalanceCheckMaxMagnitude + 1m
            }));
    }

    [Fact]
    public void OtherTenantsBank_IsNotFound()
    {
        using var harness = NewInvoicingHarness(out var bankId, out _);
        var other = harness.BuildServices(companyId: 2);

        Assert.Throws<TenantEntityNotFoundException>(() =>
            other.Banks.CheckBalance(bankId, new BankBalanceCheckRequest { AsOfBalance = 1m }));
    }
}
