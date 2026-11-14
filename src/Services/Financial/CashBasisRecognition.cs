// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;

namespace Services.Financial
{
    public enum CashRecognitionSource
    {
        InvoiceSettlement,
        BillSettlement,
        Ledger
    }

    public sealed record CashRecognizedPosting(
        int AccountId,
        DrOrCrSide DrCr,
        decimal Amount,
        DateTime Date,
        CashRecognitionSource Source,
        int? DocumentId,
        int? GlHeaderId);

    public sealed record CashAccountInfo(int Id, int AccountClassId, string AccountCode, string AccountName);

    public sealed record CashGlLineInfo(
        int Id,
        int GeneralLedgerHeaderId,
        DateTime HeaderDate,
        DocumentTypes? DocumentType,
        int AccountId,
        DrOrCrSide DrCr,
        decimal Amount);

    public sealed record CashAllocationInfo(
        int Id,
        int SalesInvoiceHeaderId,
        int SalesReceiptHeaderId,
        int? GeneralLedgerHeaderId,
        DateTime Date,
        decimal Amount);

    public sealed record CashInvoiceInfo(
        int Id,
        int CustomerId,
        SalesInvoiceStatus? Status,
        int GlHeaderId,
        int? VoidGeneralLedgerHeaderId);

    public sealed record CashVendorPaymentInfo(
        int Id,
        int BillId,
        int? GeneralLedgerHeaderId,
        DateTime Date,
        decimal Amount);

    public sealed record CashBillInfo(
        int Id,
        int? VendorId,
        PurchaseInvoiceStatus? Status,
        int GlHeaderId,
        int? VoidGeneralLedgerHeaderId);

    public sealed record CashBasisRecognitionInput(
        IReadOnlyDictionary<int, CashAccountInfo> Accounts,
        IReadOnlyList<CashGlLineInfo> GlLines,
        IReadOnlySet<int> PurchasingTaxAccountIds,
        bool HasSalesInvoices,
        IReadOnlyDictionary<int, int?> CustomerArAccounts,
        IReadOnlyDictionary<int, int> ReceiptGlMap,
        IReadOnlyList<CashAllocationInfo> Allocations,
        IReadOnlyList<CashInvoiceInfo> Invoices,
        bool HasPurchaseInvoices,
        IReadOnlyDictionary<int, int?> VendorApAccounts,
        IReadOnlyList<CashVendorPaymentInfo> VendorPayments,
        IReadOnlyList<CashBillInfo> Bills);

    public static class CashBasisRecognitionLoader
    {
        public static CashBasisRecognitionInput Load(
            IRepository<Account> accountRepo,
            IRepository<GeneralLedgerLine> glLineRepo,
            IRepository<Tax>? taxRepo,
            IRepository<SalesInvoiceHeader>? salesInvoiceRepo,
            IRepository<Customer>? customerRepo,
            IRepository<SalesReceiptHeader>? salesReceiptRepo,
            IRepository<CustomerAllocation>? customerAllocationRepo,
            IRepository<PurchaseInvoiceHeader>? purchaseInvoiceRepo,
            IRepository<Vendor>? vendorRepo,
            IRepository<VendorPayment>? vendorPaymentRepo,
            IReadOnlyCollection<int>? excludedGlHeaderIds = null)
        {
            var accounts = accountRepo.Table
                .Select(a => new CashAccountInfo(a.Id, a.AccountClassId, a.AccountCode, a.AccountName))
                .ToDictionary(a => a.Id);

            var purchasingTaxAccountIds = taxRepo?.Table
                .Where(t => t.PurchasingAccountId.HasValue)
                .Select(t => t.PurchasingAccountId!.Value)
                .ToHashSet()
                ?? new HashSet<int>();

            var glLines = glLineRepo.Table
                .Where(l => excludedGlHeaderIds == null
                            || excludedGlHeaderIds.Count == 0
                            || !excludedGlHeaderIds.Contains(l.GeneralLedgerHeaderId))
                .Select(l => new CashGlLineInfo(
                    l.Id,
                    l.GeneralLedgerHeaderId,
                    l.GeneralLedgerHeader.Date,
                    l.GeneralLedgerHeader.DocumentType,
                    l.AccountId,
                    l.DrCr,
                    l.Amount))
                .ToList();

            var hasSalesInvoices = salesInvoiceRepo != null;
            var customerArAccounts = hasSalesInvoices && customerRepo != null
                ? customerRepo.Table
                    .Select(c => new { c.Id, c.AccountsReceivableAccountId })
                    .ToDictionary(c => c.Id, c => c.AccountsReceivableAccountId)
                : new Dictionary<int, int?>();

            var receiptGlMap = hasSalesInvoices && salesReceiptRepo != null
                ? salesReceiptRepo.Table
                    .Where(r => r.GeneralLedgerHeaderId.HasValue)
                    .Select(r => new { r.Id, GlHeaderId = r.GeneralLedgerHeaderId!.Value })
                    .ToDictionary(r => r.Id, r => r.GlHeaderId)
                : new Dictionary<int, int>();

            var allocations = hasSalesInvoices && customerAllocationRepo != null
                ? customerAllocationRepo.Table
                    .Select(a => new CashAllocationInfo(
                        a.Id,
                        a.SalesInvoiceHeaderId,
                        a.SalesReceiptHeaderId,
                        a.GeneralLedgerHeaderId,
                        a.Date,
                        a.Amount))
                    .ToList()
                : new List<CashAllocationInfo>();

            var invoices = salesInvoiceRepo != null
                ? salesInvoiceRepo.Table
                    .Where(i => i.GeneralLedgerHeaderId.HasValue)
                    .Select(i => new CashInvoiceInfo(
                        i.Id,
                        i.CustomerId,
                        i.Status,
                        i.GeneralLedgerHeaderId!.Value,
                        i.VoidGeneralLedgerHeaderId))
                    .ToList()
                : new List<CashInvoiceInfo>();

            var hasPurchaseInvoices = purchaseInvoiceRepo != null;
            var vendorApAccounts = hasPurchaseInvoices && vendorRepo != null
                ? vendorRepo.Table
                    .Select(v => new { v.Id, v.AccountsPayableAccountId })
                    .ToDictionary(v => v.Id, v => v.AccountsPayableAccountId)
                : new Dictionary<int, int?>();

            var vendorPayments = hasPurchaseInvoices && vendorPaymentRepo != null
                ? vendorPaymentRepo.Table
                    .Where(p => p.PurchaseInvoiceHeaderId.HasValue && !p.VoidedAt.HasValue)
                    .Select(p => new CashVendorPaymentInfo(
                        p.Id,
                        p.PurchaseInvoiceHeaderId!.Value,
                        p.GeneralLedgerHeaderId,
                        p.Date,
                        p.Amount))
                    .ToList()
                : new List<CashVendorPaymentInfo>();

            var bills = purchaseInvoiceRepo != null
                ? purchaseInvoiceRepo.Table
                    .Where(b => b.GeneralLedgerHeaderId.HasValue)
                    .Select(b => new CashBillInfo(
                        b.Id,
                        b.VendorId,
                        b.Status,
                        b.GeneralLedgerHeaderId!.Value,
                        b.VoidGeneralLedgerHeaderId))
                    .ToList()
                : new List<CashBillInfo>();

            return new CashBasisRecognitionInput(
                accounts,
                glLines,
                purchasingTaxAccountIds,
                hasSalesInvoices,
                customerArAccounts,
                receiptGlMap,
                allocations,
                invoices,
                hasPurchaseInvoices,
                vendorApAccounts,
                vendorPayments,
                bills);
        }
    }

    public static class CashBasisRecognition
    {
        public static IReadOnlyList<CashRecognizedPosting> Recognize(CashBasisRecognitionInput input)
        {
            var accounts = input.Accounts;
            var purchasingTaxAccountIds = input.PurchasingTaxAccountIds;
            var allGlLines = input.GlLines;

            bool IsCapitalizedAssetAccount(int accountId) =>
                accounts.TryGetValue(accountId, out var acc)
                && acc.AccountClassId == (int)AccountClasses.Assets
                && acc.AccountCode != "10160"
                && !purchasingTaxAccountIds.Contains(accountId);

            var linesByHeader = allGlLines
                .GroupBy(l => l.GeneralLedgerHeaderId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).ToList());

            var suppressedLineAmounts = new Dictionary<int, decimal>();
            var postings = new List<CashRecognizedPosting>();

            // Accrual customer-allocation journals reclassify Customer Advances to A/R; cash basis recognizes revenue directly against the receipt credit.
            foreach (var allocLine in allGlLines.Where(l => l.DocumentType == DocumentTypes.CustomerAllocation))
                suppressedLineAmounts[allocLine.Id] = allocLine.Amount;

            void EmitPosting(
                int accountId,
                DrOrCrSide drCr,
                decimal amount,
                DateTime date,
                CashRecognitionSource source,
                int? documentId,
                int? glHeaderId)
            {
                if (amount == 0m)
                    return;
                if (amount < 0m)
                {
                    drCr = drCr == DrOrCrSide.Dr ? DrOrCrSide.Cr : DrOrCrSide.Dr;
                    amount = -amount;
                }
                postings.Add(new CashRecognizedPosting(accountId, drCr, amount, date, source, documentId, glHeaderId));
            }

            if (input.HasSalesInvoices)
            {
                var allocationsByInvoice = input.Allocations
                    .GroupBy(a => a.SalesInvoiceHeaderId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Date).ThenBy(a => a.Id).ToList());

                foreach (var invoice in input.Invoices)
                {
                    if (!linesByHeader.TryGetValue(invoice.GlHeaderId, out var invLines) || invLines.Count == 0)
                        continue;

                    // Stock-issue pairs (Dr COGS, Cr Inventory) stay on the invoice date; only A/R and its revenue/tax/discount/shipping offsets wait for cash.
                    var stockLineIds = new HashSet<int>();
                    foreach (var invCredit in invLines.Where(l => l.DrCr == DrOrCrSide.Cr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets))
                    {
                        var matchingCogs = invLines.FirstOrDefault(l =>
                            !stockLineIds.Contains(l.Id)
                            && l.DrCr == DrOrCrSide.Dr
                            && l.Amount == invCredit.Amount
                            && accounts.TryGetValue(l.AccountId, out var cogsAcc)
                            && cogsAcc.AccountClassId == (int)AccountClasses.Expense);
                        if (matchingCogs != null)
                        {
                            stockLineIds.Add(invCredit.Id);
                            stockLineIds.Add(matchingCogs.Id);
                        }
                    }

                    var nonStockLines = invLines.Where(l => !stockLineIds.Contains(l.Id)).ToList();
                    var expectedArAccountId = input.CustomerArAccounts.GetValueOrDefault(invoice.CustomerId);
                    var arLine = nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && expectedArAccountId.HasValue && l.AccountId == expectedArAccountId.Value)
                        ?? nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets)
                        ?? nonStockLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr);

                    if (arLine == null || arLine.Amount <= 0m)
                        continue;

                    var offsetLines = nonStockLines.Where(l => l.Id != arLine.Id).ToList();
                    foreach (var line in nonStockLines)
                        suppressedLineAmounts[line.Id] = line.Amount;

                    if (invoice.VoidGeneralLedgerHeaderId.HasValue && linesByHeader.TryGetValue(invoice.VoidGeneralLedgerHeaderId.Value, out var voidLines))
                    {
                        var voidStockIds = new HashSet<int>();
                        foreach (var invDebit in voidLines.Where(l => l.DrCr == DrOrCrSide.Dr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Assets))
                        {
                            var matchingCogs = voidLines.FirstOrDefault(l =>
                                !voidStockIds.Contains(l.Id)
                                && l.DrCr == DrOrCrSide.Cr
                                && l.Amount == invDebit.Amount
                                && accounts.TryGetValue(l.AccountId, out var cogsAcc)
                                && cogsAcc.AccountClassId == (int)AccountClasses.Expense);
                            if (matchingCogs != null)
                            {
                                voidStockIds.Add(invDebit.Id);
                                voidStockIds.Add(matchingCogs.Id);
                            }
                        }
                        foreach (var line in voidLines.Where(l => !voidStockIds.Contains(l.Id)))
                            suppressedLineAmounts[line.Id] = line.Amount;
                        continue;
                    }

                    if (invoice.Status == SalesInvoiceStatus.Void || offsetLines.Count == 0)
                        continue;

                    if (!allocationsByInvoice.TryGetValue(invoice.Id, out var allocations) || allocations.Count == 0)
                        continue;

                    var invoiceTotal = arLine.Amount;
                    var signedOffsets = offsetLines
                        .Select(l => (l.AccountId, SignedAmount: l.DrCr == DrOrCrSide.Cr ? l.Amount : -l.Amount))
                        .ToList();
                    var prevCumulativeByLine = new decimal[signedOffsets.Count];
                    var cumulativePaid = 0m;

                    foreach (var allocation in allocations)
                    {
                        if (allocation.GeneralLedgerHeaderId.HasValue
                            && linesByHeader.TryGetValue(allocation.GeneralLedgerHeaderId.Value, out var allocGlLines))
                        {
                            foreach (var allocGlLine in allocGlLines)
                                suppressedLineAmounts[allocGlLine.Id] = allocGlLine.Amount;
                        }

                        if (allocation.Amount <= 0m || cumulativePaid >= invoiceTotal)
                            continue;

                        var desiredSettle = Math.Min(allocation.Amount, invoiceTotal - cumulativePaid);
                        if (desiredSettle <= 0m)
                            continue;

                        var settledAmount = desiredSettle;
                        var recognitionDate = allocation.Date;
                        if (input.ReceiptGlMap.TryGetValue(allocation.SalesReceiptHeaderId, out var receiptGlId)
                            && linesByHeader.TryGetValue(receiptGlId, out var receiptLines))
                        {
                            var receiptCreditLines = receiptLines
                                .Where(l => l.DrCr == DrOrCrSide.Cr)
                                .OrderByDescending(l => l.AccountId == arLine.AccountId)
                                .ThenBy(l => l.Id)
                                .ToList();
                            var availableReceiptCredit = receiptCreditLines.Sum(l =>
                                Math.Max(0m, l.Amount - suppressedLineAmounts.GetValueOrDefault(l.Id)));
                            settledAmount = Math.Min(desiredSettle, availableReceiptCredit);
                            if (settledAmount <= 0m)
                                continue;

                            var toSuppress = settledAmount;
                            foreach (var creditLine in receiptCreditLines)
                            {
                                if (toSuppress <= 0m)
                                    break;
                                var lineAvailable = Math.Max(0m, creditLine.Amount - suppressedLineAmounts.GetValueOrDefault(creditLine.Id));
                                if (lineAvailable <= 0m)
                                    continue;
                                var take = Math.Min(toSuppress, lineAvailable);
                                suppressedLineAmounts[creditLine.Id] = suppressedLineAmounts.GetValueOrDefault(creditLine.Id) + take;
                                toSuppress -= take;
                                recognitionDate = creditLine.HeaderDate;
                            }
                        }

                        var nextCumulativePaid = cumulativePaid + settledAmount;
                        var allocatedSumBeforeLast = 0m;
                        for (var i = 0; i < signedOffsets.Count; i++)
                        {
                            decimal delta;
                            if (i < signedOffsets.Count - 1)
                            {
                                var targetCumulative = Math.Round(signedOffsets[i].SignedAmount * nextCumulativePaid / invoiceTotal, 2, MidpointRounding.AwayFromZero);
                                delta = targetCumulative - prevCumulativeByLine[i];
                                prevCumulativeByLine[i] = targetCumulative;
                                allocatedSumBeforeLast += delta;
                            }
                            else
                            {
                                delta = settledAmount - allocatedSumBeforeLast;
                                prevCumulativeByLine[i] += delta;
                            }

                            if (delta > 0m)
                                EmitPosting(signedOffsets[i].AccountId, DrOrCrSide.Cr, delta, recognitionDate, CashRecognitionSource.InvoiceSettlement, invoice.Id, null);
                            else if (delta < 0m)
                                EmitPosting(signedOffsets[i].AccountId, DrOrCrSide.Dr, -delta, recognitionDate, CashRecognitionSource.InvoiceSettlement, invoice.Id, null);
                        }

                        cumulativePaid = nextCumulativePaid;
                    }
                }
            }

            if (input.HasPurchaseInvoices)
            {
                var paymentsByBill = input.VendorPayments
                    .GroupBy(p => p.BillId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ThenBy(p => p.Id).ToList());

                foreach (var bill in input.Bills)
                {
                    if (!linesByHeader.TryGetValue(bill.GlHeaderId, out var billLines) || billLines.Count == 0)
                        continue;

                    var expectedApAccountId = bill.VendorId.HasValue ? input.VendorApAccounts.GetValueOrDefault(bill.VendorId.Value) : null;
                    var apLine = billLines.LastOrDefault(l => l.DrCr == DrOrCrSide.Cr && expectedApAccountId.HasValue && l.AccountId == expectedApAccountId.Value)
                        ?? billLines.Where(l => l.DrCr == DrOrCrSide.Cr && accounts.TryGetValue(l.AccountId, out var acc) && acc.AccountClassId == (int)AccountClasses.Liabilities)
                            .OrderByDescending(l => l.Amount).FirstOrDefault()
                        ?? billLines.Where(l => l.DrCr == DrOrCrSide.Cr).OrderByDescending(l => l.Amount).FirstOrDefault();

                    if (apLine == null || apLine.Amount <= 0m)
                        continue;

                    var offsetLines = billLines.Where(l => l.Id != apLine.Id).ToList();
                    // Keep balance-sheet asset capitalization (e.g., inventory, fixed assets) on the bill date; defer only P&L and tax lines until payment.
                    var deferredLines = offsetLines.Where(l => !IsCapitalizedAssetAccount(l.AccountId)).ToList();
                    var deferredNet = deferredLines.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);
                    foreach (var line in deferredLines)
                        suppressedLineAmounts[line.Id] = line.Amount;
                    suppressedLineAmounts[apLine.Id] = Math.Clamp(deferredNet, 0m, apLine.Amount);

                    if (bill.VoidGeneralLedgerHeaderId.HasValue && linesByHeader.TryGetValue(bill.VoidGeneralLedgerHeaderId.Value, out var voidLines))
                    {
                        var voidApLine = voidLines.LastOrDefault(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == apLine.AccountId)
                            ?? voidLines.Where(l => l.DrCr == DrOrCrSide.Dr).OrderByDescending(l => l.Amount).FirstOrDefault();
                        foreach (var line in voidLines.Where(l => (voidApLine == null || l.Id != voidApLine.Id) && !IsCapitalizedAssetAccount(l.AccountId)))
                            suppressedLineAmounts[line.Id] = line.Amount;
                        if (voidApLine != null)
                            suppressedLineAmounts[voidApLine.Id] = Math.Clamp(deferredNet, 0m, voidApLine.Amount);
                        continue;
                    }

                    if (bill.Status == PurchaseInvoiceStatus.Void || deferredLines.Count == 0)
                        continue;

                    if (!paymentsByBill.TryGetValue(bill.Id, out var payments) || payments.Count == 0)
                        continue;

                    var billTotal = apLine.Amount;
                    var signedOffsets = offsetLines
                        .Select(l => (
                            l.AccountId,
                            SignedAmount: l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount,
                            IsCapitalized: IsCapitalizedAssetAccount(l.AccountId)))
                        .ToList();
                    var prevCumulativeByLine = new decimal[signedOffsets.Count];
                    var cumulativePaid = 0m;

                    foreach (var payment in payments)
                    {
                        if (payment.Amount <= 0m || cumulativePaid >= billTotal)
                            continue;

                        var desiredSettle = Math.Min(payment.Amount, billTotal - cumulativePaid);
                        if (desiredSettle <= 0m)
                            continue;

                        var settledAmount = desiredSettle;
                        var recognitionDate = payment.Date;
                        int? paymentDrLineId = null;
                        decimal availablePaymentDebit = 0m;
                        if (payment.GeneralLedgerHeaderId.HasValue
                            && linesByHeader.TryGetValue(payment.GeneralLedgerHeaderId.Value, out var paymentLines))
                        {
                            var paymentDrLine = paymentLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == apLine.AccountId)
                                ?? paymentLines.FirstOrDefault(l => l.DrCr == DrOrCrSide.Dr);
                            if (paymentDrLine != null)
                            {
                                availablePaymentDebit = Math.Max(0m, paymentDrLine.Amount - suppressedLineAmounts.GetValueOrDefault(paymentDrLine.Id));
                                settledAmount = Math.Min(desiredSettle, availablePaymentDebit);
                                if (settledAmount <= 0m)
                                    continue;
                                paymentDrLineId = paymentDrLine.Id;
                                recognitionDate = paymentDrLine.HeaderDate;
                            }
                        }

                        var nextCumulativePaid = cumulativePaid + settledAmount;
                        var allocatedSumBeforeLast = 0m;
                        var deferredDeltaSum = 0m;
                        for (var i = 0; i < signedOffsets.Count; i++)
                        {
                            decimal delta;
                            if (i < signedOffsets.Count - 1)
                            {
                                var targetCumulative = Math.Round(signedOffsets[i].SignedAmount * nextCumulativePaid / billTotal, 2, MidpointRounding.AwayFromZero);
                                delta = targetCumulative - prevCumulativeByLine[i];
                                prevCumulativeByLine[i] = targetCumulative;
                                allocatedSumBeforeLast += delta;
                            }
                            else
                            {
                                delta = settledAmount - allocatedSumBeforeLast;
                                prevCumulativeByLine[i] += delta;
                            }

                            if (!signedOffsets[i].IsCapitalized)
                            {
                                deferredDeltaSum += delta;
                                if (delta > 0m)
                                    EmitPosting(signedOffsets[i].AccountId, DrOrCrSide.Dr, delta, recognitionDate, CashRecognitionSource.BillSettlement, bill.Id, null);
                                else if (delta < 0m)
                                    EmitPosting(signedOffsets[i].AccountId, DrOrCrSide.Cr, -delta, recognitionDate, CashRecognitionSource.BillSettlement, bill.Id, null);
                            }
                        }

                        if (paymentDrLineId.HasValue)
                        {
                            var suppressDebit = Math.Clamp(deferredDeltaSum, 0m, availablePaymentDebit);
                            suppressedLineAmounts[paymentDrLineId.Value] = suppressedLineAmounts.GetValueOrDefault(paymentDrLineId.Value) + suppressDebit;
                        }

                        cumulativePaid = nextCumulativePaid;
                    }
                }
            }

            foreach (var line in allGlLines)
            {
                var remaining = line.Amount - suppressedLineAmounts.GetValueOrDefault(line.Id);
                if (remaining > 0m)
                    EmitPosting(line.AccountId, line.DrCr, remaining, line.HeaderDate, CashRecognitionSource.Ledger, null, line.GeneralLedgerHeaderId);
            }

            return postings;
        }
    }
}
