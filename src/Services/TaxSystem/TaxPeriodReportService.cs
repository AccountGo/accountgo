// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;

namespace Services.TaxSystem
{
    public sealed class TaxPeriodReportService : ITaxPeriodReportService
    {
        private const int DefaultDocumentLimitValue = 2000;
        public const int DefaultDocumentLimit = DefaultDocumentLimitValue;
        private const int MaxLedgerOtherEntries = 200;
        private const int MaxRangeDays = 400;
        private const int NoTaxKey = -1;

        private readonly IRepository<CompanySetting> _settings;
        private readonly IRepository<Tax> _taxes;
        private readonly IRepository<SalesInvoiceHeader> _salesInvoices;
        private readonly IRepository<PurchaseInvoiceHeader> _purchaseInvoices;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<Account> _accounts;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;

        public TaxPeriodReportService(
            IRepository<CompanySetting> settings,
            IRepository<Tax> taxes,
            IRepository<SalesInvoiceHeader> salesInvoices,
            IRepository<PurchaseInvoiceHeader> purchaseInvoices,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<Account> accounts,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _settings = settings;
            _taxes = taxes;
            _salesInvoices = salesInvoices;
            _purchaseInvoices = purchaseInvoices;
            _glLines = glLines;
            _accounts = accounts;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public TaxPeriodReport Build(DateTime from, DateTime to) => Build(from, to, DefaultDocumentLimit);

        public TaxPeriodReport Build(DateTime from, DateTime to, int? documentLimit)
        {
            var startDate = from.Date;
            var endDate = to.Date;

            if (startDate > endDate)
                throw new TaxPeriodReportValidationException("invalid_range", "Choose an end date on or after the start date.");

            if ((endDate - startDate).TotalDays > MaxRangeDays)
                throw new TaxPeriodReportValidationException("range_too_long", "Choose a range of 400 days or less.");

            var setting = _settings.Table.FirstOrDefault() ?? new CompanySetting();

            if (setting.AccountingBasis == AccountingBasis.Cash)
            {
                throw new TaxPeriodReportConflictException(
                    "tax_cash_basis_pending",
                    "Cash-basis tax reports are coming soon. Switch to accrual in Settings \u2192 Financial rules to see tax by invoice and bill date for now.");
            }

            var endExclusive = endDate.AddDays(1);
            var cycleStart = TaxPeriodCalculator.EffectiveCycleStart(setting);
            var isWholePeriod = TaxPeriodCalculator.IsWholePeriod(startDate, endDate, setting.TaxPeriodFrequency, cycleStart);
            var periodLabel = isWholePeriod
                ? TaxPeriodCalculator.Containing(startDate, setting.TaxPeriodFrequency, cycleStart).Label
                : null;

            var taxCatalog = _taxes.Table
                .Select(t => new TaxMeta(t.Id, t.TaxCode, t.TaxName, t.Rate, t.SalesAccountId, t.PurchasingAccountId))
                .ToList();
            var taxById = taxCatalog.ToDictionary(t => t.Id);

            // 1. Sales invoices dated in range (posted/issued; exclude Draft, unposted Void, and unposted null-Status rows)
            var salesDocs = _salesInvoices.Table
                .Where(h => h.Date >= startDate
                            && h.Date < endExclusive
                            && (h.Status == null
                                ? h.GeneralLedgerHeaderId != null
                                : h.Status != SalesInvoiceStatus.Draft
                                  && !(h.Status == SalesInvoiceStatus.Void && h.GeneralLedgerHeaderId == null)))
                .Select(h => new
                {
                    h.Id,
                    No = h.No ?? "",
                    h.Date,
                    h.Status,
                    h.GeneralLedgerHeaderId,
                    h.ShippingHandlingCharge,
                    PartyName = h.Customer != null && h.Customer.Party != null ? h.Customer.Party.Name : "",
                    Lines = h.SalesInvoiceLines.Select(l => new LineProjection(l.TaxId, l.Quantity, l.Amount, l.Discount, l.TaxAmount)).ToList()
                })
                .ToList();

            // 2. Sales invoice voids whose VoidGeneralLedgerHeader.Date falls in range
            var salesVoidDocs = _salesInvoices.Table
                .Where(h => h.Status == SalesInvoiceStatus.Void
                            && h.VoidGeneralLedgerHeaderId != null
                            && h.VoidGeneralLedgerHeader != null
                            && h.VoidGeneralLedgerHeader.Date >= startDate
                            && h.VoidGeneralLedgerHeader.Date < endExclusive)
                .Select(h => new
                {
                    h.Id,
                    No = h.No ?? "",
                    VoidDate = h.VoidGeneralLedgerHeader!.Date,
                    h.VoidGeneralLedgerHeaderId,
                    h.ShippingHandlingCharge,
                    PartyName = h.Customer != null && h.Customer.Party != null ? h.Customer.Party.Name : "",
                    Lines = h.SalesInvoiceLines.Select(l => new LineProjection(l.TaxId, l.Quantity, l.Amount, l.Discount, l.TaxAmount)).ToList()
                })
                .ToList();

            // 3. Purchase bills dated in range (posted/issued; exclude Draft, unposted Void, and unposted null-Status rows)
            var billDocs = _purchaseInvoices.Table
                .Where(h => h.Date >= startDate
                            && h.Date < endExclusive
                            && (h.Status == null
                                ? h.GeneralLedgerHeaderId != null
                                : h.Status != PurchaseInvoiceStatus.Draft
                                  && !(h.Status == PurchaseInvoiceStatus.Void && h.GeneralLedgerHeaderId == null)))
                .Select(h => new
                {
                    h.Id,
                    No = h.No ?? h.VendorInvoiceNo,
                    h.Date,
                    h.Status,
                    h.GeneralLedgerHeaderId,
                    PartyName = h.Vendor != null && h.Vendor.Party != null ? h.Vendor.Party.Name : "",
                    Lines = h.PurchaseInvoiceLines.Select(l => new LineProjection(l.TaxId, l.Quantity, l.Amount, l.Discount, l.TaxAmount)).ToList()
                })
                .ToList();

            // 4. Purchase bill voids whose VoidGeneralLedgerHeader.Date falls in range
            var billVoidDocs = _purchaseInvoices.Table
                .Where(h => h.Status == PurchaseInvoiceStatus.Void
                            && h.VoidGeneralLedgerHeaderId != null
                            && h.VoidGeneralLedgerHeader != null
                            && h.VoidGeneralLedgerHeader.Date >= startDate
                            && h.VoidGeneralLedgerHeader.Date < endExclusive)
                .Select(h => new
                {
                    h.Id,
                    No = h.No ?? h.VendorInvoiceNo,
                    VoidDate = h.VoidGeneralLedgerHeader!.Date,
                    h.VoidGeneralLedgerHeaderId,
                    PartyName = h.Vendor != null && h.Vendor.Party != null ? h.Vendor.Party.Name : "",
                    Lines = h.PurchaseInvoiceLines.Select(l => new LineProjection(l.TaxId, l.Quantity, l.Amount, l.Discount, l.TaxAmount)).ToList()
                })
                .ToList();

            var outputAcc = new Dictionary<int, TaxRowAccumulator>();
            var inputAcc = new Dictionary<int, TaxRowAccumulator>();
            var allDocuments = new List<TaxPeriodDocumentRow>();
            var unpostedWarnings = new List<TaxPeriodUnpostedWarning>();
            var docGlHeaderIds = new HashSet<int>();

            foreach (var doc in salesDocs)
            {
                var posted = doc.GeneralLedgerHeaderId.HasValue;
                if (doc.GeneralLedgerHeaderId.HasValue)
                    docGlHeaderIds.Add(doc.GeneralLedgerHeaderId.Value);

                if (doc.Status != SalesInvoiceStatus.Void && !posted)
                {
                    unpostedWarnings.Add(new TaxPeriodUnpostedWarning(
                        "invoice",
                        doc.Id,
                        doc.No,
                        FormatDate(doc.Date)));
                }

                AccumulateDocument(
                    "output",
                    "invoice",
                    doc.Id,
                    doc.No,
                    doc.Date,
                    doc.PartyName ?? "",
                    posted,
                    sign: 1m,
                    doc.ShippingHandlingCharge,
                    doc.Lines,
                    outputAcc,
                    allDocuments);
            }

            foreach (var doc in salesVoidDocs)
            {
                if (doc.VoidGeneralLedgerHeaderId.HasValue)
                    docGlHeaderIds.Add(doc.VoidGeneralLedgerHeaderId.Value);

                AccumulateDocument(
                    "output",
                    "invoiceVoid",
                    doc.Id,
                    doc.No,
                    doc.VoidDate,
                    doc.PartyName ?? "",
                    posted: true,
                    sign: -1m,
                    doc.ShippingHandlingCharge,
                    doc.Lines,
                    outputAcc,
                    allDocuments);
            }

            foreach (var doc in billDocs)
            {
                var posted = doc.GeneralLedgerHeaderId.HasValue;
                if (doc.GeneralLedgerHeaderId.HasValue)
                    docGlHeaderIds.Add(doc.GeneralLedgerHeaderId.Value);

                if (doc.Status != PurchaseInvoiceStatus.Void && !posted)
                {
                    unpostedWarnings.Add(new TaxPeriodUnpostedWarning(
                        "bill",
                        doc.Id,
                        doc.No,
                        FormatDate(doc.Date)));
                }

                AccumulateDocument(
                    "input",
                    "bill",
                    doc.Id,
                    doc.No,
                    doc.Date,
                    doc.PartyName ?? "",
                    posted,
                    sign: 1m,
                    shippingCharge: 0m,
                    doc.Lines,
                    inputAcc,
                    allDocuments);
            }

            foreach (var doc in billVoidDocs)
            {
                if (doc.VoidGeneralLedgerHeaderId.HasValue)
                    docGlHeaderIds.Add(doc.VoidGeneralLedgerHeaderId.Value);

                AccumulateDocument(
                    "input",
                    "billVoid",
                    doc.Id,
                    doc.No,
                    doc.VoidDate,
                    doc.PartyName ?? "",
                    posted: true,
                    sign: -1m,
                    shippingCharge: 0m,
                    doc.Lines,
                    inputAcc,
                    allDocuments);
            }

            var outputSummary = BuildSideSummary(outputAcc, taxById);
            var inputSummary = BuildSideSummary(inputAcc, taxById);
            var netTax = Round2(outputSummary.Tax - inputSummary.Tax);

            allDocuments.Sort((a, b) =>
            {
                var dateCmp = string.Compare(a.Date, b.Date, StringComparison.Ordinal);
                if (dateCmp != 0) return dateCmp;
                var kindCmp = string.Compare(a.Kind, b.Kind, StringComparison.Ordinal);
                if (kindCmp != 0) return kindCmp;
                return a.Id.CompareTo(b.Id);
            });

            var truncated = false;
            IReadOnlyList<TaxPeriodDocumentRow> returnedDocs = allDocuments;
            if (documentLimit.HasValue && allDocuments.Count > documentLimit.Value)
            {
                truncated = true;
                returnedDocs = allDocuments.Take(documentLimit.Value).ToList();
            }

            unpostedWarnings.Sort((a, b) =>
            {
                var dateCmp = string.Compare(a.Date, b.Date, StringComparison.Ordinal);
                return dateCmp != 0 ? dateCmp : a.Id.CompareTo(b.Id);
            });

            var hasFinancials = HasFinancialsModule();
            var ledger = hasFinancials
                ? BuildLedgerCheck(startDate, endExclusive, taxCatalog, docGlHeaderIds)
                : null;
            var lockStatus = hasFinancials
                ? BuildLockStatus(setting.BooksLockedThrough, startDate, endDate)
                : null;

            return new TaxPeriodReport(
                From: FormatDate(startDate),
                To: FormatDate(endDate),
                Basis: "Accrual",
                Currency: setting.Currency ?? "USD",
                IsWholePeriod: isWholePeriod,
                PeriodLabel: periodLabel,
                Output: outputSummary,
                Input: inputSummary,
                NetTax: netTax,
                Documents: returnedDocs,
                DocumentsTruncated: truncated,
                Warnings: new TaxPeriodWarnings(unpostedWarnings),
                Ledger: ledger,
                Lock: lockStatus);
        }

        public IReadOnlyList<TaxPeriodSummaryItem> GetRecentPeriods(int count)
        {
            if (count < 1 || count > 24)
                throw new TaxPeriodReportValidationException("invalid_count", "Period count must be between 1 and 24.");

            var setting = _settings.Table.FirstOrDefault() ?? new CompanySetting();
            var cycleStart = TaxPeriodCalculator.EffectiveCycleStart(setting);
            var today = WorkspaceToday(setting.Timezone);
            var periods = TaxPeriodCalculator.Recent(today, setting.TaxPeriodFrequency, cycleStart, count);
            var hasFinancials = HasFinancialsModule();

            var items = new List<TaxPeriodSummaryItem>(periods.Count);
            foreach (var p in periods)
            {
                var state = p.End < today ? "ended" : "open";
                string? lockState = null;
                if (hasFinancials)
                {
                    lockState = ComputeLockState(setting.BooksLockedThrough, p.Start, p.End);
                }

                items.Add(new TaxPeriodSummaryItem(
                    Start: FormatDate(p.Start),
                    End: FormatDate(p.End),
                    Label: p.Label,
                    State: state,
                    Lock: lockState,
                    Filing: null));
            }

            return items;
        }

        public TaxPeriodSettingsDto GetSettings()
        {
            var setting = _settings.Table.FirstOrDefault() ?? new CompanySetting();
            var defaultCycle = TaxPeriodCalculator.DefaultQuarterCycleStartMonth(setting.FiscalYearPattern, setting.FiscalYearStartMonth);
            var effectiveCycle = TaxPeriodCalculator.EffectiveCycleStart(setting);

            return new TaxPeriodSettingsDto(
                Frequency: setting.TaxPeriodFrequency.ToString(),
                QuarterCycleStartMonth: setting.TaxQuarterCycleStartMonth,
                EffectiveQuarterCycleStartMonth: effectiveCycle,
                DefaultQuarterCycleStartMonth: defaultCycle);
        }

        public TaxPeriodSettingsDto UpdateSettings(TaxPeriodFrequency frequency, int? quarterCycleStartMonth)
        {
            if (!Enum.IsDefined(typeof(TaxPeriodFrequency), frequency))
                throw new TaxPeriodReportValidationException("invalid_settings", "Choose how often you file and which months your quarters start in.");

            if (quarterCycleStartMonth.HasValue && (quarterCycleStartMonth.Value < 1 || quarterCycleStartMonth.Value > 3))
                throw new TaxPeriodReportValidationException("invalid_settings", "Choose how often you file and which months your quarters start in.");

            var setting = _settings.Table.FirstOrDefault();
            if (setting == null)
                throw new InvalidOperationException("Workspace settings were not found.");

            setting.TaxPeriodFrequency = frequency;
            setting.TaxQuarterCycleStartMonth = quarterCycleStartMonth;
            _settings.Update(setting);

            return GetSettings();
        }

        private TaxPeriodLedgerCheck BuildLedgerCheck(
            DateTime startDate,
            DateTime endExclusive,
            IReadOnlyList<TaxMeta> taxCatalog,
            HashSet<int> docGlHeaderIds)
        {
            var accountIds = taxCatalog
                .SelectMany(t => new[] { t.SalesAccountId, t.PurchasingAccountId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            if (accountIds.Count == 0)
                return new TaxPeriodLedgerCheck(Array.Empty<TaxPeriodLedgerAccount>());

            var accounts = _accounts.Table
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new
                {
                    a.Id,
                    a.AccountCode,
                    a.AccountName,
                    a.AccountClassId
                })
                .OrderBy(a => a.AccountCode)
                .ToList();

            var glLines = _glLines.Table
                .Where(l => accountIds.Contains(l.AccountId)
                            && l.GeneralLedgerHeader.Date >= startDate
                            && l.GeneralLedgerHeader.Date < endExclusive)
                .Select(l => new
                {
                    l.AccountId,
                    l.GeneralLedgerHeaderId,
                    l.GeneralLedgerHeader.Date,
                    Description = l.GeneralLedgerHeader.Description ?? "",
                    l.GeneralLedgerHeader.DocumentType,
                    l.DrCr,
                    l.Amount
                })
                .ToList();

            var linesByAccount = glLines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.ToList());
            var result = new List<TaxPeriodLedgerAccount>(accounts.Count);

            foreach (var acc in accounts)
            {
                var isDebitPositive = acc.AccountClassId == (int)AccountClasses.Assets
                                      || acc.AccountClassId == (int)AccountClasses.Expense;
                var positiveSide = isDebitPositive ? "debit" : "credit";

                decimal movement = 0m;
                decimal fromDocuments = 0m;
                var otherByHeader = new Dictionary<int, (DateTime Date, string Description, DocumentTypes DocType, decimal Amount)>();

                if (linesByAccount.TryGetValue(acc.Id, out var accLines))
                {
                    foreach (var line in accLines)
                    {
                        var signed = SignedAmount(line.DrCr, line.Amount, isDebitPositive);
                        movement += signed;

                        if (docGlHeaderIds.Contains(line.GeneralLedgerHeaderId))
                        {
                            fromDocuments += signed;
                        }
                        else
                        {
                            if (!otherByHeader.TryGetValue(line.GeneralLedgerHeaderId, out var existing))
                            {
                                otherByHeader[line.GeneralLedgerHeaderId] = (line.Date, line.Description, line.DocumentType, signed);
                            }
                            else
                            {
                                otherByHeader[line.GeneralLedgerHeaderId] = (existing.Date, existing.Description, existing.DocType, existing.Amount + signed);
                            }
                        }
                    }
                }

                movement = Round2(movement);
                fromDocuments = Round2(fromDocuments);
                var other = Round2(movement - fromDocuments);

                var entries = otherByHeader
                    .OrderBy(kv => kv.Value.Date)
                    .ThenBy(kv => kv.Key)
                    .Take(MaxLedgerOtherEntries)
                    .Select(kv => new TaxPeriodLedgerOtherEntry(
                        GlHeaderId: kv.Key,
                        Date: FormatDate(kv.Value.Date),
                        Description: kv.Value.Description,
                        DocumentType: kv.Value.DocType.ToString(),
                        Amount: Round2(kv.Value.Amount)))
                    .ToList();

                result.Add(new TaxPeriodLedgerAccount(
                    AccountId: acc.Id,
                    Code: acc.AccountCode ?? "",
                    Name: acc.AccountName ?? "",
                    PositiveSide: positiveSide,
                    Movement: movement,
                    FromDocuments: fromDocuments,
                    Other: other,
                    Entries: entries));
            }

            return new TaxPeriodLedgerCheck(result);
        }

        private TaxPeriodLockStatus BuildLockStatus(DateTime? lockedThrough, DateTime from, DateTime to)
        {
            var canLock = _tenant.Permissions.Contains(SystemPermissions.FinancialsPeriodsClose, StringComparer.Ordinal);
            return new TaxPeriodLockStatus(
                LockedThrough: lockedThrough.HasValue ? FormatDate(lockedThrough.Value) : null,
                State: ComputeLockState(lockedThrough, from, to),
                CanLock: canLock);
        }

        private static string ComputeLockState(DateTime? lockedThrough, DateTime from, DateTime to)
        {
            if (!lockedThrough.HasValue)
                return "none";
            var locked = lockedThrough.Value.Date;
            if (locked >= to.Date)
                return "full";
            if (locked >= from.Date)
                return "partial";
            return "none";
        }

        private static void AccumulateDocument(
            string side,
            string kind,
            int docId,
            string docNo,
            DateTime docDate,
            string partyName,
            bool posted,
            decimal sign,
            decimal shippingCharge,
            IReadOnlyList<LineProjection> lines,
            Dictionary<int, TaxRowAccumulator> acc,
            List<TaxPeriodDocumentRow> documents)
        {
            var perTax = new Dictionary<int, (decimal Taxable, decimal Tax)>();

            foreach (var line in lines)
            {
                var subtotal = PurchaseInvoiceLine.SubtotalOf(line.Quantity, line.Amount, line.Discount);
                var key = line.TaxId ?? NoTaxKey;
                perTax.TryGetValue(key, out var cur);
                perTax[key] = (cur.Taxable + subtotal * sign, cur.Tax + line.TaxAmount * sign);
            }

            if (shippingCharge != 0m)
            {
                perTax.TryGetValue(NoTaxKey, out var cur);
                perTax[NoTaxKey] = (cur.Taxable + shippingCharge * sign, cur.Tax);
            }

            var dateStr = FormatDate(docDate);
            foreach (var kv in perTax)
            {
                var taxable = Round2(kv.Value.Taxable);
                var tax = Round2(kv.Value.Tax);
                if (taxable == 0m && tax == 0m)
                    continue;

                if (!acc.TryGetValue(kv.Key, out var row))
                {
                    row = new TaxRowAccumulator();
                    acc[kv.Key] = row;
                }

                row.Taxable += taxable;
                row.Tax += tax;
                row.DocumentCount++;

                documents.Add(new TaxPeriodDocumentRow(
                    Side: side,
                    Kind: kind,
                    Id: docId,
                    No: docNo,
                    Date: dateStr,
                    Party: partyName,
                    TaxId: kv.Key == NoTaxKey ? null : kv.Key,
                    Taxable: taxable,
                    Tax: tax,
                    Posted: posted));
            }
        }

        private static TaxPeriodSideSummary BuildSideSummary(
            Dictionary<int, TaxRowAccumulator> acc,
            Dictionary<int, TaxMeta> taxById)
        {
            var rows = new List<TaxPeriodTaxRow>();
            decimal totalTaxable = 0m;
            decimal totalTax = 0m;

            foreach (var kv in acc)
            {
                var taxable = Round2(kv.Value.Taxable);
                var tax = Round2(kv.Value.Tax);
                if (taxable == 0m && tax == 0m)
                    continue;

                totalTaxable += taxable;
                totalTax += tax;

                if (kv.Key != NoTaxKey && taxById.TryGetValue(kv.Key, out var meta))
                {
                    rows.Add(new TaxPeriodTaxRow(
                        TaxId: meta.Id,
                        TaxCode: meta.Code,
                        TaxName: meta.Name,
                        Rate: meta.Rate,
                        Taxable: taxable,
                        Tax: tax,
                        DocumentCount: kv.Value.DocumentCount));
                }
                else
                {
                    rows.Add(new TaxPeriodTaxRow(
                        TaxId: null,
                        TaxCode: "No tax",
                        TaxName: "No tax",
                        Rate: null,
                        Taxable: taxable,
                        Tax: tax,
                        DocumentCount: kv.Value.DocumentCount));
                }
            }

            rows.Sort((a, b) =>
            {
                if (a.TaxId == null && b.TaxId == null) return 0;
                if (a.TaxId == null) return 1;
                if (b.TaxId == null) return -1;
                var codeCmp = string.Compare(a.TaxCode, b.TaxCode, StringComparison.OrdinalIgnoreCase);
                return codeCmp != 0 ? codeCmp : (a.TaxId ?? 0).CompareTo(b.TaxId ?? 0);
            });

            return new TaxPeriodSideSummary(
                Rows: rows,
                Taxable: Round2(totalTaxable),
                Tax: Round2(totalTax));
        }

        private bool HasFinancialsModule()
        {
            var decision = _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials));
            return decision.Allowed;
        }

        private static DateTime WorkspaceToday(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }
            return DateTime.UtcNow.Date;
        }

        private static decimal SignedAmount(DrOrCrSide drCr, decimal amount, bool isDebitPositive)
        {
            if (isDebitPositive)
                return drCr == DrOrCrSide.Dr ? amount : -amount;
            return drCr == DrOrCrSide.Cr ? amount : -amount;
        }

        private static string FormatDate(DateTime date) =>
            date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static decimal Round2(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private sealed record LineProjection(int? TaxId, decimal Quantity, decimal Amount, decimal? Discount, decimal TaxAmount);

        private sealed record TaxMeta(int Id, string Code, string Name, decimal Rate, int? SalesAccountId, int? PurchasingAccountId);

        private sealed class TaxRowAccumulator
        {
            public decimal Taxable { get; set; }
            public decimal Tax { get; set; }
            public int DocumentCount { get; set; }
        }
    }
}
