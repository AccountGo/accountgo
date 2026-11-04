// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Domain.Security;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Dashboard;
using Services.Financial;
using Services.Purchasing;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>Invoicing product dashboard: sales YTD and overdue from invoices without a ledger.</summary>
public class DashboardInvoicingSurfaceTests
{
    private static DashboardService BuildService(
        IEnumerable<SalesInvoiceHeader> invoices,
        IEnumerable<Customer>? customers = null,
        IEnumerable<string>? domains = null,
        IBankAccountService? bankAccounts = null,
        Mock<IFinancialService>? financialMock = null)
    {
        var financial = financialMock ?? new Mock<IFinancialService>(MockBehavior.Loose);
        if (financialMock == null)
        {
            financial.Setup(f => f.GetAccounts()).Returns(new List<Account>());
            financial.Setup(f => f.GetCashAndBanks()).Returns(new List<Bank>());
            financial
                .Setup(f => f.MasterGeneralLedger(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<int?>()))
                .Returns(new List<MasterGeneralLedger>());
            financial
                .Setup(f => f.BalanceSheet(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AccountingBasis>()))
                .Returns(new List<BalanceSheet>());
            financial
                .Setup(f => f.IncomeStatement(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AccountingBasis>()))
                .Returns(new List<IncomeStatement>());
            financial.Setup(f => f.CurrentFiscalYear()).Returns((FinancialYear?)null);
        }

        var sales = new Mock<ISalesService>(MockBehavior.Loose);
        sales.Setup(s => s.GetSalesInvoices()).Returns(invoices.ToList());
        sales.Setup(s => s.GetSalesReceipts()).Returns(new List<SalesReceiptHeader>());
        sales.Setup(s => s.GetCustomers()).Returns((customers ?? Enumerable.Empty<Customer>()).ToList());

        var purchases = new Mock<IPurchasingService>(MockBehavior.Loose);
        purchases.Setup(p => p.GetPurchaseInvoices()).Returns(new List<Core.Domain.Purchases.PurchaseInvoiceHeader>());

        var authorized = new HashSet<string>(domains ?? new[] { "sales", "purchases" });
        var auth = new Mock<Core.Authorization.IAccessAuthorizationService>();
        auth.Setup(a => a.GetAuthorizedDashboardDomains(It.IsAny<Core.Authorization.AccessActorContext?>())).Returns(authorized);

        var permissions = new HashSet<string>();
        if (authorized.Contains("sales")) permissions.Add(SystemPermissions.SalesInvoicesView);
        if (authorized.Contains("purchases")) permissions.Add(SystemPermissions.PurchasesBillsView);
        if (authorized.Contains("banking")) permissions.Add(SystemPermissions.BankingAccountsView);
        if (authorized.Contains("financials")) permissions.Add(SystemPermissions.FinancialsAccountsView);

        return new DashboardService(
            financial.Object,
            sales.Object,
            purchases.Object,
            bankAccounts ?? Mock.Of<IBankAccountService>(),
            new BankingTestHarness.FakeTenant(1, permissions),
            auth.Object);
    }

    private static IBankAccountService BanksWithBalance(string name, decimal bookBalance, decimal unassignedCash = 0m)
    {
        var banks = new Mock<IBankAccountService>();
        banks.Setup(b => b.List(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new BankAccountsListDto
            {
                Accounts =
                [
                    new BankAccountDto { Id = 7, Name = name, BookBalance = bookBalance }
                ],
                UnassignedCash = unassignedCash == 0m
                    ? Array.Empty<UnassignedCashDto>()
                    : [new UnassignedCashDto { AccountId = 10, Balance = unassignedCash, Description = "Cash" }]
            });
        return banks.Object;
    }

    private static SalesInvoiceHeader Invoice(
        DateTime date,
        decimal lineAmount,
        SalesInvoiceStatus status,
        DateTime? dueDate = null,
        decimal allocated = 0m,
        decimal taxAmount = 0m,
        decimal shipping = 0m,
        decimal discount = 0m,
        int customerId = 1,
        int? id = null)
    {
        var invoice = new SalesInvoiceHeader
        {
            Id = id ?? date.Day + date.Month * 100,
            Date = date,
            DueDate = dueDate,
            Status = status,
            No = $"INV-{date:MMdd}",
            CustomerId = customerId,
            ShippingHandlingCharge = shipping,
            SalesInvoiceLines =
            {
                new SalesInvoiceLine
                {
                    Quantity = 1,
                    Amount = lineAmount,
                    Discount = discount,
                    TaxAmount = taxAmount
                }
            },
        };
        if (allocated > 0)
        {
            invoice.CustomerAllocations.Add(new CustomerAllocation
            {
                Amount = allocated,
                SalesInvoiceHeaderId = invoice.Id,
            });
        }
        return invoice;
    }

    [Fact]
    public void InvoicingSurface_UsesIssuedInvoicesForSalesYtd_AndMarksSurface()
    {
        var invoices = new[]
        {
            Invoice(new DateTime(2026, 1, 10), 100m, SalesInvoiceStatus.Open),
            Invoice(new DateTime(2026, 2, 5), 50m, SalesInvoiceStatus.Closed),
            Invoice(new DateTime(2026, 2, 6), 999m, SalesInvoiceStatus.Draft), // excluded
            Invoice(new DateTime(2025, 12, 1), 40m, SalesInvoiceStatus.Open), // outside default calendar YTD if fiscal unset uses Jan 1
        };

        var summary = BuildService(invoices).GetDashboardSummary(
            from: new DateTime(2026, 1, 1),
            to: new DateTime(2026, 3, 1));

        Assert.Equal("Invoicing", summary.Surface);
        Assert.Equal(150m, summary.Kpis.TotalSalesYtd);
        Assert.Equal(0m, summary.Kpis.NetProfitYtd);
        Assert.Equal(2, summary.MonthlySales.Count(p => p.Amount > 0));
    }

    [Fact]
    public void InvoicingSurface_CountsOverdueOpenInvoices()
    {
        var today = DateTime.Today;
        var invoices = new[]
        {
            Invoice(today.AddDays(-40), 200m, SalesInvoiceStatus.Open, dueDate: today.AddDays(-10)),
            Invoice(today.AddDays(-5), 80m, SalesInvoiceStatus.Open, dueDate: today.AddDays(20)),
            Invoice(today.AddDays(-60), 30m, SalesInvoiceStatus.Open, dueDate: today.AddDays(-1), allocated: 30m), // paid
        };

        var summary = BuildService(invoices).GetDashboardSummary(from: today.AddYears(-1), to: today);

        Assert.Equal(1, summary.Kpis.OverdueInvoiceCount);
        Assert.Equal(200m, summary.Kpis.OverdueReceivables);
        Assert.Equal(2, summary.Kpis.OpenInvoiceCount);
    }

    [Fact]
    public void BankingWithoutFinancials_UsesBankServiceBalances_NotEmptyGetAccounts()
    {
        // Invoicing member with banking: GetAccounts() is empty under auth, but bank List still has balances.
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
        financial.Setup(f => f.GetAccounts()).Returns(new List<Account>());
        financial.Setup(f => f.GetCashAndBanks()).Returns(new List<Bank>
        {
            new() { Id = 7, Name = "Checking", AccountId = 99 }
        });
        financial
            .Setup(f => f.MasterGeneralLedger(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<MasterGeneralLedger>());
        financial
            .Setup(f => f.BalanceSheet(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AccountingBasis>()))
            .Returns(new List<BalanceSheet>());
        financial
            .Setup(f => f.IncomeStatement(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AccountingBasis>()))
            .Returns(new List<IncomeStatement>());
        financial.Setup(f => f.CurrentFiscalYear()).Returns((FinancialYear?)null);

        var summary = BuildService(
                Array.Empty<SalesInvoiceHeader>(),
                domains: new[] { "sales", "purchases", "banking" },
                bankAccounts: BanksWithBalance("Checking", 1250.50m, unassignedCash: 49.50m),
                financialMock: financial)
            .GetDashboardSummary();

        Assert.Equal("Invoicing", summary.Surface);
        Assert.False(summary.Capabilities.Financials);
        Assert.True(summary.Capabilities.Banking);
        Assert.True(summary.Capabilities.MoneyInBank);
        Assert.Equal(1300m, summary.Kpis.MoneyInBank);
        Assert.Equal(1250.50m, summary.Kpis.BankBalances!.Single().Balance);
        Assert.Equal(49.50m, summary.Kpis.UnassignedCash);
        financial.Verify(f => f.GetAccounts(), Times.Never);
    }

    [Fact]
    public void OutstandingAndOverdueReceivables_UseSameBalanceDueBasis_WithTaxShippingDiscountAndPayment()
    {
        var today = DateTime.Today;
        // Line 100 less 10% discount = 90; + tax 10 + shipping 5 = 105 grand; paid 25 → due 80.
        var overdue = Invoice(
            today.AddDays(-40),
            lineAmount: 100m,
            status: SalesInvoiceStatus.Open,
            dueDate: today.AddDays(-5),
            allocated: 25m,
            taxAmount: 10m,
            shipping: 5m,
            discount: 10m,
            id: 1);
        var current = Invoice(
            today.AddDays(-2),
            lineAmount: 50m,
            status: SalesInvoiceStatus.Open,
            dueDate: today.AddDays(20),
            taxAmount: 5m,
            shipping: 0m,
            discount: 0m,
            id: 2);

        Assert.Equal(80m, overdue.BalanceDue());
        Assert.Equal(55m, current.BalanceDue());

        var summary = BuildService(new[] { overdue, current })
            .GetDashboardSummary(from: today.AddYears(-1), to: today);

        Assert.Equal(135m, summary.Kpis.OutstandingReceivables);
        Assert.Equal(80m, summary.Kpis.OverdueReceivables);
        Assert.Equal(1, summary.Kpis.OverdueInvoiceCount);
        Assert.Equal(2, summary.Kpis.OpenInvoiceCount);
        Assert.Equal(135m, summary.TopReceivables.Sum(r => r.Balance));
        Assert.True(summary.Capabilities.Receivables);
    }

    [Fact]
    public void RestrictedMemberWithoutSales_OmitsReceivablesAndMarksCapabilities()
    {
        var today = DateTime.Today;
        var invoices = new[]
        {
            Invoice(today.AddDays(-40), 200m, SalesInvoiceStatus.Open, dueDate: today.AddDays(-10)),
        };

        var summary = BuildService(
                invoices,
                domains: new[] { "banking" },
                bankAccounts: BanksWithBalance("Checking", 400m))
            .GetDashboardSummary(from: today.AddYears(-1), to: today);

        Assert.Equal("Invoicing", summary.Surface);
        Assert.False(summary.Capabilities.Sales);
        Assert.False(summary.Capabilities.Receivables);
        Assert.False(summary.Capabilities.Purchases);
        Assert.False(summary.Capabilities.Payables);
        Assert.False(summary.Capabilities.Financials);
        Assert.True(summary.Capabilities.Banking);
        Assert.True(summary.Capabilities.MoneyInBank);
        Assert.Equal(0m, summary.Kpis.OutstandingReceivables);
        Assert.Equal(0m, summary.Kpis.OverdueReceivables);
        Assert.Equal(0, summary.Kpis.OpenInvoiceCount);
        Assert.Equal(400m, summary.Kpis.MoneyInBank);
        Assert.Empty(summary.TopReceivables);
    }

    [Fact]
    public void SalesYtd_IncludesTaxAndShipping_ExcludesDraftsAndVoids()
    {
        var invoices = new[]
        {
            Invoice(new DateTime(2026, 1, 10), 100m, SalesInvoiceStatus.Open, taxAmount: 10m, shipping: 5m, id: 1),
            Invoice(new DateTime(2026, 2, 1), 40m, SalesInvoiceStatus.Void, taxAmount: 4m, id: 2),
            Invoice(new DateTime(2026, 2, 2), 90m, SalesInvoiceStatus.Draft, taxAmount: 9m, id: 3),
        };

        var summary = BuildService(invoices).GetDashboardSummary(
            from: new DateTime(2026, 1, 1),
            to: new DateTime(2026, 3, 1));

        Assert.Equal(115m, summary.Kpis.TotalSalesYtd);
    }
}
