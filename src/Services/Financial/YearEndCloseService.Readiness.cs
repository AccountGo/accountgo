// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;

namespace Services.Financial
{
    public sealed partial class YearEndCloseService
    {
        private const decimal TieTolerance = 0.005m;

        public YearEndPreview DeclareIssuance(string fiscalYearCode, YearEndIssuanceCommand command, string actor)
        {
            if (_issuances == null)
                throw new InvalidOperationException("Issuance declarations are not available.");
            var year = RequireYear(fiscalYearCode);
            if (year.ClosedAt == null)
                throw new YearEndCloseException("Only a closed fiscal year can record whether its financial statements were issued.", "validation");
            var status = command.Status?.Trim();
            if (status != "Unissued" && status != "Issued")
                throw new YearEndCloseException("Issuance status must be Unissued or Issued.", "validation");
            var reason = command.Reason?.Trim() ?? "";
            if (reason.Length == 0 || reason.Length > 2000)
                throw new YearEndCloseException("A reason of up to 2000 characters is required.", "validation");
            if (status == "Issued" && string.IsNullOrWhiteSpace(command.ExternalReference))
                throw new YearEndCloseException("An issued or filed declaration needs an external reference.", "validation");
            var latest = LatestIssuance(year.Id);
            if (latest?.Status == "Issued")
                throw new YearEndCloseException("An issued or filed declaration cannot be cleared.", "validation");

            var name = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
            _issuances.Insert(new YearEndIssuanceDeclaration
            {
                CompanyId = year.CompanyId,
                FinancialYearId = year.Id,
                Status = status,
                Actor = name,
                DeclaredAtUtc = DateTime.UtcNow,
                ExternalDate = command.ExternalDate?.Date,
                ExternalReference = command.ExternalReference?.Trim(),
                Reason = reason
            });
            AppendLifecycle(year, "ExternalIssue", name, reason, null, null, null);
            return BuildPreview(year);
        }

        private void AddStructuralBlockers(FinancialYear year, List<YearEndCheckItem> blocking,
            Dictionary<int, (decimal Debit, decimal Credit)>? throughEnd)
        {
            try
            {
                var accounts = _accounts.Table.ToList();
                var byId = accounts.ToDictionary(a => a.Id);
                var glSetting = _glSettings.Table.FirstOrDefault();
                if (!TryResolvePermanentAccount(glSetting, accounts, out _, out var problem))
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "permanent_account_invalid",
                        Message = problem ?? "The permanent equity account is not valid.",
                        Count = 1,
                        LinkPath = "/financials/accounts"
                    });
                }

                var end = ExclusiveEnd(year.EndDate);
                var unbalanced = _glLines.GetAllIncluding(l => l.GeneralLedgerHeader)
                    .Where(l => l.GeneralLedgerHeader != null && l.GeneralLedgerHeader.Date < end)
                    .AsEnumerable()
                    .GroupBy(l => l.GeneralLedgerHeaderId)
                    .Count(g => g.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount)
                        != g.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));
                if (unbalanced > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "ledger_unbalanced",
                        Message = "A general ledger transaction dated on or before the fiscal year end does not balance.",
                        Count = unbalanced,
                        LinkPath = "/financials?from=&to=" + year.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }

                var unpostedIssued = _salesInvoices.Table.Count(i =>
                        i.Status != SalesInvoiceStatus.Draft && i.Status != SalesInvoiceStatus.Void
                        && i.GeneralLedgerHeaderId == null && i.Date < end)
                    + _purchaseInvoices.Table.Count(i =>
                        i.Status != null && i.Status != PurchaseInvoiceStatus.Draft && i.Status != PurchaseInvoiceStatus.Void
                        && i.GeneralLedgerHeaderId == null && i.Date < end);
                if (unpostedIssued > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "unposted_issued_documents",
                        Message = "Issued invoices or bills dated on or before the fiscal year end are missing from the ledger.",
                        Count = unpostedIssued,
                        LinkPath = "/sales/invoices?from=" + year.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            + "&to=" + year.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }

                var companyId = _tenant.RequireCompanyId();
                var unpostedSettlements = CountUnpostedSettlementDocuments(end);
                if (unpostedSettlements > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "unposted_settlement_documents",
                        Message = "Unposted credits, advances, refunds, or write-offs dated on or before the fiscal year end are waiting to be posted.",
                        Count = unpostedSettlements,
                        LinkPath = "/sales/credits"
                    });
                }

                var pendingLinks = _linkedPairs == null ? 0 : _linkedPairs.Table.Count(p =>
                    p.SourceCompanyId == companyId && p.State == "PendingSource");
                if (pendingLinks > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "unposted_journals",
                        Message = "A linked journal in this workspace is still waiting to be posted.",
                        Count = pendingLinks,
                        LinkPath = "/financials/journals?status=draft"
                    });
                }

                if (_customers != null && _vendors != null && throughEnd != null
                    && !ArApTied(year, throughEnd, out var tieMessage, out var tieAmount))
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "ar_ap_control_tieout_failed",
                        Message = tieMessage,
                        Count = 1,
                        Amount = tieAmount,
                        LinkPath = "/reports?statement=aging&to=" + year.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    });
                }
            }
            catch (YearEndCloseException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new YearEndCloseException(
                    "A year-end check could not be completed. Close was not accepted.",
                    "close_check_unavailable");
            }
        }

        private void AddWrongSideWarning(FinancialYear year, List<YearEndCheckItem> warnings,
            Dictionary<int, (decimal Debit, decimal Credit)>? throughEnd)
        {
            if (throughEnd == null || throughEnd.Count == 0) return;
            var accounts = _accounts.Table.Where(a => !a.IsContraAccount
                && (a.AccountClassId == (int)AccountClasses.Assets
                    || a.AccountClassId == (int)AccountClasses.Liabilities
                    || a.AccountClassId == (int)AccountClasses.Revenue
                    || a.AccountClassId == (int)AccountClasses.Expense)).ToList();
            var count = 0;
            foreach (var account in accounts)
            {
                if (!throughEnd.TryGetValue(account.Id, out var bal)) continue;
                var net = bal.Debit - bal.Credit;
                var abnormal = account.DrOrCrSide == DrOrCrSide.Dr ? net < -TieTolerance : net > TieTolerance;
                if (abnormal) count++;
            }
            if (count == 0) return;
            warnings.Add(new YearEndCheckItem
            {
                Code = "wrong_side_balances",
                Message = "Asset, liability, revenue, or expense accounts have a balance on the unusual side at the fiscal year end.",
                Count = count,
                LinkPath = "/financials?from=&to=" + year.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        }

        private int CountUnpostedSettlementDocuments(DateTime endExclusive)
        {
            var count = 0;
            if (_customerCreditNotes != null)
                count += _customerCreditNotes.Table.Count(c =>
                    (c.Status == SettlementDocumentStatus.Draft || c.Status == SettlementDocumentStatus.PendingReview) && c.Date < endExclusive);
            if (_supplierCreditNotes != null)
                count += _supplierCreditNotes.Table.Count(c =>
                    (c.Status == SettlementDocumentStatus.Draft || c.Status == SettlementDocumentStatus.PendingReview) && c.EffectiveDate < endExclusive);
            if (_supplierAdvances != null)
                count += _supplierAdvances.Table.Count(a =>
                    (a.Status == SettlementDocumentStatus.Draft || a.Status == SettlementDocumentStatus.PendingReview) && a.Date < endExclusive);
            if (_partyRefunds != null)
                count += _partyRefunds.Table.Count(r =>
                    (r.Status == SettlementDocumentStatus.Draft || r.Status == SettlementDocumentStatus.PendingReview) && r.Date < endExclusive);
            if (_receivableWriteOffs != null)
                count += _receivableWriteOffs.Table.Count(w =>
                    (w.Status == SettlementDocumentStatus.Draft || w.Status == SettlementDocumentStatus.PendingReview) && w.Date < endExclusive);
            return count;
        }

        private int CountOpenReturnClearing(DateTime fyEnd)
        {
            if (_supplierReturnDispatches == null)
                return 0;
            var day = fyEnd.Date;
            return _supplierReturnDispatches.Table
                .Where(d => d.HasControlTransferred && d.Status == SettlementDocumentStatus.Posted && d.SupplierCreditNoteId == null)
                .AsEnumerable()
                .Count(d => (d.ControlTransferDate ?? d.DispatchDate).Date <= day);
        }

        private (Dictionary<int, decimal> InvoiceSettled, Dictionary<int, decimal> BillSettled) BuildSettlementAdjustmentsAsOf(DateTime asOf)
        {
            var day = asOf.Date;
            var invoiceSettled = new Dictionary<int, decimal>();
            var billSettled = new Dictionary<int, decimal>();

            if (_settlementApplications != null)
            {
                var reversedAppIds = _settlementApplicationReversals != null
                    ? _settlementApplicationReversals.Table
                        .Where(r => r.ReversalDate <= day)
                        .Select(r => r.SettlementApplicationId)
                        .ToHashSet()
                    : new HashSet<int>();

                foreach (var app in _settlementApplications.Table.Where(a => a.EffectiveDate <= day).ToList())
                {
                    if (reversedAppIds.Contains(app.Id))
                        continue;

                    if (app.SalesInvoiceHeaderId.HasValue)
                    {
                        var id = app.SalesInvoiceHeaderId.Value;
                        invoiceSettled[id] = invoiceSettled.GetValueOrDefault(id) + app.Amount;
                    }
                    else if (app.PurchaseInvoiceHeaderId.HasValue)
                    {
                        var id = app.PurchaseInvoiceHeaderId.Value;
                        billSettled[id] = billSettled.GetValueOrDefault(id) + app.Amount;
                    }
                }
            }

            if (_receivableWriteOffs != null)
            {
                var writeOffs = _receivableWriteOffs
                    .GetAllIncluding(w => w.VoidGeneralLedgerHeader)
                    .Where(w => w.GeneralLedgerHeaderId != null
                        && w.Status != SettlementDocumentStatus.Draft
                        && w.Status != SettlementDocumentStatus.PendingReview
                        && w.Date <= day)
                    .ToList();

                foreach (var wo in writeOffs)
                {
                    if (wo.Status == SettlementDocumentStatus.Reversed)
                    {
                        var revDate = wo.VoidGeneralLedgerHeader?.Date;
                        if (revDate == null || revDate.Value.Date <= day)
                            continue;
                    }
                    invoiceSettled[wo.SalesInvoiceHeaderId] = invoiceSettled.GetValueOrDefault(wo.SalesInvoiceHeaderId) + wo.Amount;
                }
            }

            return (invoiceSettled, billSettled);
        }

        private int CountStaleOpenItems(DateTime fyEnd)
        {
            var (invoiceSettled, billSettled) = BuildSettlementAdjustmentsAsOf(fyEnd);
            var stale = 0;
            foreach (var invoice in _salesInvoices.GetAllIncluding(i => i.SalesInvoiceLines, i => i.CustomerAllocations, i => i.VoidGeneralLedgerHeader).ToList())
            {
                if (invoice.Status == SalesInvoiceStatus.Draft || invoice.Date.Date > fyEnd) continue;
                var due = invoice.DueDate?.Date ?? invoice.Date.Date.AddDays(30);
                if ((fyEnd.Date - due).TotalDays <= 365) continue;
                if (invoice.BalanceDueAsOf(fyEnd, invoice.VoidGeneralLedgerHeader?.Date, invoiceSettled.GetValueOrDefault(invoice.Id)) > TieTolerance)
                    stale++;
            }
            foreach (var bill in _purchaseInvoices.GetAllIncluding(i => i.PurchaseInvoiceLines, i => i.VendorPayments, i => i.VoidGeneralLedgerHeader).ToList())
            {
                if (bill.IsDraft || bill.Date.Date > fyEnd) continue;
                var due = bill.DueDate?.Date ?? bill.Date.Date.AddDays(30);
                if ((fyEnd.Date - due).TotalDays <= 365) continue;
                if (bill.BalanceDueAsOf(fyEnd, billSettled.GetValueOrDefault(bill.Id)) > TieTolerance)
                    stale++;
            }
            return stale;
        }

        private bool ArApTied(FinancialYear year, Dictionary<int, (decimal Debit, decimal Credit)> throughEnd, out string message, out decimal amount)
        {
            var end = year.EndDate.Date;
            var arAccounts = _customers!.Table.Where(c => c.AccountsReceivableAccountId != null)
                .Select(c => c.AccountsReceivableAccountId!.Value).Distinct().ToList();
            var apAccounts = _vendors!.Table.Where(v => v.AccountsPayableAccountId != null)
                .Select(v => v.AccountsPayableAccountId!.Value).Distinct().ToList();
            var payableSetting = _glSettings.Table.Select(s => s.PayableAccountId).FirstOrDefault();
            if (payableSetting.HasValue && !apAccounts.Contains(payableSetting.Value))
                apAccounts.Add(payableSetting.Value);

            var invoices = _salesInvoices.GetAllIncluding(i => i.SalesInvoiceLines, i => i.CustomerAllocations, i => i.VoidGeneralLedgerHeader)
                .Where(i => i.GeneralLedgerHeaderId != null && i.Date <= end)
                .ToList();
            var bills = _purchaseInvoices.GetAllIncluding(b => b.PurchaseInvoiceLines, b => b.VendorPayments, b => b.VoidGeneralLedgerHeader)
                .Where(b => b.GeneralLedgerHeaderId != null && b.Date <= end)
                .ToList();

            foreach (var invAr in invoices.Where(i => i.ReceivableAccountId.HasValue).Select(i => i.ReceivableAccountId!.Value))
            {
                if (!arAccounts.Contains(invAr))
                    arAccounts.Add(invAr);
            }
            foreach (var billAp in bills.Where(b => b.PayableAccountId.HasValue).Select(b => b.PayableAccountId!.Value))
            {
                if (!apAccounts.Contains(billAp))
                    apAccounts.Add(billAp);
            }

            var (invoiceSettled, billSettled) = BuildSettlementAdjustmentsAsOf(end);

            decimal worst = 0m;
            var failed = 0;
            if (!SideTied(arAccounts, invoices.Select(i => (AccountId: CustomerAr(i), Amount: SignedAr(i, end, invoiceSettled.GetValueOrDefault(i.Id)))), throughEnd, debitNormal: true, ref worst))
                failed++;
            if (!SideTied(apAccounts, bills.Select(b => (AccountId: b.PayableAccountId ?? payableSetting, Amount: SignedAp(b, end, billSettled.GetValueOrDefault(b.Id)))), throughEnd, debitNormal: false, ref worst))
                failed++;
            amount = worst;
            message = failed == 0
                ? ""
                : "Posted receivables or payables do not agree with their ledger control accounts at the fiscal year end. Opposite differences are not allowed to cancel.";
            return failed == 0;
        }

        private static bool SideTied(
            IReadOnlyList<int> accounts,
            IEnumerable<(int? AccountId, decimal Amount)> documents,
            Dictionary<int, (decimal Debit, decimal Credit)> throughEnd,
            bool debitNormal,
            ref decimal worst)
        {
            var rows = documents.ToList();
            var ok = true;
            var seen = new HashSet<int>(accounts);
            foreach (var id in rows.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value))
                seen.Add(id);
            foreach (var id in seen)
            {
                var doc = rows.Where(r => r.AccountId == id).Sum(r => r.Amount);
                var net = throughEnd.TryGetValue(id, out var bal) ? bal.Debit - bal.Credit : 0m;
                var control = debitNormal ? net : -net;
                var diff = control - doc;
                if (Math.Abs(diff) > TieTolerance)
                {
                    ok = false;
                    if (Math.Abs(diff) > Math.Abs(worst)) worst = diff;
                }
            }
            var unmapped = rows.Where(r => r.AccountId == null).Sum(r => r.Amount);
            if (Math.Abs(unmapped) > TieTolerance)
            {
                ok = false;
                if (Math.Abs(unmapped) > Math.Abs(worst)) worst = unmapped;
            }
            return ok;
        }

        private int? CustomerAr(SalesInvoiceHeader invoice)
        {
            if (invoice.ReceivableAccountId.HasValue) return invoice.ReceivableAccountId.Value;
            if (invoice.CustomerId == 0 || _customers == null) return null;
            return _customers.Table.Where(c => c.Id == invoice.CustomerId).Select(c => c.AccountsReceivableAccountId).FirstOrDefault();
        }

        private static decimal SignedAr(SalesInvoiceHeader invoice, DateTime asOf, decimal creditAndWriteOffAsOf = 0m)
        {
            var voidDate = invoice.VoidGeneralLedgerHeader?.Date;
            if (invoice.Status == SalesInvoiceStatus.Draft || invoice.Date.Date > asOf.Date) return 0m;
            if (invoice.Status == SalesInvoiceStatus.Void && (voidDate == null || voidDate.Value.Date <= asOf.Date)) return 0m;
            var allocated = (invoice.CustomerAllocations ?? new List<CustomerAllocation>())
                .Where(a => a.Date.Date <= asOf.Date).Sum(a => a.Amount);
            return Math.Round(invoice.GrandTotal() - allocated - creditAndWriteOffAsOf, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal SignedAp(PurchaseInvoiceHeader bill, DateTime asOf, decimal creditAndAdvanceAsOf = 0m)
        {
            var day = asOf.Date;
            if (bill.IsDraft || bill.Date.Date > day) return 0m;
            if (bill.IsVoid)
            {
                var voided = bill.VoidGeneralLedgerHeader?.Date ?? bill.VoidedAt;
                if (voided == null || voided.Value.Date <= day) return 0m;
            }
            var paid = (bill.VendorPayments ?? new List<VendorPayment>())
                .Where(p => p.Date.Date <= day)
                .Where(p =>
                {
                    if (!p.IsVoid) return true;
                    var paymentVoided = p.VoidGeneralLedgerHeader?.Date ?? p.VoidedAt;
                    return paymentVoided != null && paymentVoided.Value.Date > day;
                })
                .Sum(p => p.Amount);
            return Math.Round(bill.Total() - paid - creditAndAdvanceAsOf, 2, MidpointRounding.AwayFromZero);
        }

        private bool TryResolvePermanentAccount(GeneralLedgerSetting? glSetting, IReadOnlyList<Account> accounts, out Account? permanent, out string? problem)
        {
            permanent = null;
            problem = null;
            if (glSetting?.PermanentAccountId == null)
            {
                problem = "Permanent equity account is not configured.";
                return false;
            }
            var account = accounts.FirstOrDefault(a => a.Id == glSetting.PermanentAccountId.Value);
            if (account == null)
            {
                problem = "Permanent equity account was not found in this workspace.";
                return false;
            }
            if (account.CompanyId != _tenant.RequireCompanyId() || account.AccountClassId != (int)AccountClasses.Equity
                || account.ArchivedAt != null || accounts.Any(a => a.ParentAccountId == account.Id))
            {
                problem = "The permanent account must be an active class 3 account that can be posted to.";
                return false;
            }
            var entity = _settings.Table.Select(s => s.EntityType).FirstOrDefault();
            if ((entity == EntityType.Corporation || entity == EntityType.Cooperative) && account.AccountCode == "30100")
            {
                problem = "The configured permanent account is 30100, the protected Share Capital or Member Capital role. Those accounts stay untouched. Accounting confirmation is required before a different rule is applied.";
                return false;
            }
            permanent = account;
            return true;
        }

        private Dictionary<string, string> NotApplicableReasons(CompanySetting? setting, FiscalYearCloseStatus status)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (status == FiscalYearCloseStatus.Closed)
            {
                foreach (var code in YearEndCheckCatalog.TierA.Concat(YearEndCheckCatalog.TierB))
                    map[code] = "This fiscal year is already closed.";
                return map;
            }
            var partnerMode = setting?.EntityType == EntityType.Partnership && setting.PartnerAccountsFrom.HasValue;
            if (!partnerMode)
                map["partner_equity_invalid"] = "Partner equity applies when partner accounts are in use.";
            var personal = setting?.EntityType == EntityType.Individual;
            if (personal && !_salesInvoices.Table.Any())
            {
                map["draft_invoices"] = "Personal books do not use customer invoices.";
                map["unposted_issued_documents"] = "Personal books do not use customer invoices.";
                map["stale_ar_ap_as_of_cutoff"] = "Personal books do not use customer invoices or vendor bills.";
                map["ar_ap_control_tieout_failed"] = "Personal books do not use customer invoices or vendor bills.";
            }
            if (personal && !_purchaseInvoices.Table.Any())
                map["draft_bills"] = "Personal books do not use vendor bills.";
            if (_customers == null || _vendors == null)
                map["ar_ap_control_tieout_failed"] = "Control-account tie-out runs when customer and vendor accounts are loaded.";
            return map;
        }

        private long ReadRevision()
        {
            if (_closeStates == null) return 0;
            return _closeStates.Table.OrderBy(s => s.Id).Select(s => (long?)s.AccountingRevision).FirstOrDefault() ?? 0;
        }

        private (string Status, bool CanReopen, string? Reason) IssuanceEligibility(FinancialYear year, FiscalYearCloseStatus status)
        {
            if (status != FiscalYearCloseStatus.Closed)
                return ("Unknown", false, null);
            if (_issuances == null)
                return ("Unknown", true, null);
            var value = LatestIssuance(year.Id)?.Status ?? "Unknown";
            if (value == "Issued")
                return (value, false, "Issued or filed financial statements stay closed. Use a prior-period adjustment.");
            if (value != "Unissued")
                return ("Unknown", false, "Declare whether the annual financial statements for this year were issued before reopening.");
            var latestClosed = _fiscalYears.Table.Where(fy => fy.ClosedAt != null).OrderByDescending(fy => fy.EndDate).FirstOrDefault();
            if (latestClosed == null || latestClosed.Id != year.Id)
                return (value, false, "Only the latest closed fiscal year can be reopened.");
            return (value, true, null);
        }

        private YearEndIssuanceDeclaration? LatestIssuance(int financialYearId) =>
            _issuances?.Table.Where(i => i.FinancialYearId == financialYearId).OrderByDescending(i => i.Id).FirstOrDefault();

        private void EnsureReopenAllowed(FinancialYear year, CompanySetting setting, YearEndReopenCommand? command)
        {
            if (command?.IndependentReviewerRequired == true && command.ActorIsOwnerOrAdmin != true)
                throw new YearEndCloseException("An owner or admin must approve reopening when independent review is required.", "validation");
            if (_issuances == null) return;
            var (_, canReopen, reason) = IssuanceEligibility(year, FiscalYearCloseStatus.Closed);
            if (!canReopen)
                throw new YearEndCloseException(reason ?? "This fiscal year cannot be reopened.", "validation");
        }

        private bool TryReplay(FinancialYear year, bool acknowledgeWarnings, YearEndCloseCommand? command, out YearEndPreview? preview)
        {
            preview = null;
            var key = command?.IdempotencyKey?.Trim();
            if (string.IsNullOrEmpty(key) || _receipts == null) return false;
            if (key.Length > 80)
                throw new YearEndCloseException("Idempotency-Key must be 80 characters or fewer.", "validation");
            var digest = YearEndPreviewHash.RequestDigest(year.FiscalYearCode, acknowledgeWarnings, command!.WarningExceptionReason, command.PreviewHash);
            var existing = _receipts.Table.FirstOrDefault(r => r.Operation == "year-end-close" && r.IdempotencyKey == key);
            if (existing == null) return false;
            if (!string.Equals(existing.RequestDigest, digest, StringComparison.Ordinal))
                throw new YearEndCloseException("This idempotency key was already used for a different close request.", "idempotency_conflict");
            if (year.ClosedAt == null)
                throw new YearEndCloseException("This idempotency key already completed a close that is no longer current.", "idempotency_conflict");
            preview = BuildPreview(year);
            return true;
        }

        private void PersistClosePackage(FinancialYear year, YearEndPreview preview, string actor, int? closingGlId,
            DateTime? lockBefore, DateTime? lockAfter, string? warningReason, bool acknowledgeWarnings, YearEndCloseCommand? command,
            IEnumerable<string>? evidenceLines = null)
        {
            if (_runs == null || _evidence == null) return;
            var attempt = (_runs.Table.Where(r => r.FinancialYearId == year.Id).Select(r => (int?)r.AttemptNo).Max() ?? 0) + 1;
            var payload = string.Join("\n", new[]
            {
                "schema=1",
                "hash=" + preview.PreviewHash,
                "revision=" + preview.AccountingRevision.ToString(CultureInfo.InvariantCulture),
                "currency=" + (_settings.Table.Select(s => s.Currency).FirstOrDefault() ?? ""),
                "basis=Accrual",
                "presentation=" + (_settings.Table.Select(s => s.AccountingBasis).FirstOrDefault().ToString()),
                "cash.note=Cash-basis figures are a projection and are not the amounts closed."
            }.Concat(evidenceLines ?? Array.Empty<string>())
            .Concat(preview.Checks.OrderBy(c => c.Code, StringComparer.Ordinal).Select(c =>
                "check=" + c.Code + "|" + c.State + "|" + c.Count.ToString(CultureInfo.InvariantCulture) + "|" + (c.Amount ?? 0m).ToString("0.0000", CultureInfo.InvariantCulture)))
            .Concat(preview.ClosingLines.OrderBy(l => l.AccountId).ThenBy(l => l.DrCr, StringComparer.Ordinal).Select(l =>
                "line=" + l.AccountId.ToString(CultureInfo.InvariantCulture) + "|" + l.AccountCode + "|" + l.DrCr + "|" + l.Amount.ToString("0.0000", CultureInfo.InvariantCulture))));
            var run = new YearEndCloseRun
            {
                CompanyId = year.CompanyId,
                FinancialYearId = year.Id,
                AttemptNo = attempt,
                PreviewHash = preview.PreviewHash,
                PolicyVersion = YearEndPreviewHash.PolicyVersion,
                SourceRevision = preview.AccountingRevision,
                Actor = actor,
                ClosedAtUtc = DateTime.UtcNow,
                BooksLockedThroughBefore = lockBefore,
                BooksLockedThroughAfter = lockAfter,
                ClosingGeneralLedgerHeaderId = closingGlId,
                WarningExceptionReason = string.IsNullOrWhiteSpace(warningReason) ? null : warningReason.Trim(),
                RecordedAuthority = command?.ActorIsOwnerOrAdmin == true ? "OwnerOrAdmin" : "PeriodsClose"
            };
            _runs.Insert(run);
            _evidence.Insert(new YearEndCloseEvidence
            {
                CompanyId = year.CompanyId,
                YearEndCloseRunId = run.Id,
                SchemaVersion = YearEndPreviewHash.PolicyVersion,
                ContentDigest = YearEndPreviewHash.Hex(payload),
                Payload = payload
            });
            AppendLifecycle(year, "Close", actor, warningReason, lockBefore, lockAfter, run.Id);
            if (_issuances != null && LatestIssuance(year.Id) == null)
            {
                _issuances.Insert(new YearEndIssuanceDeclaration
                {
                    CompanyId = year.CompanyId,
                    FinancialYearId = year.Id,
                    YearEndCloseRunId = run.Id,
                    Status = "Unissued",
                    Actor = actor,
                    DeclaredAtUtc = DateTime.UtcNow,
                    Reason = "Recorded when the year was closed. This is not an external issuance."
                });
            }
            var key = command?.IdempotencyKey?.Trim();
            if (_receipts != null && !string.IsNullOrEmpty(key))
            {
                _receipts.Insert(new YearEndCommandReceipt
                {
                    CompanyId = year.CompanyId,
                    Operation = "year-end-close",
                    IdempotencyKey = key,
                    RequestDigest = YearEndPreviewHash.RequestDigest(year.FiscalYearCode, acknowledgeWarnings, command?.WarningExceptionReason, command?.PreviewHash),
                    YearEndCloseRunId = run.Id,
                    CommittedAtUtc = DateTime.UtcNow
                });
            }
        }

        private void AppendLifecycle(FinancialYear year, string eventType, string actor, string? reason, DateTime? lockBefore, DateTime? lockAfter, int? runId)
        {
            if (_events == null) return;
            _events.Insert(new YearEndLifecycleEvent
            {
                CompanyId = year.CompanyId,
                FinancialYearId = year.Id,
                YearEndCloseRunId = runId,
                EventType = eventType,
                Actor = actor,
                OccurredAtUtc = DateTime.UtcNow,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                BooksLockedThroughBefore = lockBefore,
                BooksLockedThroughAfter = lockAfter
            });
        }

        private IEnumerable<string> StatementEvidence(string when, FinancialYear year)
        {
            var end = year.EndDate.Date;
            var start = year.StartDate.Date;
            foreach (var basis in new[] { AccountingBasis.Accrual, AccountingBasis.Cash })
            {
                var label = basis == AccountingBasis.Cash ? "projection.cash" : "accrual";
                IEnumerable<string> Lines()
                {
                    foreach (var row in _financial.BalanceSheet(null, end, basis).GroupBy(r => r.AccountClassId))
                        yield return "statement|" + when + "|" + label + "|class|" + row.Key.ToString(CultureInfo.InvariantCulture) + "|" + row.Sum(r => r.Amount).ToString("0.0000", CultureInfo.InvariantCulture);
                    var income = _financial.IncomeStatement(start, end, basis);
                    var net = income.Sum(r => r.IsExpense ? -r.Amount : r.Amount);
                    yield return "statement|" + when + "|" + label + "|net|" + net.ToString("0.0000", CultureInfo.InvariantCulture);
                }
                if (basis == AccountingBasis.Cash)
                {
                    IEnumerable<string> cash;
                    try { cash = Lines().ToList(); }
                    catch (Exception) { cash = new[] { "statement|" + when + "|projection.cash|unavailable" }; }
                    foreach (var line in cash) yield return line;
                }
                else
                {
                    foreach (var line in Lines()) yield return line;
                }
            }
        }

        private IEnumerable<string> AgingEvidence(DateTime asOf)
        {
            string Bucket(DateTime due)
            {
                var days = (asOf.Date - due.Date).Days;
                if (days <= 0) return "current";
                if (days <= 30) return "d1to30";
                if (days <= 60) return "d31to60";
                if (days <= 90) return "d61to90";
                return "over90";
            }
            var names = new[] { "current", "d1to30", "d31to60", "d61to90", "over90" };
            foreach (var side in new[] { "ar", "ap" })
            {
                var totals = names.ToDictionary(n => n, _ => 0m);
                if (side == "ar")
                {
                    foreach (var invoice in _salesInvoices.GetAllIncluding(i => i.SalesInvoiceLines, i => i.CustomerAllocations, i => i.VoidGeneralLedgerHeader).ToList())
                    {
                        if (invoice.Status == SalesInvoiceStatus.Draft || invoice.Date.Date > asOf) continue;
                        var amount = invoice.BalanceDueAsOf(asOf, invoice.VoidGeneralLedgerHeader?.Date);
                        if (Math.Abs(amount) <= TieTolerance) continue;
                        var due = invoice.DueDate?.Date ?? invoice.Date.Date.AddDays(30);
                        totals[Bucket(due)] += amount;
                    }
                }
                else
                {
                    foreach (var bill in _purchaseInvoices.GetAllIncluding(b => b.PurchaseInvoiceLines, b => b.VendorPayments, b => b.VoidGeneralLedgerHeader).ToList())
                    {
                        if (bill.IsDraft || bill.Date.Date > asOf) continue;
                        var amount = bill.BalanceDueAsOf(asOf);
                        if (Math.Abs(amount) <= TieTolerance) continue;
                        var due = bill.DueDate?.Date ?? bill.Date.Date.AddDays(30);
                        totals[Bucket(due)] += amount;
                    }
                }
                foreach (var name in names)
                    yield return "aging|" + side + "|" + name + "|" + totals[name].ToString("0.0000", CultureInfo.InvariantCulture);
            }
        }

        private static void CollapseChecks(List<YearEndCheckItem> items)
        {
            if (items.Count < 2) return;
            var grouped = items.GroupBy(i => i.Code).Select(g =>
            {
                var first = g.First();
                first.Count = g.Sum(x => x.Count);
                first.Message = string.Join(" ", g.Select(x => x.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
                var amounts = g.Where(x => x.Amount.HasValue).Select(x => x.Amount!.Value).ToList();
                first.Amount = amounts.Count == 0 ? null : amounts.Sum();
                return first;
            }).ToList();
            items.Clear();
            items.AddRange(grouped);
        }

        private static DateTime ExclusiveEnd(DateTime end)
        {
            var date = end.Date;
            if (date == DateTime.MaxValue.Date)
                throw new YearEndCloseException("The fiscal year end is too late to evaluate.", "validation");
            return date.AddDays(1);
        }
    }
}
