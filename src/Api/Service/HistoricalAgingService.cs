// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>
    /// Outstanding AR/AP at a cutoff. Balances use the same dated allocation and void rules as close.
    /// A denied section is omitted rather than reported as zero.
    /// </summary>
    public sealed class HistoricalAgingService
    {
        private readonly ApiDbContext _db;
        public HistoricalAgingService(ApiDbContext db) => _db = db;

        public object Build(DateTime asOf, bool includeReceivables, bool includePayables)
        {
            var day = asOf.Date;
            var endExclusive = day.AddDays(1);
            return new
            {
                asOf = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                label = "Outstanding balances as of " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "; GL tie-out on accrual ledger basis",
                receivables = includeReceivables ? (object)Section(day, endExclusive, receivable: true) : new { denied = true },
                payables = includePayables ? (object)Section(day, endExclusive, receivable: false) : new { denied = true }
            };
        }

        private object Section(DateTime day, DateTime endExclusive, bool receivable)
        {
            var buckets = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["current"] = 0m, ["d1to30"] = 0m, ["d31to60"] = 0m, ["d61to90"] = 0m, ["over90"] = 0m
            };
            decimal posted = 0m, unposted = 0m;
            var count = 0;
            if (receivable)
            {
                var invoices = _db.SalesInvoiceHeaders.AsNoTracking()
                    .Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).Include(i => i.VoidGeneralLedgerHeader)
                    .Where(i => i.Date < endExclusive && i.Status != SalesInvoiceStatus.Draft)
                    .ToList();
                var appSettledByInvoice = _db.SettlementApplications.AsNoTracking()
                    .Include(a => a.Reversal)
                    .Where(a => a.DestinationType == SettlementDestinationType.SalesInvoice && a.SalesInvoiceHeaderId != null && a.EffectiveDate < endExclusive)
                    .ToList()
                    .Where(a => !a.IsReversed || (a.Reversal != null && a.Reversal.ReversalDate.Date > day))
                    .GroupBy(a => a.SalesInvoiceHeaderId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
                var writeOffsByInvoice = _db.ReceivableWriteOffs.AsNoTracking()
                    .Include(w => w.VoidGeneralLedgerHeader)
                    .Where(w => w.Status != SettlementDocumentStatus.Draft && w.Status != SettlementDocumentStatus.PendingReview && w.Date < endExclusive)
                    .ToList()
                    .Where(w => w.Status != SettlementDocumentStatus.Reversed || (w.VoidGeneralLedgerHeader != null && w.VoidGeneralLedgerHeader.Date.Date > day))
                    .GroupBy(w => w.SalesInvoiceHeaderId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

                foreach (var invoice in invoices)
                {
                    var cashAlloc = invoice.CustomerAllocations?.Where(a => a.Date.Date <= day).Sum(a => a.Amount) ?? 0m;
                    var creditSettled = appSettledByInvoice.TryGetValue(invoice.Id, out var cs) ? cs : 0m;
                    var writtenOff = writeOffsByInvoice.TryGetValue(invoice.Id, out var wo) ? wo : 0m;
                    var amount = Signed(invoice.GrandTotal(), cashAlloc + creditSettled + writtenOff,
                        invoice.Status == SalesInvoiceStatus.Void, invoice.VoidGeneralLedgerHeader?.Date, invoice.Date, day);
                    if (Math.Abs(amount) <= 0.005m) continue;
                    count++;
                    var bucket = Bucket(invoice.DueDate?.Date ?? invoice.Date.Date.AddDays(30), day);
                    buckets[bucket] += amount;
                    if (invoice.GeneralLedgerHeaderId == null) unposted += amount;
                    else posted += amount;
                }
            }
            else
            {
                var bills = _db.PurchaseInvoiceHeaders.AsNoTracking()
                    .Include(b => b.PurchaseInvoiceLines).Include(b => b.VendorPayments).ThenInclude(p => p.VoidGeneralLedgerHeader)
                    .Include(b => b.VoidGeneralLedgerHeader)
                    .Where(b => b.Date < endExclusive)
                    .ToList();
                var appSettledByBill = _db.SettlementApplications.AsNoTracking()
                    .Include(a => a.Reversal)
                    .Where(a => a.DestinationType == SettlementDestinationType.PurchaseInvoice && a.PurchaseInvoiceHeaderId != null && a.EffectiveDate < endExclusive)
                    .ToList()
                    .Where(a => !a.IsReversed || (a.Reversal != null && a.Reversal.ReversalDate.Date > day))
                    .GroupBy(a => a.PurchaseInvoiceHeaderId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

                foreach (var bill in bills)
                {
                    if (bill.IsDraft) continue;
                    var paid = (bill.VendorPayments ?? new List<VendorPayment>())
                        .Where(p => p.Date.Date <= day && (!p.IsVoid || ((p.VoidGeneralLedgerHeader?.Date ?? p.VoidedAt)?.Date > day)))
                        .Sum(p => p.Amount);
                    var settled = appSettledByBill.TryGetValue(bill.Id, out var bs) ? bs : 0m;
                    var amount = Signed(bill.Total(), paid + settled, bill.IsVoid, bill.VoidGeneralLedgerHeader?.Date ?? bill.VoidedAt, bill.Date, day);
                    if (Math.Abs(amount) <= 0.005m) continue;
                    count++;
                    var bucket = Bucket(bill.DueDate?.Date ?? bill.Date.Date.AddDays(30), day);
                    buckets[bucket] += amount;
                    if (bill.GeneralLedgerHeaderId == null) unposted += amount;
                    else posted += amount;
                }
            }

            var controlIds = receivable
                ? _db.Customers.AsNoTracking().Where(c => c.AccountsReceivableAccountId != null).Select(c => c.AccountsReceivableAccountId!.Value)
                    .Concat(_db.SalesInvoiceHeaders.AsNoTracking().Where(i => i.ReceivableAccountId != null).Select(i => i.ReceivableAccountId!.Value))
                    .Distinct().ToList()
                : _db.Vendors.AsNoTracking().Where(v => v.AccountsPayableAccountId != null).Select(v => v.AccountsPayableAccountId!.Value)
                    .Concat(_db.PurchaseInvoiceHeaders.AsNoTracking().Where(b => b.PayableAccountId != null).Select(b => b.PayableAccountId!.Value))
                    .Distinct().ToList();
            var gl = ControlBalance(controlIds, endExclusive, receivable);
            var difference = gl - posted;
            return new
            {
                denied = false,
                documentCount = count,
                current = buckets["current"],
                days1To30 = buckets["d1to30"],
                days31To60 = buckets["d31to60"],
                days61To90 = buckets["d61to90"],
                over90 = buckets["over90"],
                postedTotal = posted,
                unpostedTotal = unposted,
                glControlBalance = gl,
                difference,
                reconciliationState = Math.Abs(difference) <= 0.005m ? "In balance" : "Out of balance"
            };
        }

        private decimal ControlBalance(List<int> accountIds, DateTime endExclusive, bool debitNormal)
        {
            if (accountIds.Count == 0) return 0m;
            var lines = _db.GeneralLedgerLines.AsNoTracking()
                .Where(l => accountIds.Contains(l.AccountId) && l.GeneralLedgerHeader.Date < endExclusive)
                .Select(l => new { l.DrCr, l.Amount })
                .ToList();
            var net = lines.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);
            return debitNormal ? net : -net;
        }

        private static decimal Signed(decimal total, decimal settled, bool voided, DateTime? voidDate, DateTime documentDate, DateTime asOf)
        {
            if (documentDate.Date > asOf.Date) return 0m;
            if (voided && (voidDate == null || voidDate.Value.Date <= asOf.Date)) return 0m;
            return Math.Round(total - settled, 2, MidpointRounding.AwayFromZero);
        }

        private static string Bucket(DateTime due, DateTime asOf)
        {
            var days = (asOf.Date - due.Date).Days;
            if (days <= 0) return "current";
            if (days <= 30) return "d1to30";
            if (days <= 60) return "d31to60";
            if (days <= 90) return "d61to90";
            return "over90";
        }

        public string ToCsv(DateTime asOf, bool includeReceivables, bool includePayables)
        {
            dynamic report = Build(asOf, includeReceivables, includePayables);
            var lines = new List<string>
            {
                "asOf,section,field,amount",
                Cell((string)report.asOf) + ",label,text," + Cell((string)report.label)
            };
            AppendSection(lines, "receivables", report.receivables);
            AppendSection(lines, "payables", report.payables);
            return string.Join("\n", lines);
        }

        private static void AppendSection(List<string> lines, string name, dynamic section)
        {
            if ((bool)section.denied)
            {
                lines.Add(",\"" + name + "\",denied,");
                return;
            }
            void Row(string field, decimal amount) =>
                lines.Add("," + name + "," + field + "," + amount.ToString("0.00", CultureInfo.InvariantCulture));
            Row("current", (decimal)section.current);
            Row("days1To30", (decimal)section.days1To30);
            Row("days31To60", (decimal)section.days31To60);
            Row("days61To90", (decimal)section.days61To90);
            Row("over90", (decimal)section.over90);
            Row("postedTotal", (decimal)section.postedTotal);
            Row("unpostedTotal", (decimal)section.unpostedTotal);
            Row("glControlBalance", (decimal)section.glControlBalance);
            Row("difference", (decimal)section.difference);
        }

        private static string Cell(string value)
        {
            var text = value ?? "";
            if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
                text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
