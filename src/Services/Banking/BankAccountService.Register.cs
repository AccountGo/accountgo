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
using Dto.Banking;

namespace Services.Banking
{
    public partial class BankAccountService
    {
        public const int RegisterDefaultPageSize = 50;
        public const int RegisterMaxPageSize = 200;

        public BankRegisterDto GetRegister(int bankId, DateTime? from, DateTime? to, int page, int pageSize)
        {
            var bank = RequireBank(bankId);
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            if (page < 1)
                page = 1;
            if (pageSize < 1)
                pageSize = RegisterDefaultPageSize;
            if (pageSize > RegisterMaxPageSize)
                pageSize = RegisterMaxPageSize;

            var accountId = bank.AccountId.Value;
            var fromDate = from?.Date;
            var toDate = to?.Date;

            var ordered = (
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId
                orderby header.Date, header.Id, line.Id
                select new RegisterMovement(
                    line.Id,
                    header.Id,
                    header.Date,
                    header.DocumentType,
                    header.SourceDocumentId,
                    header.ReversedGeneralLedgerHeaderId,
                    header.Description,
                    line.DrCr,
                    line.Amount)
            ).ToList();

            decimal Signed(RegisterMovement m) =>
                m.DrCr == DrOrCrSide.Dr ? m.Amount : -m.Amount;

            var broughtForward = fromDate.HasValue
                ? ordered.Where(m => m.Date.Date < fromDate.Value).Sum(Signed)
                : 0m;

            var inRange = ordered.AsEnumerable();
            if (fromDate.HasValue)
                inRange = inRange.Where(m => m.Date.Date >= fromDate.Value);
            if (toDate.HasValue)
                inRange = inRange.Where(m => m.Date.Date <= toDate.Value);
            var ranged = inRange.ToList();
            var totalCount = ranged.Count;

            var pageItems = ranged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var balanceBeforePage = broughtForward
                + ranged.Take((page - 1) * pageSize).Sum(Signed);

            var enrichment = EnrichRegister(pageItems, bank);
            var running = balanceBeforePage;
            var lines = new List<BankRegisterLineDto>(pageItems.Count);
            foreach (var m in pageItems)
            {
                running += Signed(m);
                enrichment.TryGetValue(m.HeaderId, out var info);
                var isIn = m.DrCr == DrOrCrSide.Dr;
                lines.Add(new BankRegisterLineDto
                {
                    LineId = m.LineId,
                    HeaderId = m.HeaderId,
                    Date = m.Date,
                    Type = info?.TypeLabel ?? DocumentTypeLabel(m.DocumentType),
                    Payee = info?.Payee,
                    Reference = info?.Reference,
                    SourceDoc = info?.SourceDoc,
                    In = isIn ? m.Amount : 0m,
                    Out = isIn ? 0m : m.Amount,
                    Balance = running,
                    ReconciliationId = null
                });
            }

            return new BankRegisterDto
            {
                BroughtForward = broughtForward,
                Lines = lines,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        private Dictionary<int, RegisterEnrichment> EnrichRegister(IReadOnlyList<RegisterMovement> pageItems, Bank bank)
        {
            var result = new Dictionary<int, RegisterEnrichment>();
            if (pageItems.Count == 0)
                return result;

            // Identify OB by Bank.OpeningBalanceGeneralLedgerHeaderId (and prior OB posts of the same kind);
            // identify reversals by ReversedGeneralLedgerHeaderId pointing at those headers — never by description alone.
            var openingBalanceHeaderIds = new HashSet<int>();
            if (bank.OpeningBalanceGeneralLedgerHeaderId.HasValue)
                openingBalanceHeaderIds.Add(bank.OpeningBalanceGeneralLedgerHeaderId.Value);
            foreach (var m in pageItems)
            {
                if (m.DocumentType == DocumentTypes.BankTransaction
                    && string.Equals(m.Description, OpeningBalanceDescription, StringComparison.Ordinal)
                    && !m.ReversedGeneralLedgerHeaderId.HasValue)
                    openingBalanceHeaderIds.Add(m.HeaderId);
            }

            var reversedTargets = pageItems
                .Where(m => m.ReversedGeneralLedgerHeaderId.HasValue)
                .Select(m => m.ReversedGeneralLedgerHeaderId!.Value)
                .Where(id => !openingBalanceHeaderIds.Contains(id))
                .Distinct()
                .ToList();
            if (reversedTargets.Count > 0)
            {
                foreach (var id in _glHeaders.Table
                    .Where(h => reversedTargets.Contains(h.Id)
                                && h.DocumentType == DocumentTypes.BankTransaction
                                && h.Description == OpeningBalanceDescription)
                    .Select(h => h.Id))
                    openingBalanceHeaderIds.Add(id);
            }

            foreach (var m in pageItems)
            {
                if (openingBalanceHeaderIds.Contains(m.HeaderId))
                {
                    result[m.HeaderId] = OpeningBalanceEnrichment(bank.Id, isReversal: false);
                    continue;
                }

                if (m.ReversedGeneralLedgerHeaderId.HasValue
                    && openingBalanceHeaderIds.Contains(m.ReversedGeneralLedgerHeaderId.Value))
                    result[m.HeaderId] = OpeningBalanceEnrichment(bank.Id, isReversal: true);
            }

            var headerIds = pageItems.Select(m => m.HeaderId).Distinct().ToList();

            var bankTxs = _bankTransactions.Table
                .Where(t => t.GeneralLedgerHeaderId != null && headerIds.Contains(t.GeneralLedgerHeaderId.Value))
                .ToList();
            var bankTxPartyIds = bankTxs.Where(t => t.PartyId != null).Select(t => t.PartyId!.Value).Distinct().ToList();
            var bankTxParties = bankTxPartyIds.Count == 0
                ? new Dictionary<int, Party>()
                : _parties.Table.Where(p => bankTxPartyIds.Contains(p.Id)).ToDictionary(p => p.Id);
            foreach (var tx in bankTxs)
            {
                if (result.ContainsKey(tx.GeneralLedgerHeaderId!.Value))
                    continue;
                string? payee = null;
                if (tx.PartyId.HasValue && bankTxParties.TryGetValue(tx.PartyId.Value, out var party))
                    payee = party.Name;
                result[tx.GeneralLedgerHeaderId.Value] = new RegisterEnrichment(
                    BankTransactionService.KindLabel(tx.Kind),
                    payee,
                    tx.ReferenceNo ?? tx.Memo,
                    new BankRegisterSourceDocDto
                    {
                        Type = nameof(DocumentTypes.BankTransaction),
                        Id = tx.Id,
                        No = tx.No
                    });
            }

            var receipts = _receipts.Table
                .Where(r => r.GeneralLedgerHeaderId != null && headerIds.Contains(r.GeneralLedgerHeaderId.Value))
                .ToList();
            var receiptCustomerIds = receipts.Select(r => r.CustomerId).Distinct().ToList();
            var customers = _customers.Table.Where(c => receiptCustomerIds.Contains(c.Id)).ToList();
            var customerPartyIds = customers.Where(c => c.PartyId != null).Select(c => c.PartyId!.Value).Distinct().ToList();
            var customerParties = customerPartyIds.Count == 0
                ? new Dictionary<int, Party>()
                : _parties.Table.Where(p => customerPartyIds.Contains(p.Id)).ToDictionary(p => p.Id);
            var allocations = _allocations.Table
                .Where(a => receipts.Select(r => r.Id).Contains(a.SalesReceiptHeaderId))
                .ToList();
            var invoiceIds = allocations.Select(a => a.SalesInvoiceHeaderId).Distinct().ToList();
            var invoices = _salesInvoices.Table.Where(i => invoiceIds.Contains(i.Id)).ToDictionary(i => i.Id);

            foreach (var receipt in receipts)
            {
                if (result.ContainsKey(receipt.GeneralLedgerHeaderId!.Value))
                    continue;
                var customer = customers.FirstOrDefault(c => c.Id == receipt.CustomerId);
                string? payee = null;
                if (customer?.PartyId != null && customerParties.TryGetValue(customer.PartyId.Value, out var party))
                    payee = party.Name;

                var allocNos = allocations
                    .Where(a => a.SalesReceiptHeaderId == receipt.Id)
                    .Select(a => invoices.TryGetValue(a.SalesInvoiceHeaderId, out var inv) ? inv.No : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();
                var reference = !string.IsNullOrWhiteSpace(receipt.ReferenceNo)
                    ? receipt.ReferenceNo
                    : (allocNos.Count > 0 ? string.Join(", ", allocNos) : receipt.No);

                result[receipt.GeneralLedgerHeaderId!.Value] = new RegisterEnrichment(
                    DocumentTypeLabel(DocumentTypes.SalesReceipt),
                    payee,
                    reference,
                    new BankRegisterSourceDocDto
                    {
                        Type = nameof(DocumentTypes.SalesReceipt),
                        Id = receipt.Id,
                        No = receipt.No
                    });
            }

            var payments = _vendorPayments.Table
                .Where(p => p.GeneralLedgerHeaderId != null && headerIds.Contains(p.GeneralLedgerHeaderId.Value))
                .ToList();
            var vendorIds = payments.Select(p => p.VendorId).Distinct().ToList();
            var vendors = _vendors.Table.Where(v => vendorIds.Contains(v.Id)).ToList();
            var vendorPartyIds = vendors.Where(v => v.PartyId != null).Select(v => v.PartyId!.Value).Distinct().ToList();
            var vendorParties = vendorPartyIds.Count == 0
                ? new Dictionary<int, Party>()
                : _parties.Table.Where(p => vendorPartyIds.Contains(p.Id)).ToDictionary(p => p.Id);
            var billIds = payments.Where(p => p.PurchaseInvoiceHeaderId != null).Select(p => p.PurchaseInvoiceHeaderId!.Value).Distinct().ToList();
            var bills = _purchaseInvoices.Table.Where(b => billIds.Contains(b.Id)).ToDictionary(b => b.Id);

            foreach (var payment in payments)
            {
                if (result.ContainsKey(payment.GeneralLedgerHeaderId!.Value))
                    continue;
                var vendor = vendors.FirstOrDefault(v => v.Id == payment.VendorId);
                string? payee = null;
                if (vendor?.PartyId != null && vendorParties.TryGetValue(vendor.PartyId.Value, out var party))
                    payee = party.Name;

                string? billNo = null;
                int sourceId = payment.Id;
                string sourceType = nameof(DocumentTypes.PurchaseInvoicePayment);
                if (payment.PurchaseInvoiceHeaderId.HasValue
                    && bills.TryGetValue(payment.PurchaseInvoiceHeaderId.Value, out var bill))
                {
                    billNo = bill.No;
                    sourceId = bill.Id;
                    sourceType = nameof(DocumentTypes.PurchaseInvoice);
                }

                result[payment.GeneralLedgerHeaderId!.Value] = new RegisterEnrichment(
                    DocumentTypeLabel(DocumentTypes.PurchaseInvoicePayment),
                    payee,
                    string.IsNullOrWhiteSpace(payment.ReferenceNo) ? billNo ?? payment.No : payment.ReferenceNo,
                    new BankRegisterSourceDocDto
                    {
                        Type = sourceType,
                        Id = sourceId,
                        No = billNo ?? payment.No
                    });
            }

            var journals = _journals.Table
                .Where(j => j.GeneralLedgerHeaderId != null && headerIds.Contains(j.GeneralLedgerHeaderId.Value))
                .ToList();
            var journalPartyIds = journals.Where(j => j.PartyId != null).Select(j => j.PartyId!.Value).Distinct().ToList();
            var journalParties = _parties.Table.Where(p => journalPartyIds.Contains(p.Id)).ToDictionary(p => p.Id);
            foreach (var journal in journals)
            {
                if (result.ContainsKey(journal.GeneralLedgerHeaderId!.Value))
                    continue;
                string? payee = null;
                if (journal.PartyId.HasValue && journalParties.TryGetValue(journal.PartyId.Value, out var party))
                    payee = party.Name;
                result[journal.GeneralLedgerHeaderId.Value] = new RegisterEnrichment(
                    DocumentTypeLabel(DocumentTypes.JournalEntry),
                    payee,
                    journal.ReferenceNo ?? journal.Memo,
                    new BankRegisterSourceDocDto
                    {
                        Type = nameof(DocumentTypes.JournalEntry),
                        Id = journal.Id,
                        No = journal.ReferenceNo
                    });
            }

            foreach (var m in pageItems)
            {
                if (result.ContainsKey(m.HeaderId))
                    continue;

                result[m.HeaderId] = new RegisterEnrichment(
                    DocumentTypeLabel(m.DocumentType),
                    null,
                    m.Description,
                    m.SourceDocumentId.HasValue
                        ? new BankRegisterSourceDocDto
                        {
                            Type = m.DocumentType.ToString(),
                            Id = m.SourceDocumentId.Value,
                            No = null
                        }
                        : null);
            }

            return result;
        }

        private static RegisterEnrichment OpeningBalanceEnrichment(int bankId, bool isReversal) =>
            new(
                isReversal ? "Opening balance reversal" : "Opening balance",
                null,
                null,
                new BankRegisterSourceDocDto
                {
                    Type = nameof(DocumentTypes.BankTransaction),
                    Id = bankId,
                    No = null
                });

        private static string DocumentTypeLabel(DocumentTypes type) => type switch
        {
            DocumentTypes.SalesReceipt => "Receipt",
            DocumentTypes.PurchaseInvoicePayment => "Bill payment",
            DocumentTypes.JournalEntry => "Journal",
            DocumentTypes.SalesInvoice => "Invoice",
            DocumentTypes.PurchaseInvoice => "Bill",
            DocumentTypes.BankTransaction => "Bank transaction",
            _ => type.ToString()
        };

        private readonly record struct RegisterMovement(
            int LineId,
            int HeaderId,
            DateTime Date,
            DocumentTypes DocumentType,
            int? SourceDocumentId,
            int? ReversedGeneralLedgerHeaderId,
            string? Description,
            DrOrCrSide DrCr,
            decimal Amount);

        private sealed record RegisterEnrichment(
            string TypeLabel,
            string? Payee,
            string? Reference,
            BankRegisterSourceDocDto? SourceDoc);
    }
}
