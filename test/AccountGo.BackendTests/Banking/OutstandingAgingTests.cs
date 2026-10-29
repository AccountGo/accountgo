// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Tenancy;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class OutstandingAgingTests
{
    private static readonly DateTime AsOf = BankingTestHarness.Utc(2026, 10, 30);

    private static BankingTestHarness NewHarness(out int bankId, out int accountId)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var created = harness.CreateBank("Checking");
        bankId = created.Id;
        accountId = created.AccountId!.Value;
        return harness;
    }

    private static int BucketIndexFor(int ageDays)
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 10m, AsOf.AddDays(-ageDays));
        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);
        return aging.Buckets.ToList().FindIndex(b => b.Count == 1);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(90, 2)]
    [InlineData(91, 3)]
    [InlineData(400, 3)]
    public void Buckets_UseInclusiveBoundaries(int ageDays, int expectedBucket)
    {
        Assert.Equal(expectedBucket, BucketIndexFor(ageDays));
    }

    [Fact]
    public void Buckets_AreLabelledAndOnlyLastIsStale()
    {
        using var harness = NewHarness(out var bankId, out _);
        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(new[] { "0-30", "31-60", "61-90", "90+" }, aging.Buckets.Select(b => b.Label));
        Assert.Equal(new[] { false, false, false, true }, aging.Buckets.Select(b => b.IsStale));
        Assert.Null(aging.Buckets[3].MaxDays);
        Assert.False(aging.HasStale);
        Assert.Equal(0, aging.TotalCount);
    }

    [Fact]
    public void FutureDatedLines_AreExcluded()
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 10m, AsOf.AddDays(1));
        harness.Post(accountId, DrOrCrSide.Dr, 20m, AsOf);

        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(1, aging.TotalCount);
        Assert.Equal(20m, aging.TotalDepositsInTransit);
    }

    [Fact]
    public void ClearedLines_AreExcluded()
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        var cleared = harness.Post(accountId, DrOrCrSide.Dr, 10m, AsOf.AddDays(-5));
        cleared.BankReconciliationId = 99;
        harness.Post(accountId, DrOrCrSide.Cr, 4m, AsOf.AddDays(-5));

        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(1, aging.TotalCount);
        Assert.Equal(4m, aging.TotalOutstandingPayments);
        Assert.Equal(0m, aging.TotalDepositsInTransit);
    }

    [Fact]
    public void Sums_SplitDepositsAndPayments_AndFlagStale()
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 100.005m, AsOf.AddDays(-2));
        harness.Post(accountId, DrOrCrSide.Cr, 30m, AsOf.AddDays(-3));
        harness.Post(accountId, DrOrCrSide.Cr, 12.50m, AsOf.AddDays(-120));

        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(2, aging.Buckets[0].Count);
        Assert.Equal(100.01m, aging.Buckets[0].DepositsInTransit);
        Assert.Equal(30m, aging.Buckets[0].OutstandingPayments);
        Assert.Equal(70.01m, aging.Buckets[0].Net);
        Assert.Equal(1, aging.StaleCount);
        Assert.True(aging.HasStale);
        Assert.Equal(3, aging.TotalCount);
    }

    [Fact]
    public void OpeningBalance_IsNotAnOutstandingItem()
    {
        using var harness = NewHarness(out var bankId, out _);
        harness.Banks.Update(bankId, new Dto.Banking.UpdateBankAccountRequest
        {
            Name = "Checking",
            Type = 1,
            OpeningBalance = 500m,
            OpeningBalanceDate = AsOf.AddDays(-200)
        }, includeFullNumbers: false);

        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(0, aging.TotalCount);
    }

    [Fact]
    public void VoidedEntryAndItsReversal_AreExcluded()
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        var original = harness.Post(accountId, DrOrCrSide.Dr, 25m, AsOf.AddDays(-40));
        var reversal = harness.Post(accountId, DrOrCrSide.Cr, 25m, AsOf.AddDays(-39));
        harness.Repos.GlHeaders.GetById(reversal.GeneralLedgerHeaderId).ReversedGeneralLedgerHeaderId =
            original.GeneralLedgerHeaderId;
        harness.Post(accountId, DrOrCrSide.Dr, 5m, AsOf.AddDays(-1));

        var aging = harness.Banks.GetOutstandingAging(bankId, AsOf);

        Assert.Equal(1, aging.TotalCount);
        Assert.Equal(5m, aging.TotalDepositsInTransit);
    }

    [Fact]
    public void DefaultAsOf_IsToday()
    {
        using var harness = NewHarness(out var bankId, out var accountId);
        harness.Post(accountId, DrOrCrSide.Dr, 10m, DateTime.UtcNow.Date);

        var aging = harness.Banks.GetOutstandingAging(bankId, asOf: null);

        Assert.Equal(DateTime.UtcNow.Date, aging.AsOfDate);
        Assert.Equal(1, aging.Buckets[0].Count);
    }

    [Fact]
    public void OtherTenantsBank_IsNotFound()
    {
        using var harness = NewHarness(out var bankId, out _);
        var other = harness.BuildServices(companyId: 2);

        Assert.Throws<TenantEntityNotFoundException>(() => other.Banks.GetOutstandingAging(bankId, AsOf));
    }
}
