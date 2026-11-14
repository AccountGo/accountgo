// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Services.Financial;
using Xunit;

namespace AccountGo.ServicesTests;

public sealed class CashBasisRecognitionTests
{
    private static IReadOnlyDictionary<int, CashAccountInfo> DefaultAccounts() =>
        new Dictionary<int, CashAccountInfo>
        {
            [1] = new(1, (int)AccountClasses.Assets, "10120", "Accounts Receivable"),
            [2] = new(2, (int)AccountClasses.Assets, "10110", "Cash in Bank"),
            [3] = new(3, (int)AccountClasses.Revenue, "40100", "Sales Revenue"),
            [4] = new(4, (int)AccountClasses.Liabilities, "20300", "Sales Tax Payable"),
            [5] = new(5, (int)AccountClasses.Liabilities, "20100", "Accounts Payable"),
            [6] = new(6, (int)AccountClasses.Assets, "10160", "Input Tax"),
            [7] = new(7, (int)AccountClasses.Expense, "50100", "Office Expense"),
            [8] = new(8, (int)AccountClasses.Assets, "15100", "Equipment"),
        };

    [Fact]
    public void Invoice_WithTwoTaxCodesOnOneAccount_PaidInThreePartialPayments_SumsToOriginalOffsetLines()
    {
        var accounts = DefaultAccounts();
        // Invoice: 1,000 revenue + 120 VAT12 + 50 RED5 = 1,170 AR (GL header 10)
        // Both tax codes share account 4 (20300), represented as two GL lines on account 4.
        var glLines = new List<CashGlLineInfo>
        {
            new(1, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 1, DrOrCrSide.Dr, 1170m),
            new(2, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 3, DrOrCrSide.Cr, 1000m),
            new(3, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 4, DrOrCrSide.Cr, 120m),
            new(4, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 4, DrOrCrSide.Cr, 50m),
            // Three receipt GL headers (101, 102, 103)
            new(5, 101, new DateTime(2026, 10, 10), DocumentTypes.SalesReceipt, 2, DrOrCrSide.Dr, 390m),
            new(6, 101, new DateTime(2026, 10, 10), DocumentTypes.SalesReceipt, 1, DrOrCrSide.Cr, 390m),
            new(7, 102, new DateTime(2026, 10, 20), DocumentTypes.SalesReceipt, 2, DrOrCrSide.Dr, 390m),
            new(8, 102, new DateTime(2026, 10, 20), DocumentTypes.SalesReceipt, 1, DrOrCrSide.Cr, 390m),
            new(9, 103, new DateTime(2026, 11, 5), DocumentTypes.SalesReceipt, 2, DrOrCrSide.Dr, 390m),
            new(10, 103, new DateTime(2026, 11, 5), DocumentTypes.SalesReceipt, 1, DrOrCrSide.Cr, 390m),
        };

        var input = new CashBasisRecognitionInput(
            Accounts: accounts,
            GlLines: glLines,
            PurchasingTaxAccountIds: new HashSet<int> { 6 },
            HasSalesInvoices: true,
            CustomerArAccounts: new Dictionary<int, int?> { [1] = 1 },
            ReceiptGlMap: new Dictionary<int, int> { [201] = 101, [202] = 102, [203] = 103 },
            Allocations: new List<CashAllocationInfo>
            {
                new(1, 50, 201, null, new DateTime(2026, 10, 10), 390m),
                new(2, 50, 202, null, new DateTime(2026, 10, 20), 390m),
                new(3, 50, 203, null, new DateTime(2026, 11, 5), 390m),
            },
            Invoices: new List<CashInvoiceInfo>
            {
                new(50, 1, SalesInvoiceStatus.Closed, 10, null),
            },
            HasPurchaseInvoices: false,
            VendorApAccounts: new Dictionary<int, int?>(),
            VendorPayments: new List<CashVendorPaymentInfo>(),
            Bills: new List<CashBillInfo>());

        var postings = CashBasisRecognition.Recognize(input);

        var settlementByAccount = postings
            .Where(p => p.Source == CashRecognitionSource.InvoiceSettlement && p.DocumentId == 50)
            .GroupBy(p => p.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.DrCr == DrOrCrSide.Cr ? p.Amount : -p.Amount));

        Assert.Equal(1000m, settlementByAccount[3]);
        Assert.Equal(170m, settlementByAccount[4]);
        Assert.False(settlementByAccount.ContainsKey(1));
    }

    [Fact]
    public void Bill_WithCapitalizedAssetLineExpenseLineAndInputTax_ExcludesAssetFromBillSettlementAndIncludesTax()
    {
        var accounts = DefaultAccounts();
        // Bill: 500 Equipment (capitalized asset 15100, account 8) + 200 Expense (account 7) + 84 Input Tax (10160, account 6) = 784 AP (account 5)
        var glLines = new List<CashGlLineInfo>
        {
            new(1, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 8, DrOrCrSide.Dr, 500m),
            new(2, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 7, DrOrCrSide.Dr, 200m),
            new(3, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 6, DrOrCrSide.Dr, 84m),
            new(4, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 5, DrOrCrSide.Cr, 784m),
            // Payment of 784 on 2026-10-15 (GL header 201)
            new(5, 201, new DateTime(2026, 10, 15), DocumentTypes.PurchaseInvoicePayment, 5, DrOrCrSide.Dr, 784m),
            new(6, 201, new DateTime(2026, 10, 15), DocumentTypes.PurchaseInvoicePayment, 2, DrOrCrSide.Cr, 784m),
        };

        var input = new CashBasisRecognitionInput(
            Accounts: accounts,
            GlLines: glLines,
            PurchasingTaxAccountIds: new HashSet<int> { 6 },
            HasSalesInvoices: false,
            CustomerArAccounts: new Dictionary<int, int?>(),
            ReceiptGlMap: new Dictionary<int, int>(),
            Allocations: new List<CashAllocationInfo>(),
            Invoices: new List<CashInvoiceInfo>(),
            HasPurchaseInvoices: true,
            VendorApAccounts: new Dictionary<int, int?> { [1] = 5 },
            VendorPayments: new List<CashVendorPaymentInfo>
            {
                new(301, 60, 201, new DateTime(2026, 10, 15), 784m),
            },
            Bills: new List<CashBillInfo>
            {
                new(60, 1, PurchaseInvoiceStatus.Paid, 20, null),
            });

        var postings = CashBasisRecognition.Recognize(input);

        var billSettlements = postings
            .Where(p => p.Source == CashRecognitionSource.BillSettlement && p.DocumentId == 60)
            .ToList();

        Assert.DoesNotContain(billSettlements, p => p.AccountId == 8);
        Assert.Equal(200m, billSettlements.Where(p => p.AccountId == 7).Sum(p => p.DrCr == DrOrCrSide.Dr ? p.Amount : -p.Amount));
        Assert.Equal(84m, billSettlements.Where(p => p.AccountId == 6).Sum(p => p.DrCr == DrOrCrSide.Dr ? p.Amount : -p.Amount));
        // Capitalized asset line remains recognized as Ledger on the bill date
        Assert.Contains(postings, p => p.Source == CashRecognitionSource.Ledger && p.AccountId == 8 && p.Amount == 500m);
    }

    [Fact]
    public void VoidedInvoice_ProducesNoPostings()
    {
        var accounts = DefaultAccounts();
        var glLines = new List<CashGlLineInfo>
        {
            new(1, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 1, DrOrCrSide.Dr, 112m),
            new(2, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 3, DrOrCrSide.Cr, 100m),
            new(3, 10, new DateTime(2026, 10, 1), DocumentTypes.SalesInvoice, 4, DrOrCrSide.Cr, 12m),
            // Void reversal GL header 11
            new(4, 11, new DateTime(2026, 10, 2), DocumentTypes.SalesInvoice, 1, DrOrCrSide.Cr, 112m),
            new(5, 11, new DateTime(2026, 10, 2), DocumentTypes.SalesInvoice, 3, DrOrCrSide.Dr, 100m),
            new(6, 11, new DateTime(2026, 10, 2), DocumentTypes.SalesInvoice, 4, DrOrCrSide.Dr, 12m),
        };

        var input = new CashBasisRecognitionInput(
            Accounts: accounts,
            GlLines: glLines,
            PurchasingTaxAccountIds: new HashSet<int> { 6 },
            HasSalesInvoices: true,
            CustomerArAccounts: new Dictionary<int, int?> { [1] = 1 },
            ReceiptGlMap: new Dictionary<int, int>(),
            Allocations: new List<CashAllocationInfo>(),
            Invoices: new List<CashInvoiceInfo>
            {
                new(50, 1, SalesInvoiceStatus.Void, 10, 11),
            },
            HasPurchaseInvoices: false,
            VendorApAccounts: new Dictionary<int, int?>(),
            VendorPayments: new List<CashVendorPaymentInfo>(),
            Bills: new List<CashBillInfo>());

        var postings = CashBasisRecognition.Recognize(input);

        Assert.Empty(postings);
    }

    [Fact]
    public void VoidedVendorPayment_IsIgnored()
    {
        var accounts = DefaultAccounts();
        // Bill posted (GL 20): 200 expense + 24 tax = 224 AP
        // Voided vendor payment has GeneralLedgerHeaderId == null (filtered out by loader), or even if its GL lines are absent, no BillSettlement occurs.
        var glLines = new List<CashGlLineInfo>
        {
            new(1, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 7, DrOrCrSide.Dr, 200m),
            new(2, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 6, DrOrCrSide.Dr, 24m),
            new(3, 20, new DateTime(2026, 10, 1), DocumentTypes.PurchaseInvoice, 5, DrOrCrSide.Cr, 224m),
        };

        var input = new CashBasisRecognitionInput(
            Accounts: accounts,
            GlLines: glLines,
            PurchasingTaxAccountIds: new HashSet<int> { 6 },
            HasSalesInvoices: false,
            CustomerArAccounts: new Dictionary<int, int?>(),
            ReceiptGlMap: new Dictionary<int, int>(),
            Allocations: new List<CashAllocationInfo>(),
            Invoices: new List<CashInvoiceInfo>(),
            HasPurchaseInvoices: true,
            VendorApAccounts: new Dictionary<int, int?> { [1] = 5 },
            // Voided payment has no GeneralLedgerHeaderId
            VendorPayments: new List<CashVendorPaymentInfo>
            {
                new(301, 60, null, new DateTime(2026, 10, 15), 224m),
            },
            Bills: new List<CashBillInfo>
            {
                new(60, 1, PurchaseInvoiceStatus.Open, 20, null),
            });

        var postings = CashBasisRecognition.Recognize(input);

        Assert.DoesNotContain(postings, p => p.Source == CashRecognitionSource.BillSettlement);
        Assert.DoesNotContain(postings, p => p.AccountId == 6);
    }
}
