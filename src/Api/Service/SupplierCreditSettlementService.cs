// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed class SupplierCreditSettlementService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly SettlementCommandCoordinator _coordinator;
        private readonly SettlementAccountMappingService _mappings;

        public SupplierCreditSettlementService(
            ApiDbContext db,
            ITenantContext tenant,
            Services.Financial.IFinancialService financial,
            SettlementCommandCoordinator coordinator,
            SettlementAccountMappingService mappings)
        {
            _db = db;
            _tenant = tenant;
            _financial = financial;
            _coordinator = coordinator;
            _mappings = mappings;
        }

        public SupplierCreditNoteDto SaveDraft(SaveSupplierCreditDraftRequest request)
        {
            return _coordinator.Execute(
                "supplier_credit.save_draft",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.VendorCreditNo))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "Supplier credit reference number is required.", 400);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A business reason is required.", 400);
                    if (request.Lines == null || request.Lines.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "At least one credit line is required.", 400);
                    if (request.IsStandalone && string.IsNullOrWhiteSpace(request.EvidenceReference))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "Standalone supplier credits require supplier acknowledgement evidence.", 400);

                    var vendor = _db.Vendors.FirstOrDefault(v => v.CompanyId == companyId && v.Id == request.VendorId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Vendor not found.", 404);

                    PurchaseInvoiceHeader? bill = null;
                    if (request.SourcePurchaseInvoiceHeaderId.HasValue)
                    {
                        bill = _db.PurchaseInvoiceHeaders
                            .Include(b => b.PurchaseInvoiceLines)
                            .Include(b => b.VendorPayments)
                            .FirstOrDefault(b => b.CompanyId == companyId && b.Id == request.SourcePurchaseInvoiceHeaderId.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source bill not found.", 404);

                        if (bill.VendorId != vendor.Id)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier credit vendor must match the source bill vendor.", 400);
                        if (bill.IsDraft || bill.IsVoid)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot record a supplier credit for a draft or void bill.", 400);
                    }

                    SupplierCreditNote note;
                    if (request.Id.HasValue)
                    {
                        note = _db.SupplierCreditNotes.Include(c => c.Lines)
                            .FirstOrDefault(c => c.CompanyId == companyId && c.Id == request.Id.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit note not found.", 404);
                        if (note.Status != SettlementDocumentStatus.Draft)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft supplier credits can be edited.", 409);
                        if (request.ExpectedVersion.HasValue && note.Version != request.ExpectedVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier credit changed concurrently.", 409);

                        _db.SupplierCreditNoteLines.RemoveRange(note.Lines);
                        note.Lines.Clear();
                    }
                    else
                    {
                        note = new SupplierCreditNote { CompanyId = companyId, Status = SettlementDocumentStatus.Draft };
                        _db.SupplierCreditNotes.Add(note);
                    }

                    var computed = ComputeLines(companyId, precision, bill, request.Lines, request.IsStandalone);
                    note.VendorId = vendor.Id;
                    note.SourcePurchaseInvoiceHeaderId = bill?.Id;
                    note.VendorCreditNo = request.VendorCreditNo.Trim();
                    note.Date = request.Date.Date;
                    note.EffectiveDate = (request.EffectiveDate ?? request.Date).Date;
                    note.CurrencyCode = setting.Currency;
                    note.CurrencyPrecision = precision;
                    note.Kind = (CreditNoteKind)request.Kind;
                    note.IsStandalone = request.IsStandalone || bill == null;
                    note.Reason = request.Reason.Trim();
                    note.EvidenceReference = request.EvidenceReference?.Trim();

                    foreach (var line in computed)
                    {
                        line.CompanyId = companyId;
                        note.Lines.Add(line);
                    }

                    note.Subtotal = computed.Sum(l => l.Subtotal);
                    note.TaxAmount = computed.Sum(l => l.TaxAmount);
                    note.TotalAmount = computed.Sum(l => l.TotalAmount);
                    if (note.TotalAmount <= 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier credit total must be greater than zero.", 400);

                    if (bill != null)
                    {
                        var priorCredited = _db.SupplierCreditNotes
                            .Where(c => c.CompanyId == companyId && c.SourcePurchaseInvoiceHeaderId == bill.Id && c.Status == SettlementDocumentStatus.Posted && c.Id != note.Id)
                            .Sum(c => (decimal?)c.TotalAmount) ?? 0m;
                        var maxAllowed = CurrencyPrecisionRules.RoundMoney(bill.Total() - priorCredited, precision);
                        if (note.TotalAmount > maxAllowed)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Supplier credit ({note.TotalAmount}) exceeds remaining bill amount ({maxAllowed}).", 409);
                    }

                    note.Version++;
                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public SupplierCreditNoteDto SubmitForReview(int id, string? idempotencyKey = null)
        {
            return _coordinator.Execute(
                "supplier_credit.submit",
                idempotencyKey,
                new { id },
                (companyId, _) =>
                {
                    var note = _db.SupplierCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit not found.", 404);
                    if (note.Status != SettlementDocumentStatus.Draft)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft supplier credits can be submitted.", 409);

                    note.Status = SettlementDocumentStatus.PendingReview;
                    note.SubmittedBy = _tenant.UserId ?? "system";
                    note.SubmittedAtUtc = DateTime.UtcNow;
                    note.Version++;
                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public SupplierCreditNoteDto Post(int id, PostSupplierCreditRequest request)
        {
            return _coordinator.Execute(
                "supplier_credit.post",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var note = _db.SupplierCreditNotes.Include(c => c.Lines)
                        .FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit not found.", 404);

                    if (note.Status is not (SettlementDocumentStatus.Draft or SettlementDocumentStatus.PendingReview))
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft or pending-review supplier credits can be posted.", 409);
                    if (request.ExpectedVersion.HasValue && note.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier credit changed concurrently.", 409);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureReviewPolicyForPosting(companyId, note.SubmittedBy, actor, requiresTeamReview: true);

                    var vendor = _db.Vendors.Include(v => v.Party).First(v => v.CompanyId == companyId && v.Id == note.VendorId);
                    PurchaseInvoiceHeader? bill = null;
                    if (note.SourcePurchaseInvoiceHeaderId.HasValue)
                    {
                        bill = _db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Include(b => b.VendorPayments)
                            .FirstOrDefault(b => b.CompanyId == companyId && b.Id == note.SourcePurchaseInvoiceHeaderId.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source bill not found.", 404);
                        if (note.EffectiveDate.Date < bill.Date.Date)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Supplier credit effective date cannot precede source bill date.", 400);
                    }

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var creditAssetId = _mappings.RequireActiveAccount(companyId, glSetting?.SupplierCreditAssetAccountId, "Supplier Credits Receivable");

                    note.BillFromJson = InvoiceBillTo.FromParty(vendor.Party).ToJson();
                    note.SupplierCreditAssetAccountId = creditAssetId;
                    note.PayableAccountId = bill?.PayableAccountId ?? vendor.AccountsPayableAccountId;
                    note.No ??= GenerateNumber("SCN", _db.SupplierCreditNotes.Where(c => c.CompanyId == companyId && c.No != null).Select(c => c.No!));

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.SupplierCreditNote,
                        note.EffectiveDate,
                        $"Supplier Credit {note.No} ({note.VendorCreditNo})",
                        note.Id);

                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, creditAssetId, note.TotalAmount));

                    foreach (var expGroup in note.Lines.GroupBy(l => l.ExpenseOrAssetAccountId!.Value))
                    {
                        var amt = CurrencyPrecisionRules.RoundMoney(expGroup.Sum(l => l.Subtotal), precision);
                        if (amt > 0)
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, expGroup.Key, amt));
                    }

                    foreach (var taxGroup in note.Lines.Where(l => l.TaxAmount > 0 && l.TaxAccountId.HasValue).GroupBy(l => l.TaxAccountId!.Value))
                    {
                        var amt = CurrencyPrecisionRules.RoundMoney(taxGroup.Sum(l => l.TaxAmount), precision);
                        if (amt > 0)
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, taxGroup.Key, amt));
                    }

                    _financial.SaveGeneralLedgerEntry(glHeader);

                    note.GeneralLedgerHeaderId = glHeader.Id;
                    note.Status = SettlementDocumentStatus.Posted;
                    note.PostedBy = actor;
                    note.PostedAtUtc = DateTime.UtcNow;
                    if (note.SubmittedBy == null)
                    {
                        note.SubmittedBy = actor;
                        note.SubmittedAtUtc = note.PostedAtUtc;
                    }
                    else
                    {
                        note.ReviewedBy = actor;
                        note.ReviewedAtUtc = note.PostedAtUtc;
                    }
                    note.Version++;
                    _db.SaveChanges();

                    if (request.ApplyToSourceBillAmount is > 0m)
                    {
                        if (bill == null)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Cannot auto-apply a standalone supplier credit.", 400);
                        ApplyInternal(companyId, precision, note, bill, request.ApplyToSourceBillAmount.Value, (request.ApplicationDate ?? note.EffectiveDate).Date, actor);
                        _db.SaveChanges();
                    }

                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public SupplierCreditNoteDto Apply(int id, ApplySupplierCreditRequest request)
        {
            return _coordinator.Execute(
                "supplier_credit.apply",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var note = _db.SupplierCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit not found.", 404);

                    if (note.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted supplier credits can be applied.", 409);
                    if (request.ExpectedCreditVersion.HasValue && note.Version != request.ExpectedCreditVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier credit changed concurrently.", 409);
                    if (request.Applications == null || request.Applications.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Select at least one bill to apply.", 400);

                    var actor = _tenant.UserId ?? "system";
                    foreach (var item in request.Applications)
                    {
                        var bill = _db.PurchaseInvoiceHeaders
                            .Include(b => b.PurchaseInvoiceLines)
                            .Include(b => b.VendorPayments)
                            .FirstOrDefault(b => b.CompanyId == companyId && b.Id == item.PurchaseInvoiceHeaderId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, $"Bill {item.PurchaseInvoiceHeaderId} not found.", 404);

                        if (item.ExpectedBillVersion.HasValue && bill.Version != item.ExpectedBillVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, $"Bill {bill.No} changed concurrently.", 409);

                        ApplyInternal(companyId, precision, note, bill, item.Amount, request.EffectiveDate.Date, actor);
                    }

                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public SupplierCreditNoteDto ReverseApplication(int applicationId, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "supplier_settlement_application.reverse",
                request.IdempotencyKey,
                new { applicationId, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var app = _db.SettlementApplications
                        .FirstOrDefault(a => a.CompanyId == companyId && a.Id == applicationId && a.SourceType == SettlementSourceType.SupplierCreditNote)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Settlement application not found.", 404);
                    if (app.IsReversed)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Application is already reversed.", 409);
                    if (request.ReversalDate.Date < app.EffectiveDate.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede the application date.", 400);

                    var note = _db.SupplierCreditNotes.First(c => c.CompanyId == companyId && c.Id == app.SupplierCreditNoteId!.Value);
                    var bill = _db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Include(b => b.VendorPayments)
                        .First(b => b.CompanyId == companyId && b.Id == app.PurchaseInvoiceHeaderId!.Value);

                    var revGl = _financial.CreateReversingEntry(
                        app.GeneralLedgerHeaderId,
                        $"Reverse supplier credit application {note.No} -> {bill.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    _db.SettlementApplicationReversals.Add(new SettlementApplicationReversal
                    {
                        CompanyId = companyId,
                        SettlementApplicationId = app.Id,
                        ReversalDate = request.ReversalDate.Date,
                        Reason = request.Reason.Trim(),
                        ReversedBy = _tenant.UserId ?? "system",
                        ReversedAtUtc = DateTime.UtcNow,
                        GeneralLedgerHeaderId = revGl.Id
                    });

                    app.IsReversed = true;
                    app.Version++;
                    note.AppliedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, note.AppliedAmount - app.Amount), precision);
                    note.Version++;
                    bill.CreditedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, bill.CreditedAmount - app.Amount), precision);
                    bill.Version++;

                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public SupplierCreditNoteDto Reverse(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "supplier_credit.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, _) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var note = _db.SupplierCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit not found.", 404);
                    if (note.Status != SettlementDocumentStatus.Posted || !note.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted supplier credits can be reversed.", 409);
                    if (request.ReversalDate.Date < note.EffectiveDate.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede effective date.", 400);
                    if (note.AppliedAmount > 0 || note.RefundedAmount > 0)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Reverse all applications and refunds before reversing the supplier credit.", 409);

                    var revGl = _financial.CreateReversingEntry(
                        note.GeneralLedgerHeaderId.Value,
                        $"Reverse supplier credit {note.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    note.Status = SettlementDocumentStatus.Reversed;
                    note.VoidGeneralLedgerHeaderId = revGl.Id;
                    note.ReversedBy = _tenant.UserId ?? "system";
                    note.ReversedAtUtc = DateTime.UtcNow;
                    note.ReversalReason = request.Reason.Trim();
                    note.Version++;

                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public IReadOnlyList<SupplierCreditNoteDto> List(int? vendorId = null, int? billId = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var query = _db.SupplierCreditNotes.AsNoTracking().Where(c => c.CompanyId == companyId);
            if (vendorId.HasValue) query = query.Where(c => c.VendorId == vendorId.Value);
            if (billId.HasValue) query = query.Where(c => c.SourcePurchaseInvoiceHeaderId == billId.Value);
            var ids = query.OrderByDescending(c => c.EffectiveDate).ThenByDescending(c => c.Id).Select(c => c.Id).ToList();
            return ids.Select(id => BuildDto(companyId, id)).ToList();
        }

        public SupplierCreditNoteDto Get(int id)
        {
            var companyId = _tenant.RequireCompanyId();
            return BuildDto(companyId, id);
        }

        private void ApplyInternal(int companyId, int precision, SupplierCreditNote note, PurchaseInvoiceHeader bill, decimal amount, DateTime effectiveDate, string actor)
        {
            if (amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(amount, precision))
                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Application amount must be positive and match currency precision.", 400);
            if (bill.VendorId != note.VendorId)
                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier credit and bill must belong to the same vendor.", 400);
            if (bill.IsDraft || bill.IsVoid)
                throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot apply supplier credit to a draft or void bill.", 400);
            if (effectiveDate < note.EffectiveDate.Date || effectiveDate < bill.Date.Date)
                throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Application date must be on or after both credit effective date and bill date.", 400);
            if (amount > note.AvailableAmount)
                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application amount ({amount}) exceeds available supplier credit ({note.AvailableAmount}).", 409);

            var billDue = CurrencyPrecisionRules.RoundMoney(bill.BalanceDue(), precision);
            if (amount > billDue)
                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application amount ({amount}) exceeds bill balance due ({billDue}).", 409);

            var sourceAccId = note.SupplierCreditAssetAccountId
                ?? throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Supplier credit asset account snapshot is missing.", 422);
            var vendor = _db.Vendors.AsNoTracking().First(v => v.CompanyId == companyId && v.Id == bill.VendorId!.Value);
            var destAccId = _mappings.RequireActiveAccount(companyId, bill.PayableAccountId ?? vendor.AccountsPayableAccountId, "Accounts Payable");
            bill.PayableAccountId ??= destAccId;

            var glHeader = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.SettlementApplication,
                effectiveDate,
                $"Apply Supplier Credit {note.No} to Bill {bill.No ?? bill.VendorInvoiceNo}",
                note.Id);
            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, destAccId, amount));
            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, sourceAccId, amount));
            _financial.SaveGeneralLedgerEntry(glHeader);

            _db.SettlementApplications.Add(new SettlementApplication
            {
                CompanyId = companyId,
                SourceType = SettlementSourceType.SupplierCreditNote,
                SupplierCreditNoteId = note.Id,
                DestinationType = SettlementDestinationType.PurchaseInvoice,
                PurchaseInvoiceHeaderId = bill.Id,
                CurrencyCode = note.CurrencyCode,
                Amount = amount,
                EffectiveDate = effectiveDate,
                SourceAccountId = sourceAccId,
                DestinationAccountId = destAccId,
                GeneralLedgerHeaderId = glHeader.Id,
                CreatedBy = actor,
                CreatedAtUtc = DateTime.UtcNow
            });

            note.AppliedAmount = CurrencyPrecisionRules.RoundMoney(note.AppliedAmount + amount, precision);
            note.Version++;
            bill.CreditedAmount = CurrencyPrecisionRules.RoundMoney(bill.CreditedAmount + amount, precision);
            bill.Version++;
        }

        private List<SupplierCreditNoteLine> ComputeLines(
            int companyId,
            int precision,
            PurchaseInvoiceHeader? bill,
            IReadOnlyList<SupplierCreditLineInputDto> inputs,
            bool isStandalone)
        {
            var taxesById = _db.Taxes.AsNoTracking().Where(t => t.CompanyId == companyId).ToDictionary(t => t.Id);
            var billLinesById = bill?.PurchaseInvoiceLines.ToDictionary(l => l.Id) ?? new Dictionary<int, PurchaseInvoiceLine>();
            var result = new List<SupplierCreditNoteLine>(inputs.Count);

            foreach (var input in inputs)
            {
                if (input.Quantity <= 0 || input.UnitCost < 0)
                    throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier credit line quantity must be positive and unit cost non-negative.", 400);

                PurchaseInvoiceLine? srcLine = null;
                if (input.SourcePurchaseInvoiceLineId.HasValue)
                {
                    if (!billLinesById.TryGetValue(input.SourcePurchaseInvoiceLineId.Value, out srcLine))
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Source bill line does not belong to the source bill.", 400);
                }

                var expAccId = input.ExpenseOrAssetAccountId ?? srcLine?.AccountId;
                if (!expAccId.HasValue)
                    throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Expense or asset account classification is required for each supplier credit line.", 422);
                _ = _mappings.RequireActiveAccount(companyId, expAccId.Value, "Supplier Credit Expense/Asset");

                var subtotal = CurrencyPrecisionRules.RoundMoney(input.Quantity * input.UnitCost, precision);
                var taxId = input.TaxId ?? srcLine?.TaxId;
                decimal taxRate = 0m;
                int? taxAccountId = null;
                if (taxId.HasValue && taxesById.TryGetValue(taxId.Value, out var tax) && tax != null)
                {
                    taxRate = tax.Rate;
                    taxAccountId = tax.PurchasingAccountId ?? tax.SalesAccountId;
                }

                var taxAmount = input.OverrideTaxAmount.HasValue
                    ? CurrencyPrecisionRules.RoundMoney(input.OverrideTaxAmount.Value, precision)
                    : (input.TaxRecoverable && taxRate > 0 ? CurrencyPrecisionRules.RoundMoney(subtotal * taxRate / 100m, precision) : 0m);

                if (taxAmount > 0 && !taxAccountId.HasValue)
                    throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Input tax account is not configured for the selected tax.", 422);

                result.Add(new SupplierCreditNoteLine
                {
                    SourcePurchaseInvoiceLineId = srcLine?.Id,
                    MatchedReturnDispatchId = input.MatchedReturnDispatchId,
                    ItemId = input.ItemId ?? srcLine?.ItemId,
                    ExpenseOrAssetAccountId = expAccId.Value,
                    TaxId = taxId,
                    TaxAccountId = taxAccountId,
                    TaxRateSnapshot = taxRate,
                    TaxRecoverableSnapshot = input.TaxRecoverable,
                    Description = input.Description?.Trim() ?? srcLine?.DisplayDescription ?? "Supplier credit adjustment",
                    Quantity = input.Quantity,
                    UnitCost = input.UnitCost,
                    Subtotal = subtotal,
                    TaxAmount = taxAmount,
                    TotalAmount = CurrencyPrecisionRules.RoundMoney(subtotal + taxAmount, precision)
                });
            }

            return result;
        }

        private SupplierCreditNoteDto BuildDto(int companyId, int id)
        {
            var note = _db.SupplierCreditNotes.AsNoTracking()
                .Include(c => c.Vendor).ThenInclude(v => v!.Party)
                .Include(c => c.SourcePurchaseInvoiceHeader)
                .Include(c => c.Lines)
                .FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit not found.", 404);

            var apps = _db.SettlementApplications.AsNoTracking()
                .Include(a => a.PurchaseInvoiceHeader)
                .Include(a => a.Reversal)
                .Where(a => a.CompanyId == companyId && a.SourceType == SettlementSourceType.SupplierCreditNote && a.SupplierCreditNoteId == note.Id)
                .OrderBy(a => a.EffectiveDate).ThenBy(a => a.Id)
                .Select(a => new SettlementApplicationSummaryDto(
                    a.Id, note.Id, "SupplierCreditNote", a.PurchaseInvoiceHeaderId!.Value,
                    a.PurchaseInvoiceHeader != null ? (a.PurchaseInvoiceHeader.No ?? a.PurchaseInvoiceHeader.VendorInvoiceNo) : "",
                    a.Amount, a.EffectiveDate, a.GeneralLedgerHeaderId, a.IsReversed,
                    a.Reversal != null ? a.Reversal.ReversalDate : null,
                    a.Reversal != null ? a.Reversal.Reason : null))
                .ToList();

            var settlementState = note.Status != SettlementDocumentStatus.Posted
                ? note.Status.ToString()
                : note.AvailableAmount <= 0 ? "Used" : (note.AppliedAmount > 0 || note.RefundedAmount > 0 ? "PartlyUsed" : "Available");

            return new SupplierCreditNoteDto(
                note.Id, note.No, note.VendorCreditNo, note.VendorId, note.Vendor?.Party?.Name ?? "",
                note.SourcePurchaseInvoiceHeaderId, note.SourcePurchaseInvoiceHeader?.No ?? note.SourcePurchaseInvoiceHeader?.VendorInvoiceNo,
                note.Date, note.EffectiveDate, note.CurrencyCode, note.CurrencyPrecision, (int)note.Kind, note.IsStandalone,
                note.Reason, note.EvidenceReference, note.Subtotal, note.TaxAmount, note.TotalAmount,
                note.AppliedAmount, note.RefundedAmount, note.AvailableAmount, settlementState,
                (int)note.Status, note.Status.ToString(), note.GeneralLedgerHeaderId, note.VoidGeneralLedgerHeaderId,
                note.SubmittedBy, note.ReviewedBy, note.PostedBy, note.Version,
                note.Lines.Select(l => new SupplierCreditLineDto(
                    l.Id, l.SourcePurchaseInvoiceLineId, l.MatchedReturnDispatchId, l.ItemId, l.ExpenseOrAssetAccountId,
                    l.TaxId, l.TaxRateSnapshot, l.TaxRecoverableSnapshot, l.Description, l.Quantity, l.UnitCost,
                    l.Subtotal, l.TaxAmount, l.TotalAmount)).ToList(),
                apps);
        }

        private static string GenerateNumber(string prefix, IEnumerable<string> existing)
        {
            var max = 0;
            foreach (var s in existing)
            {
                if (s.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(s[(prefix.Length + 1)..], out var num) && num > max)
                {
                    max = num;
                }
            }
            return $"{prefix}-{(max + 1):D5}";
        }
    }
}
