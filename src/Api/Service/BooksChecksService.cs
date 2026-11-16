// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Api.Data;
using Core.Domain;
using Dto.Financial;
using Microsoft.EntityFrameworkCore;
using Services.Banking;

namespace Api.Service
{
    /// <summary>Which areas the caller may read. A check in an area the caller cannot read is reported as not checked.</summary>
    public sealed record BooksChecksAccess(bool Ledger, bool Sales, bool Purchases, bool Banking, bool History);

    /// <summary>
    /// Runs the repeatable checks of a books review for one period: ledger integrity, receivables and payables against
    /// their control accounts, documents missing from the ledger, duplicates, manual journals worth a look, and bank
    /// reconciliation coverage. It only reads. Every figure is computed here so a reviewer does not add rows by hand.
    /// </summary>
    public sealed class BooksChecksService
    {
        private const int MaxExamples = 10;
        private const decimal Tolerance = 0.005m;
        /// <summary>Manual journals dated this close to the period end are listed for review.</summary>
        public const int PeriodEndDays = 3;
        /// <summary>A reversal this soon after the period end of an entry inside the period is listed for review.</summary>
        public const int ReversalWindowDays = 15;

        private readonly ApiDbContext _db;
        private readonly IBankAccountService _banks;

        public BooksChecksService(ApiDbContext db, IBankAccountService banks)
        {
            _db = db;
            _banks = banks;
        }

        public BooksChecksResult Run(int companyId, DateTime from, DateTime to, BooksChecksAccess access)
        {
            var start = from.Date;
            var end = to.Date;
            var endExclusive = end.AddDays(1);
            var result = new BooksChecksResult { From = start, To = end };

            // Net debit balance per account through the period end, summed in the database.
            var balances = new Dictionary<int, decimal>();
            if (access.Ledger)
            {
                var sums = _db.GeneralLedgerLines.AsNoTracking()
                    .Where(l => l.GeneralLedgerHeader.Date < endExclusive)
                    .GroupBy(l => new { l.AccountId, l.DrCr })
                    .Select(g => new { g.Key.AccountId, g.Key.DrCr, Total = g.Sum(l => l.Amount) })
                    .ToList();
                foreach (var sum in sums)
                    balances[sum.AccountId] = balances.GetValueOrDefault(sum.AccountId) + (sum.DrCr == DrOrCrSide.Dr ? sum.Total : -sum.Total);
            }

            var receivableAccounts = _db.Customers.AsNoTracking().Where(c => c.AccountsReceivableAccountId != null)
                .Select(c => c.AccountsReceivableAccountId!.Value).Distinct().ToList().ToHashSet();
            var payableAccounts = _db.Vendors.AsNoTracking().Where(v => v.AccountsPayableAccountId != null)
                .Select(v => v.AccountsPayableAccountId!.Value).Distinct().ToList().ToHashSet();
            foreach (var id in _db.PurchaseInvoiceHeaders.AsNoTracking().Where(b => b.PayableAccountId != null)
                         .Select(b => b.PayableAccountId!.Value).Distinct().ToList())
                payableAccounts.Add(id);
            var bankAccounts = _db.Banks.AsNoTracking().Where(b => b.AccountId != null)
                .Select(b => b.AccountId!.Value).Distinct().ToList().ToHashSet();

            result.Checks.Add(access.Ledger ? LedgerBalanced(endExclusive) : NotChecked("ledger.balanced", "ledger", "amount", "Every ledger entry balances"));
            result.Checks.Add(access.Ledger ? WrongSideBalances(balances) : NotChecked("ledger.wrongSide", "ledger", "account", "Balances on the unusual side"));

            if (access.Sales)
            {
                result.Checks.Add(access.Ledger
                    ? ReceivablesTieOut(end, endExclusive, balances, receivableAccounts)
                    : NotChecked("sales.receivablesTieOut", "sales", "amount", "Receivables agree with the ledger"));
                result.Checks.Add(UnpostedInvoices(endExclusive));
                result.Checks.Add(DuplicateInvoiceNumbers());
            }
            else
            {
                result.Checks.Add(NotChecked("sales.receivablesTieOut", "sales", "amount", "Receivables agree with the ledger"));
                result.Checks.Add(NotChecked("sales.unposted", "sales", "missing", "Issued invoices missing from the ledger"));
                result.Checks.Add(NotChecked("sales.duplicateNumbers", "sales", "real", "Invoice numbers used more than once"));
            }

            if (access.Purchases)
            {
                result.Checks.Add(access.Ledger
                    ? PayablesTieOut(end, endExclusive, balances, payableAccounts)
                    : NotChecked("purchases.payablesTieOut", "purchases", "amount", "Payables agree with the ledger"));
                result.Checks.Add(UnpostedBills(endExclusive));
                result.Checks.AddRange(DuplicateBills(start, endExclusive));
            }
            else
            {
                result.Checks.Add(NotChecked("purchases.payablesTieOut", "purchases", "amount", "Payables agree with the ledger"));
                result.Checks.Add(NotChecked("purchases.unposted", "purchases", "missing", "Approved bills missing from the ledger"));
                result.Checks.Add(NotChecked("purchases.duplicateVendorInvoice", "purchases", "real", "Same vendor invoice number entered twice"));
                result.Checks.Add(NotChecked("purchases.sameDayAmount", "purchases", "real", "Same vendor, date and amount"));
            }

            if (access.Ledger)
                result.Checks.AddRange(Journals(companyId, start, end, endExclusive, receivableAccounts, payableAccounts, bankAccounts, access.History));
            else
                foreach (var (id, title) in new[]
                         {
                             ("journals.periodEnd", "Manual journals dated at the period end"),
                             ("journals.noMemo", "Manual journals without a memo"),
                             ("journals.controlAccounts", "Manual journals posted to receivables, payables or bank accounts"),
                             ("journals.reversedAfterPeriodEnd", "Entries reversed soon after the period end"),
                             ("journals.postedByPreparer", "Manual journals posted by the person who prepared them")
                         })
                    result.Checks.Add(NotChecked(id, "journals", "control", title));

            if (access.Banking)
            {
                result.Checks.Add(BankReconciled(end));
                result.Checks.Add(UnmatchedStatementLines(endExclusive));
            }
            else
            {
                result.Checks.Add(NotChecked("bank.reconciled", "bank", "real", "Bank accounts reconciled through the period end"));
                result.Checks.Add(NotChecked("bank.unmatchedLines", "bank", "missing", "Imported bank lines not yet in the books"));
            }

            return result;
        }

        private BooksCheck LedgerBalanced(DateTime endExclusive)
        {
            var unbalanced = _db.GeneralLedgerHeaders.AsNoTracking()
                .Where(h => h.Date < endExclusive)
                .Select(h => new
                {
                    h.Id,
                    h.Date,
                    Debit = h.GeneralLedgerLines.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => (decimal?)l.Amount) ?? 0,
                    Credit = h.GeneralLedgerLines.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => (decimal?)l.Amount) ?? 0
                })
                .Where(h => h.Debit != h.Credit)
                .OrderBy(h => h.Id)
                .ToList();

            var check = New("ledger.balanced", "ledger", "amount", "Every ledger entry balances");
            if (unbalanced.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = unbalanced.Count;
            check.Amount = unbalanced.Sum(h => Math.Abs(h.Debit - h.Credit));
            check.Detail = "Ledger entries whose debits and credits differ. The trial balance cannot agree until these are corrected.";
            check.Examples = unbalanced.Take(MaxExamples)
                .Select(h => Example($"Transaction #{h.Id}", $"/financials?transactionNo={h.Id}", h.Date, Math.Abs(h.Debit - h.Credit))).ToList();
            return check;
        }

        private BooksCheck WrongSideBalances(Dictionary<int, decimal> balances)
        {
            var check = New("ledger.wrongSide", "ledger", "account", "Balances on the unusual side");
            if (balances.Count == 0) return check;

            // Equity moves either way with profit and loss, so only asset, liability, revenue and expense accounts are tested.
            var tested = new[] { (int)AccountClasses.Assets, (int)AccountClasses.Liabilities, (int)AccountClasses.Revenue, (int)AccountClasses.Expense };
            var ids = balances.Keys.ToList();
            var accounts = _db.Accounts.AsNoTracking()
                .Where(a => ids.Contains(a.Id) && tested.Contains(a.AccountClassId) && !a.IsContraAccount)
                .Select(a => new { a.Id, a.AccountCode, a.AccountName, a.DrOrCrSide })
                .ToList();
            var wrong = accounts
                .Select(a => new { Account = a, Net = balances[a.Id] })
                .Where(x => x.Account.DrOrCrSide == DrOrCrSide.Dr ? x.Net < -Tolerance : x.Net > Tolerance)
                .OrderByDescending(x => Math.Abs(x.Net))
                .ToList();
            if (wrong.Count == 0) return check;

            check.Status = BooksCheckStatus.Review;
            check.Count = wrong.Count;
            check.Detail = "Accounts whose balance at the period end is on the opposite side to normal, such as a bank or receivable in credit or an expense in credit. Each needs an explanation.";
            check.Examples = wrong.Take(MaxExamples)
                .Select(x => Example($"{x.Account.AccountCode} {x.Account.AccountName}", "/financials?accountCode=" + Uri.EscapeDataString(x.Account.AccountCode), null, x.Net)).ToList();
            return check;
        }

        private BooksCheck ReceivablesTieOut(DateTime end, DateTime endExclusive, Dictionary<int, decimal> balances, HashSet<int> receivableAccounts)
        {
            var invoices = _db.SalesInvoiceHeaders.AsNoTracking()
                .Include(i => i.SalesInvoiceLines)
                .Include(i => i.CustomerAllocations)
                .Where(i => i.Date < endExclusive && i.GeneralLedgerHeaderId != null)
                .ToList();
            var voidIds = invoices.Where(i => i.VoidGeneralLedgerHeaderId.HasValue).Select(i => i.VoidGeneralLedgerHeaderId!.Value).ToList();
            var voidDates = _db.GeneralLedgerHeaders.AsNoTracking().Where(h => voidIds.Contains(h.Id))
                .Select(h => new { h.Id, h.Date }).ToDictionary(h => h.Id, h => h.Date);

            var owed = invoices.Sum(i => i.BalanceDueAsOf(end,
                i.VoidGeneralLedgerHeaderId.HasValue && voidDates.TryGetValue(i.VoidGeneralLedgerHeaderId.Value, out var date) ? date : null));
            var control = receivableAccounts.Sum(id => balances.GetValueOrDefault(id));
            return TieOut("sales.receivablesTieOut", "sales", "Receivables agree with the ledger", owed, control,
                "what posted invoices show as owed", "the receivables accounts in the ledger");
        }

        private BooksCheck PayablesTieOut(DateTime end, DateTime endExclusive, Dictionary<int, decimal> balances, HashSet<int> payableAccounts)
        {
            var bills = _db.PurchaseInvoiceHeaders.AsNoTracking()
                .Include(b => b.PurchaseInvoiceLines)
                .Include(b => b.VoidGeneralLedgerHeader)
                .Include(b => b.VendorPayments).ThenInclude(p => p.VoidGeneralLedgerHeader)
                .Where(b => b.Date < endExclusive && b.GeneralLedgerHeaderId != null)
                .ToList();
            var owed = bills.Sum(b => b.BalanceDueAsOf(end));
            var control = -payableAccounts.Sum(id => balances.GetValueOrDefault(id));
            return TieOut("purchases.payablesTieOut", "purchases", "Payables agree with the ledger", owed, control,
                "what posted bills show as owed", "the payables accounts in the ledger");
        }

        private static BooksCheck TieOut(string id, string area, string title, decimal documents, decimal ledger, string documentsLabel, string ledgerLabel)
        {
            var check = New(id, area, "amount", title);
            var difference = Math.Round(ledger - documents, 2, MidpointRounding.AwayFromZero);
            check.Amount = difference;
            check.Detail = string.Format(CultureInfo.InvariantCulture,
                "At the period end {0} totals {1:0.00} and {2} total {3:0.00}.", documentsLabel, documents, ledgerLabel, ledger);
            if (Math.Abs(difference) > Tolerance)
            {
                check.Status = BooksCheckStatus.Finding;
                check.Count = 1;
                check.Detail += " The usual causes are a manual journal posted straight to the control account, or a document whose posting differs from its total.";
            }
            return check;
        }

        private BooksCheck UnpostedInvoices(DateTime endExclusive)
        {
            var rows = _db.SalesInvoiceHeaders.AsNoTracking()
                .Where(i => i.Date < endExclusive && i.Status != null && i.Status != SalesInvoiceStatus.Draft
                    && i.Status != SalesInvoiceStatus.Void && i.GeneralLedgerHeaderId == null)
                .OrderBy(i => i.Date).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.No, i.Date })
                .ToList();
            var check = New("sales.unposted", "sales", "missing", "Issued invoices missing from the ledger");
            if (rows.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = rows.Count;
            check.Detail = "Issued invoices with no ledger entry. Their revenue and receivable are absent from every report.";
            check.Examples = rows.Take(MaxExamples).Select(i => Example($"Invoice {i.No}", $"/sales/{i.Id}", i.Date, null)).ToList();
            return check;
        }

        private BooksCheck UnpostedBills(DateTime endExclusive)
        {
            var rows = _db.PurchaseInvoiceHeaders.AsNoTracking()
                .Where(b => b.Date < endExclusive && b.Status != null && b.Status != PurchaseInvoiceStatus.Draft
                    && b.Status != PurchaseInvoiceStatus.Void && b.GeneralLedgerHeaderId == null)
                .OrderBy(b => b.Date).ThenBy(b => b.Id)
                .Select(b => new { b.Id, b.No, b.Date })
                .ToList();
            var check = New("purchases.unposted", "purchases", "missing", "Approved bills missing from the ledger");
            if (rows.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = rows.Count;
            check.Detail = "Approved bills with no ledger entry. Their cost and payable are absent from every report.";
            check.Examples = rows.Take(MaxExamples).Select(b => Example($"Bill {b.No}", $"/purchases/{b.Id}", b.Date, null)).ToList();
            return check;
        }

        private BooksCheck DuplicateInvoiceNumbers()
        {
            var rows = _db.SalesInvoiceHeaders.AsNoTracking()
                .Where(i => i.No != null && i.No != "" && i.Status != SalesInvoiceStatus.Void && i.Status != SalesInvoiceStatus.Draft)
                .Select(i => new { i.Id, i.No, i.Date })
                .ToList();
            var groups = rows.GroupBy(i => i.No.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
            var check = New("sales.duplicateNumbers", "sales", "real", "Invoice numbers used more than once");
            if (groups.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = groups.Count;
            check.Detail = "The same invoice number appears on more than one issued invoice.";
            check.Examples = groups.SelectMany(g => g).Take(MaxExamples).Select(i => Example($"Invoice {i.No}", $"/sales/{i.Id}", i.Date, null)).ToList();
            return check;
        }

        private IEnumerable<BooksCheck> DuplicateBills(DateTime start, DateTime endExclusive)
        {
            var bills = _db.PurchaseInvoiceHeaders.AsNoTracking()
                .Include(b => b.PurchaseInvoiceLines)
                .Where(b => b.VendorId != null && b.Status != null && b.Status != PurchaseInvoiceStatus.Draft && b.Status != PurchaseInvoiceStatus.Void)
                .ToList();

            var sameNumber = bills.Where(b => !string.IsNullOrWhiteSpace(b.VendorInvoiceNo))
                .GroupBy(b => (b.VendorId, Number: b.VendorInvoiceNo.Trim().ToUpperInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();
            var byNumber = New("purchases.duplicateVendorInvoice", "purchases", "real", "Same vendor invoice number entered twice");
            if (sameNumber.Count > 0)
            {
                byNumber.Status = BooksCheckStatus.Finding;
                byNumber.Count = sameNumber.Count;
                byNumber.Amount = sameNumber.Sum(g => g.OrderBy(b => b.Id).Skip(1).Sum(b => b.Total()));
                byNumber.Detail = "More than one approved bill from the same vendor carries the same vendor invoice number. The amount is the total of the later copies.";
                byNumber.Examples = sameNumber.SelectMany(g => g).Take(MaxExamples)
                    .Select(b => Example($"Bill {b.No} (vendor invoice {b.VendorInvoiceNo})", $"/purchases/{b.Id}", b.Date, b.Total())).ToList();
            }
            yield return byNumber;

            var flagged = sameNumber.SelectMany(g => g).Select(b => b.Id).ToHashSet();
            var sameDay = bills.Where(b => b.Date >= start && b.Date < endExclusive && !flagged.Contains(b.Id))
                .GroupBy(b => (b.VendorId, b.Date.Date, Total: b.Total()))
                .Where(g => g.Count() > 1 && g.Key.Total > 0)
                .ToList();
            var byAmount = New("purchases.sameDayAmount", "purchases", "real", "Same vendor, date and amount");
            if (sameDay.Count > 0)
            {
                byAmount.Status = BooksCheckStatus.Review;
                byAmount.Count = sameDay.Count;
                byAmount.Detail = "Bills in the period from the same vendor on the same date for the same amount. They may be genuine repeat purchases or one bill entered twice.";
                byAmount.Examples = sameDay.SelectMany(g => g).Take(MaxExamples)
                    .Select(b => Example($"Bill {b.No}", $"/purchases/{b.Id}", b.Date, b.Total())).ToList();
            }
            yield return byAmount;
        }

        private IEnumerable<BooksCheck> Journals(int companyId, DateTime start, DateTime end, DateTime endExclusive,
            HashSet<int> receivableAccounts, HashSet<int> payableAccounts, HashSet<int> bankAccounts, bool history)
        {
            var journals = _db.JournalEntryHeaders.AsNoTracking()
                .Where(j => j.GeneralLedgerHeaderId != null && j.Date >= start && j.Date < endExclusive)
                .Select(j => new
                {
                    j.Id,
                    j.Date,
                    j.Memo,
                    j.ReferenceNo,
                    Accounts = j.JournalEntryLines.Select(l => l.AccountId).ToList(),
                    Debit = j.JournalEntryLines.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => (decimal?)l.Amount) ?? 0
                })
                .ToList();
            BooksCheckExample JournalExample(int id, DateTime date, string? reference, decimal amount) =>
                Example(string.IsNullOrWhiteSpace(reference) ? $"Journal #{id}" : $"Journal #{id} ({reference})", $"/financials/journals?id={id}", date, amount);

            var periodEndFrom = end.AddDays(-(PeriodEndDays - 1));
            var late = journals.Where(j => j.Date.Date >= periodEndFrom).OrderByDescending(j => j.Debit).ToList();
            var periodEnd = New("journals.periodEnd", "journals", "period", "Manual journals dated at the period end");
            if (late.Count > 0)
            {
                periodEnd.Status = BooksCheckStatus.Review;
                periodEnd.Count = late.Count;
                periodEnd.Amount = late.Sum(j => j.Debit);
                periodEnd.Detail = $"Manual journals dated in the last {PeriodEndDays} days of the period, largest first. Period-end adjustments deserve a look at what supports them.";
                periodEnd.Examples = late.Take(MaxExamples).Select(j => JournalExample(j.Id, j.Date, j.ReferenceNo, j.Debit)).ToList();
            }
            yield return periodEnd;

            var blank = journals.Where(j => string.IsNullOrWhiteSpace(j.Memo)).OrderByDescending(j => j.Debit).ToList();
            var noMemo = New("journals.noMemo", "journals", "control", "Manual journals without a memo");
            if (blank.Count > 0)
            {
                noMemo.Status = BooksCheckStatus.Review;
                noMemo.Count = blank.Count;
                noMemo.Amount = blank.Sum(j => j.Debit);
                noMemo.Detail = "Posted manual journals in the period with no memo explaining their purpose.";
                noMemo.Examples = blank.Take(MaxExamples).Select(j => JournalExample(j.Id, j.Date, j.ReferenceNo, j.Debit)).ToList();
            }
            yield return noMemo;

            var control = journals.Where(j => j.Accounts.Any(a => receivableAccounts.Contains(a) || payableAccounts.Contains(a) || bankAccounts.Contains(a)))
                .OrderByDescending(j => j.Debit).ToList();
            var controlAccounts = New("journals.controlAccounts", "journals", "account", "Manual journals posted to receivables, payables or bank accounts");
            if (control.Count > 0)
            {
                controlAccounts.Status = BooksCheckStatus.Review;
                controlAccounts.Count = control.Count;
                controlAccounts.Amount = control.Sum(j => j.Debit);
                controlAccounts.Detail = "Manual journals in the period that post to an account normally moved only by invoices, bills, receipts, payments or bank entries. They bypass those documents and can explain a tie-out difference.";
                controlAccounts.Examples = control.Take(MaxExamples).Select(j => JournalExample(j.Id, j.Date, j.ReferenceNo, j.Debit)).ToList();
            }
            yield return controlAccounts;

            var windowEnd = endExclusive.AddDays(ReversalWindowDays);
            var reversals = _db.GeneralLedgerHeaders.AsNoTracking()
                .Where(h => h.ReversedGeneralLedgerHeaderId != null && h.Date >= endExclusive && h.Date < windowEnd)
                .Join(_db.GeneralLedgerHeaders.AsNoTracking().Where(o => o.Date >= start && o.Date < endExclusive),
                    r => r.ReversedGeneralLedgerHeaderId, o => o.Id,
                    (r, o) => new { ReversalId = r.Id, ReversalDate = r.Date, OriginalId = o.Id })
                .OrderBy(r => r.ReversalId)
                .ToList();
            var reversed = New("journals.reversedAfterPeriodEnd", "journals", "period", "Entries reversed soon after the period end");
            if (reversals.Count > 0)
            {
                reversed.Status = BooksCheckStatus.Review;
                reversed.Count = reversals.Count;
                reversed.Detail = $"Ledger entries dated in the period that were reversed within {ReversalWindowDays} days after it. Ask whether each belonged in the period at all.";
                reversed.Examples = reversals.Take(MaxExamples)
                    .Select(r => Example($"Transaction #{r.OriginalId} reversed by #{r.ReversalId}", $"/financials?transactionNo={r.ReversalId}", r.ReversalDate, null)).ToList();
            }
            yield return reversed;

            if (!history)
            {
                yield return NotChecked("journals.postedByPreparer", "journals", "control", "Manual journals posted by the person who prepared them");
                yield break;
            }

            var journalIds = journals.Select(j => j.Id).ToList();
            var recordIds = journalIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
            var preparers = _db.AuditLogs.AsNoTracking()
                .Where(a => a.CompanyId == companyId && a.TableName == "__JournalEntryPreparer" && recordIds.Contains(a.RecordId))
                .Select(a => new { a.RecordId, a.UserName })
                .ToList()
                .GroupBy(a => a.RecordId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.UserName).ToHashSet(StringComparer.Ordinal));
            var posters = _db.RecordChanges.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.EntityType == "JournalEntryHeader" && journalIds.Contains(c.EntityId)
                    && (c.Action == "Added" || c.Action == "Modified"))
                .OrderBy(c => c.Id)
                .Select(c => new { c.EntityId, c.Action, c.ChangesJson, c.ActorUserId })
                .ToList()
                .Where(c => JournalAttributionReader.TurnedPostedOn(c.Action, c.ChangesJson))
                .GroupBy(c => c.EntityId)
                .ToDictionary(g => g.Key, g => g.Last().ActorUserId);

            var self = journals.Where(j => posters.TryGetValue(j.Id, out var poster) && poster != null
                    && preparers.TryGetValue(j.Id.ToString(CultureInfo.InvariantCulture), out var who) && who.Contains(poster))
                .OrderByDescending(j => j.Debit).ToList();
            var unknown = journals.Count(j => !posters.TryGetValue(j.Id, out var poster) || poster == null);
            var selfPosted = New("journals.postedByPreparer", "journals", "control", "Manual journals posted by the person who prepared them");
            selfPosted.Detail = $"{journals.Count} manual journals were posted in the period; who posted is not recorded for {unknown} of them.";
            if (self.Count > 0)
            {
                selfPosted.Status = BooksCheckStatus.Review;
                selfPosted.Count = self.Count;
                selfPosted.Amount = self.Sum(j => j.Debit);
                selfPosted.Detail += " The listed journals were prepared and posted by the same person, so nobody else checked them. That is expected when the workspace allows single-user posting.";
                selfPosted.Examples = self.Take(MaxExamples).Select(j => JournalExample(j.Id, j.Date, j.ReferenceNo, j.Debit)).ToList();
            }
            yield return selfPosted;
        }

        private BooksCheck BankReconciled(DateTime end)
        {
            var check = New("bank.reconciled", "bank", "real", "Bank accounts reconciled through the period end");
            var status = _banks.GetCloseStatus(end);
            if (status == null)
            {
                check.Detail = "There are no bank accounts to reconcile.";
                return check;
            }

            var open = status.Banks.Where(b => !b.IsReconciled).ToList();
            check.Detail = $"{status.ReconciledCount} of {status.TotalCount} bank accounts are reconciled through {status.PeriodEnd:yyyy-MM-dd}.";
            if (open.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = open.Count;
            check.Examples = open.Take(MaxExamples)
                .Select(b => Example(b.ReconciledThrough.HasValue ? $"{b.Name} (reconciled through {b.ReconciledThrough:yyyy-MM-dd})" : $"{b.Name} (never reconciled)",
                    $"/banking/{b.BankId}/reconcile", b.ReconciledThrough, null)).ToList();
            return check;
        }

        private BooksCheck UnmatchedStatementLines(DateTime endExclusive)
        {
            var lines = _db.BankStatementLines.AsNoTracking()
                .Where(l => l.PostedDate < endExclusive && l.MatchedGeneralLedgerLineId == null && l.ExcludedAt == null)
                .OrderBy(l => l.PostedDate).ThenBy(l => l.Id)
                .Select(l => new { l.Id, l.BankId, l.PostedDate, l.Amount, l.Payee, l.Memo })
                .ToList();
            var check = New("bank.unmatchedLines", "bank", "missing", "Imported bank lines not yet in the books");
            if (lines.Count == 0) return check;
            check.Status = BooksCheckStatus.Finding;
            check.Count = lines.Count;
            check.Amount = lines.Sum(l => Math.Abs(l.Amount));
            check.Detail = "Imported statement lines dated on or before the period end that are neither matched to a ledger entry nor excluded. Each is money that moved and may be missing from the books. The amount is the total ignoring direction.";
            check.Examples = lines.Take(MaxExamples)
                .Select(l => Example(string.IsNullOrWhiteSpace(l.Payee) ? (l.Memo ?? $"Statement line #{l.Id}") : l.Payee!,
                    $"/banking/{l.BankId}/statement-import", l.PostedDate, l.Amount)).ToList();
            return check;
        }

        private static BooksCheck New(string id, string area, string question, string title) =>
            new() { Id = id, Area = area, Question = question, Title = title, Status = BooksCheckStatus.Pass };

        private static BooksCheck NotChecked(string id, string area, string question, string title) =>
            new()
            {
                Id = id, Area = area, Question = question, Title = title, Status = BooksCheckStatus.NotChecked,
                Detail = "Not checked: your access does not include this area."
            };

        private static BooksCheckExample Example(string label, string? path, DateTime? date, decimal? amount) =>
            new() { Label = label, Path = path, Date = date, Amount = amount };
    }
}
