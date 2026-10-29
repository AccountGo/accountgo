// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class ReconciliationReviewTests
{
    private static BankReconciliation AddReconciliation(
        BankingTestHarness harness,
        int bankId,
        BankReconciliationStatuses status,
        string? finishedBy = "preparer@example.com")
    {
        var recon = new BankReconciliation
        {
            CompanyId = 1,
            BankId = bankId,
            StatementEndDate = BankingTestHarness.Utc(2026, 9, 30),
            Status = status,
            CreatedAt = BankingTestHarness.Utc(2026, 10, 1),
            FinishedBy = status == BankReconciliationStatuses.Finished ? finishedBy : null,
            FinishedAt = status == BankReconciliationStatuses.Finished ? BankingTestHarness.Utc(2026, 10, 2) : null,
            Version = 3
        };
        harness.Repos.Reconciliations.Insert(recon);
        return recon;
    }

    private static (BankingTestHarness Harness, int BankId) NewHarness()
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        return (harness, harness.CreateBank("Checking").Id);
    }

    [Fact]
    public void Review_DifferentUser_SignsOff()
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished);

        var reviewed = harness.Reconciliations.Review(recon.Id, "  reviewer@example.com ");

        Assert.Equal("reviewer@example.com", reviewed.ReviewedBy);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.Equal((int)BankReconciliationStatuses.Finished, reviewed.Status);
        Assert.Equal(4, reviewed.Version);
        var listed = harness.Reconciliations.List(bankId).Single();
        Assert.Equal("reviewer@example.com", listed.ReviewedBy);
        Assert.NotNull(listed.ReviewedAt);
    }

    [Theory]
    [InlineData("preparer@example.com")]
    [InlineData("PREPARER@example.com")]
    [InlineData("  preparer@example.com  ")]
    public void Review_SameUserAsPreparer_IsRefused(string actor)
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished);

        var ex = Assert.Throws<BankReconciliationConflictException>(() =>
            harness.Reconciliations.Review(recon.Id, actor));

        Assert.Equal(BankReconciliationConflictException.SameReviewerCode, ex.Code);
        Assert.Null(harness.Repos.Reconciliations.GetById(recon.Id).ReviewedBy);
        Assert.Null(harness.Repos.Reconciliations.GetById(recon.Id).ReviewedAt);
    }

    [Theory]
    [InlineData(BankReconciliationStatuses.Draft)]
    [InlineData(BankReconciliationStatuses.Undone)]
    public void Review_NonFinished_IsRefused(BankReconciliationStatuses status)
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, status);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Reconciliations.Review(recon.Id, "reviewer@example.com"));

        Assert.Contains("finished", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Repos.Reconciliations.GetById(recon.Id).ReviewedBy);
    }

    [Fact]
    public void Review_Twice_IsRefused_AndKeepsFirstReviewer()
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished);
        harness.Reconciliations.Review(recon.Id, "first@example.com");

        var ex = Assert.Throws<BankReconciliationConflictException>(() =>
            harness.Reconciliations.Review(recon.Id, "second@example.com"));

        Assert.Equal(BankReconciliationConflictException.AlreadyReviewedCode, ex.Code);
        Assert.Equal("first@example.com", harness.Repos.Reconciliations.GetById(recon.Id).ReviewedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Review_WithoutIdentifiableActor_IsRefused(string actor)
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished);

        Assert.Throws<InvalidOperationException>(() => harness.Reconciliations.Review(recon.Id, actor));
    }

    [Fact]
    public void Review_LegacyFinishedWithoutPreparer_AllowsAnyReviewer()
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished, finishedBy: null);

        var reviewed = harness.Reconciliations.Review(recon.Id, "reviewer@example.com");

        Assert.Equal("reviewer@example.com", reviewed.ReviewedBy);
    }

    [Fact]
    public void Review_OtherTenantsReconciliation_IsNotFound()
    {
        var (harness, bankId) = NewHarness();
        using var _ = harness;
        var recon = AddReconciliation(harness, bankId, BankReconciliationStatuses.Finished);
        var other = harness.BuildServices(companyId: 2);

        Assert.Throws<TenantEntityNotFoundException>(() =>
            other.Reconciliations.Review(recon.Id, "reviewer@example.com"));
    }
}
