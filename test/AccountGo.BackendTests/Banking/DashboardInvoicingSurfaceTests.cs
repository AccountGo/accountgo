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
using Moq;
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
        bool withBanking = false)
    {
        var financial = new Mock<IFinancialService>(MockBehavior.Loose);
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

        var sales = new Mock<ISalesService>(MockBehavior.Loose);
        sales.Setup(s => s.GetSalesInvoices()).Returns(invoices.ToList());
        sales.Setup(s => s.GetSalesReceipts()).Returns(new List<SalesReceiptHeader>());
        sales.Setup(s => s.GetCustomers()).Returns((customers ?? Enumerable.Empty<Customer>()).ToList());

        var purchases = new Mock<IPurchasingService>(MockBehavior.Loose);
        purchases.Setup(p => p.GetPurchaseInvoices()).Returns(new List<Core.Domain.Purchases.PurchaseInvoiceHeader>());

        var domains = new HashSet<string> { "sales", "purchases" };
        if (withBanking) domains.Add("banking");
        var auth = new Mock<Core.Authorization.IAccessAuthorizationService>();
        auth.Setup(a => a.GetAuthorizedDashboardDomains(It.IsAny<Core.Authorization.AccessActorContext?>())).Returns(domains);

        var permissions = new HashSet<string>
        {
            SystemPermissions.SalesInvoicesView,
            SystemPermissions.PurchasesBillsView,
        };
        if (withBanking) permissions.Add(SystemPermissions.BankingAccountsView);

        return new DashboardService(
            financial.Object,
            sales.Object,
            purchases.Object,
            Mock.Of<Services.Banking.IBankAccountService>(),
            new BankingTestHarness.FakeTenant(1, permissions),
            auth.Object);
    }

    private static SalesInvoiceHeader Invoice(
        DateTime date,
        decimal lineAmount,
        SalesInvoiceStatus status,
        DateTime? dueDate = null,
        decimal allocated = 0m)
    {
        var invoice = new SalesInvoiceHeader
        {
            Id = date.Day + date.Month * 100,
            Date = date,
            DueDate = dueDate,
            Status = status,
            No = $"INV-{date:MMdd}",
            CustomerId = 1,
            SalesInvoiceLines =
            {
                new SalesInvoiceLine { Quantity = 1, Amount = lineAmount, Discount = 0, TaxAmount = 0 }
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
}
