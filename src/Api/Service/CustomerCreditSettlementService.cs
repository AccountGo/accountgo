// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;
using Services.Financial;

namespace Api.Service
{
    public sealed class CustomerCreditSettlementService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly SettlementCommandCoordinator _coordinator;
        private readonly SettlementAccountMappingService _mappings;

        public CustomerCreditSettlementService(
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

        public CustomerCreditPreviewDto Preview(SaveCustomerCreditDraftRequest request)
        {
            var companyId = _tenant.RequireCompanyId();
            var setting = _coordinator.EnsureFeatureEnabled(companyId);
            var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);

            SalesInvoiceHeader? invoice = null;
            if (request.SourceSalesInvoiceHeaderId.HasValue)
            {
                invoice = _db.SalesInvoiceHeaders
                    .Include(i => i.SalesInvoiceLines).ThenInclude(l => l.Tax)
                    .Include(i => i.CustomerAllocations)
                    .FirstOrDefault(i => i.CompanyId == companyId && i.Id == request.SourceSalesInvoiceHeaderId.Value)
                    ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source sales invoice not found.", 404);
            }

            var computed = ComputeLines(companyId, precision, invoice, request.Lines, request.Id);
            var subtotal = computed.Sum(l => l.Subtotal);
            var tax = computed.Sum(l => l.TaxAmount);
            var total = computed.Sum(l => l.TotalAmount);

            decimal remainingCreditable = total;
            decimal invoiceDue = 0m;
            if (invoice != null)
            {
                var priorCredited = _db.CustomerCreditNotes
                    .Where(c => c.CompanyId == companyId && c.SourceSalesInvoiceHeaderId == invoice.Id && c.Status == SettlementDocumentStatus.Posted && c.Id != request.Id)
                    .Sum(c => (decimal?)c.TotalAmount) ?? 0m;
                remainingCreditable = CurrencyPrecisionRules.RoundMoney(
                    Math.Max(0m, invoice.GrandTotal() - invoice.WrittenOffAmount - priorCredited), precision);
                invoiceDue = CurrencyPrecisionRules.RoundMoney(invoice.BalanceDue(), precision);
            }

            var defaultApply = invoice != null ? Math.Min(total, invoiceDue) : 0m;
            var mappingPreview = _mappings.GetPreview();
            var opSettings = _db.WorkspaceOperatingSettings.AsNoTracking().FirstOrDefault(s => s.CompanyId == companyId);
            var isSolo = opSettings != null && (opSettings.CollaborationState == WorkspaceCollaborationState.SoloActive || opSettings.Purpose == WorkspacePurpose.Personal);

            return new CustomerCreditPreviewDto(
                Subtotal: subtotal,
                TaxAmount: tax,
                TotalAmount: total,
                RemainingCreditableOnInvoice: remainingCreditable,
                InvoiceBalanceDue: invoiceDue,
                DefaultApplyAmount: defaultApply,
                RemainingAvailableAfterDefaultApply: CurrencyPrecisionRules.RoundMoney(total - defaultApply, precision),
                RequiresTeamReview: !isSolo,
                MappingsReady: mappingPreview.ReadyForSalesCredits,
                Lines: computed.Select((l, idx) => new CustomerCreditLineDto(
                    idx, l.SourceSalesInvoiceLineId, l.ItemId, l.TaxId, l.TaxRateSnapshot, l.IsShippingLine,
                    l.Description, l.Quantity, l.UnitPrice, l.DiscountAmount, l.Subtotal, l.TaxAmount, l.TotalAmount)).ToList());
        }

        public CustomerCreditNoteDto SaveDraft(SaveCustomerCreditDraftRequest request)
        {
            return _coordinator.Execute(
                "customer_credit.save_draft",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A business reason is required for a credit note.", 400);
                    if (request.Lines == null || request.Lines.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "At least one credit line is required.", 400);

                    var customer = _db.Customers.FirstOrDefault(c => c.CompanyId == companyId && c.Id == request.CustomerId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer not found.", 404);

                    SalesInvoiceHeader? invoice = null;
                    if (request.SourceSalesInvoiceHeaderId.HasValue)
                    {
                        invoice = _db.SalesInvoiceHeaders
                            .Include(i => i.SalesInvoiceLines).ThenInclude(l => l.Tax)
                            .Include(i => i.CustomerAllocations)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == request.SourceSalesInvoiceHeaderId.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source sales invoice not found.", 404);

                        if (invoice.CustomerId != customer.Id)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Credit note customer must match the source invoice customer.", 400);
                        if (invoice.Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot create a credit note for a draft or void invoice.", 400);
                    }

                    CustomerCreditNote note;
                    if (request.Id.HasValue)
                    {
                        note = _db.CustomerCreditNotes.Include(c => c.Lines)
                            .FirstOrDefault(c => c.CompanyId == companyId && c.Id == request.Id.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);
                        if (note.Status != SettlementDocumentStatus.Draft)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft credit notes can be edited.", 409);
                        if (request.ExpectedVersion.HasValue && note.Version != request.ExpectedVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Credit note was modified concurrently.", 409);

                        _db.CustomerCreditNoteLines.RemoveRange(note.Lines);
                        note.Lines.Clear();
                    }
                    else
                    {
                        note = new CustomerCreditNote { CompanyId = companyId, Status = SettlementDocumentStatus.Draft };
                        _db.CustomerCreditNotes.Add(note);
                    }

                    var computed = ComputeLines(companyId, precision, invoice, request.Lines, note.Id == 0 ? null : note.Id);
                    note.CustomerId = customer.Id;
                    note.SourceSalesInvoiceHeaderId = invoice?.Id;
                    note.Date = request.Date.Date;
                    note.CurrencyCode = setting.Currency;
                    note.CurrencyPrecision = precision;
                    note.Kind = (CreditNoteKind)request.Kind;
                    note.ReturnGoodsWithoutRestock = request.ReturnGoodsWithoutRestock;
                    note.Reason = request.Reason.Trim();
                    note.SourceReference = request.SourceReference?.Trim();
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
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Credit note total must be greater than zero.", 400);

                    ValidateInvoiceCreditCap(companyId, precision, invoice, note.TotalAmount, note.Id == 0 ? null : note.Id);

                    note.Version++;
                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(CustomerCreditNote));
        }

        public CustomerCreditNoteDto SubmitForReview(int id, string? idempotencyKey = null)
        {
            return _coordinator.Execute(
                "customer_credit.submit",
                idempotencyKey,
                new { id },
                (companyId, _) =>
                {
                    var note = _db.CustomerCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);
                    if (note.Status != SettlementDocumentStatus.Draft)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft credit notes can be submitted.", 409);

                    note.Status = SettlementDocumentStatus.PendingReview;
                    note.SubmittedBy = _tenant.UserId ?? "system";
                    note.SubmittedAtUtc = DateTime.UtcNow;
                    note.Version++;
                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(CustomerCreditNote));
        }

        public CustomerCreditNoteDto Post(int id, PostCustomerCreditRequest request)
        {
            return _coordinator.Execute(
                "customer_credit.post",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var note = _db.CustomerCreditNotes.Include(c => c.Lines)
                        .FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);

                    if (note.Status is not (SettlementDocumentStatus.Draft or SettlementDocumentStatus.PendingReview))
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft or pending-review credit notes can be posted.", 409);
                    if (request.ExpectedVersion.HasValue && note.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Credit note was modified concurrently.", 409);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureReviewPolicyForPosting(companyId, note.SubmittedBy, actor, requiresTeamReview: true);

                    var customer = _db.Customers.Include(c => c.Party).First(c => c.CompanyId == companyId && c.Id == note.CustomerId);
                    SalesInvoiceHeader? invoice = null;
                    if (note.SourceSalesInvoiceHeaderId.HasValue)
                    {
                        invoice = _db.SalesInvoiceHeaders
                            .Include(i => i.SalesInvoiceLines)
                            .Include(i => i.CustomerAllocations)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == note.SourceSalesInvoiceHeaderId.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source sales invoice not found.", 404);

                        if (note.Date.Date < invoice.Date.Date)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Credit note date cannot precede source invoice date.", 400);

                        ValidateInvoiceCreditCap(companyId, precision, invoice, note.TotalAmount, note.Id);
                    }

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var liabilityAccountId = _mappings.RequireActiveAccount(companyId, glSetting?.CustomerCreditLiabilityAccountId, "Customer Credits Liability");
                    var returnsAccountId = _mappings.RequireActiveAccount(companyId, glSetting?.SalesReturnsAllowancesAccountId, "Sales Returns & Allowances");
                    var receivableAccountId = invoice?.ReceivableAccountId ?? customer.AccountsReceivableAccountId;
                    if (invoice != null)
                        receivableAccountId = _mappings.RequireActiveAccount(companyId, receivableAccountId, "Accounts Receivable");

                    var company = _db.Companies.AsNoTracking().FirstOrDefault(c => c.Id == companyId);
                    note.SellerJson = InvoiceSeller.FromWorkspace(company, setting).ToJson();
                    note.BillToJson = InvoiceBillTo.FromParty(customer.Party).ToJson();
                    note.CustomerCreditLiabilityAccountId = liabilityAccountId;
                    note.SalesReturnsAccountId = returnsAccountId;
                    note.ReceivableAccountId = receivableAccountId;
                    note.No ??= GenerateNumber(companyId, "CRN", _db.CustomerCreditNotes.Where(c => c.CompanyId == companyId && c.No != null).Select(c => c.No!));

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.CustomerCreditNote,
                        note.Date,
                        $"Customer Credit Note {note.No}",
                        note.Id);

                    if (note.Subtotal > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, returnsAccountId, note.Subtotal));

                    foreach (var taxGroup in note.Lines.Where(l => l.TaxAmount > 0 && l.TaxAccountId.HasValue).GroupBy(l => l.TaxAccountId!.Value))
                    {
                        var taxAmt = CurrencyPrecisionRules.RoundMoney(taxGroup.Sum(l => l.TaxAmount), precision);
                        if (taxAmt > 0)
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, taxGroup.Key, taxAmt));
                    }

                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, liabilityAccountId, note.TotalAmount));
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

                    if (request.ApplyToSourceInvoiceAmount is > 0m)
                    {
                        if (invoice == null)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Cannot auto-apply a standalone credit note.", 400);

                        var applyDate = (request.ApplicationDate ?? note.Date).Date;
                        ApplyInternal(companyId, precision, note, invoice, request.ApplyToSourceInvoiceAmount.Value, applyDate, actor);
                        _db.SaveChanges();
                    }

                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(CustomerCreditNote));
        }

        public CustomerCreditNoteDto Apply(int id, ApplyCustomerCreditRequest request)
        {
            return _coordinator.Execute(
                "customer_credit.apply",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var note = _db.CustomerCreditNotes
                        .FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);

                    if (note.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted credit notes can be applied.", 409);
                    if (request.ExpectedCreditVersion.HasValue && note.Version != request.ExpectedCreditVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Credit note was modified concurrently.", 409);
                    if (request.Applications == null || request.Applications.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Select at least one invoice to apply.", 400);

                    var actor = _tenant.UserId ?? "system";
                    foreach (var item in request.Applications)
                    {
                        var invoice = _db.SalesInvoiceHeaders
                            .Include(i => i.SalesInvoiceLines)
                            .Include(i => i.CustomerAllocations)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == item.SalesInvoiceHeaderId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, $"Invoice {item.SalesInvoiceHeaderId} not found.", 404);

                        if (item.ExpectedInvoiceVersion.HasValue && invoice.Version != item.ExpectedInvoiceVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, $"Invoice {invoice.No} changed concurrently.", 409);

                        ApplyInternal(companyId, precision, note, invoice, item.Amount, request.EffectiveDate.Date, actor);
                    }

                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(CustomerCreditNote));
        }

        public CustomerCreditNoteDto ReverseApplication(int applicationId, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "settlement_application.reverse",
                request.IdempotencyKey,
                new { applicationId, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var app = _db.SettlementApplications
                        .FirstOrDefault(a => a.CompanyId == companyId && a.Id == applicationId && a.SourceType == SettlementSourceType.CustomerCreditNote)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Settlement application not found.", 404);

                    if (app.IsReversed)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Application is already reversed.", 409);
                    if (request.ExpectedVersion.HasValue && app.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Application changed concurrently.", 409);
                    if (request.ReversalDate.Date < app.EffectiveDate.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede the application date.", 400);

                    var note = _db.CustomerCreditNotes.First(c => c.CompanyId == companyId && c.Id == app.CustomerCreditNoteId!.Value);
                    var invoice = _db.SalesInvoiceHeaders
                        .Include(i => i.SalesInvoiceLines)
                        .Include(i => i.CustomerAllocations)
                        .First(i => i.CompanyId == companyId && i.Id == app.SalesInvoiceHeaderId!.Value);

                    var revGl = _financial.CreateReversingEntry(
                        app.GeneralLedgerHeaderId,
                        $"Reverse credit application {note.No} -> {invoice.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    var actor = _tenant.UserId ?? "system";
                    _db.SettlementApplicationReversals.Add(new SettlementApplicationReversal
                    {
                        CompanyId = companyId,
                        SettlementApplicationId = app.Id,
                        ReversalDate = request.ReversalDate.Date,
                        Reason = request.Reason.Trim(),
                        ReversedBy = actor,
                        ReversedAtUtc = DateTime.UtcNow,
                        GeneralLedgerHeaderId = revGl.Id
                    });

                    app.IsReversed = true;
                    app.Version++;
                    note.AppliedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, note.AppliedAmount - app.Amount), precision);
                    note.Version++;
                    invoice.CreditedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, invoice.CreditedAmount - app.Amount), precision);
                    invoice.Version++;
                    invoice.RefreshPaymentStatus();

                    _db.SaveChanges();
                    return BuildDto(companyId, note.Id);
                },
                dto => dto.Id,
                nameof(CustomerCreditNote));
        }

        public CustomerCreditNoteDto Reverse(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "customer_credit.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, _) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var note = _db.CustomerCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);

                    if (note.Status != SettlementDocumentStatus.Posted || !note.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted credit notes can be reversed.", 409);
                    if (request.ExpectedVersion.HasValue && note.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Credit note changed concurrently.", 409);
                    if (request.ReversalDate.Date < note.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede the credit note date.", 400);
                    if (note.AppliedAmount > 0 || note.RefundedAmount > 0)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Reverse all credit applications and refunds before reversing the credit note.", 409);

                    var revGl = _financial.CreateReversingEntry(
                        note.GeneralLedgerHeaderId.Value,
                        $"Reverse customer credit note {note.No}: {request.Reason.Trim()}",
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
                nameof(CustomerCreditNote));
        }

        public IReadOnlyList<CustomerCreditNoteDto> List(int? customerId = null, int? invoiceId = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var query = _db.CustomerCreditNotes.AsNoTracking().Where(c => c.CompanyId == companyId);
            if (customerId.HasValue) query = query.Where(c => c.CustomerId == customerId.Value);
            if (invoiceId.HasValue) query = query.Where(c => c.SourceSalesInvoiceHeaderId == invoiceId.Value);
            var ids = query.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).Select(c => c.Id).ToList();
            return ids.Select(id => BuildDto(companyId, id)).ToList();
        }

        public CustomerCreditNoteDto Get(int id)
        {
            var companyId = _tenant.RequireCompanyId();
            return BuildDto(companyId, id);
        }

        private void ApplyInternal(int companyId, int precision, CustomerCreditNote note, SalesInvoiceHeader invoice, decimal amount, DateTime effectiveDate, string actor)
        {
            if (amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(amount, precision))
                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Application amount must be positive and match currency precision.", 400);
            if (invoice.CustomerId != note.CustomerId)
                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Credit note and invoice must belong to the same customer.", 400);
            if (invoice.Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot apply credit to a draft or void invoice.", 400);
            if (effectiveDate < note.Date.Date || effectiveDate < invoice.Date.Date)
                throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Application date must be on or after both the credit note date and invoice date.", 400);
            if (amount > note.AvailableAmount)
                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application amount ({amount}) exceeds available credit ({note.AvailableAmount}).", 409);

            var invoiceDue = CurrencyPrecisionRules.RoundMoney(invoice.BalanceDue(), precision);
            if (amount > invoiceDue)
                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application amount ({amount}) exceeds invoice balance due ({invoiceDue}).", 409);

            var sourceAccId = note.CustomerCreditLiabilityAccountId
                ?? throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Credit note liability account snapshot is missing.", 422);

            var customer = _db.Customers.AsNoTracking().First(c => c.CompanyId == companyId && c.Id == invoice.CustomerId);
            var destAccId = _mappings.RequireActiveAccount(companyId, invoice.ReceivableAccountId ?? customer.AccountsReceivableAccountId, "Accounts Receivable");
            invoice.ReceivableAccountId ??= destAccId;

            var glHeader = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.SettlementApplication,
                effectiveDate,
                $"Apply Credit {note.No} to Invoice {invoice.No}",
                note.Id);
            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, sourceAccId, amount));
            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, destAccId, amount));
            _financial.SaveGeneralLedgerEntry(glHeader);

            _db.SettlementApplications.Add(new SettlementApplication
            {
                CompanyId = companyId,
                SourceType = SettlementSourceType.CustomerCreditNote,
                CustomerCreditNoteId = note.Id,
                DestinationType = SettlementDestinationType.SalesInvoice,
                SalesInvoiceHeaderId = invoice.Id,
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
            invoice.CreditedAmount = CurrencyPrecisionRules.RoundMoney(invoice.CreditedAmount + amount, precision);
            invoice.Version++;
            invoice.RefreshPaymentStatus();
        }

        private void ValidateInvoiceCreditCap(int companyId, int precision, SalesInvoiceHeader? invoice, decimal newCreditTotal, int? excludeNoteId)
        {
            if (invoice == null)
                return;

            var priorCredited = _db.CustomerCreditNotes
                .Where(c => c.CompanyId == companyId && c.SourceSalesInvoiceHeaderId == invoice.Id && c.Status == SettlementDocumentStatus.Posted && c.Id != excludeNoteId)
                .Sum(c => (decimal?)c.TotalAmount) ?? 0m;

            var maxAllowed = CurrencyPrecisionRules.RoundMoney(invoice.GrandTotal() - invoice.WrittenOffAmount - priorCredited, precision);
            if (newCreditTotal > maxAllowed)
            {
                if (invoice.WrittenOffAmount > 0)
                {
                    throw new SettlementCommandException(
                        SettlementErrorCodes.InvalidState,
                        $"Credit amount ({newCreditTotal}) exceeds remaining creditable balance ({maxAllowed}) because {invoice.WrittenOffAmount} has already been written off. Reverse or reclassify the write-off first.",
                        409);
                }

                throw new SettlementCommandException(
                    SettlementErrorCodes.SourceExhausted,
                    $"Credit amount ({newCreditTotal}) exceeds remaining creditable balance ({maxAllowed}) on invoice {invoice.No}.",
                    409);
            }
        }

        private List<CustomerCreditNoteLine> ComputeLines(
            int companyId,
            int precision,
            SalesInvoiceHeader? invoice,
            IReadOnlyList<CustomerCreditLineInputDto> inputs,
            int? excludeNoteId)
        {
            var taxesById = _db.Taxes.AsNoTracking().Where(t => t.CompanyId == companyId).ToDictionary(t => t.Id);
            var invoiceLinesById = invoice?.SalesInvoiceLines.ToDictionary(l => l.Id) ?? new Dictionary<int, Core.Domain.Sales.SalesInvoiceLine>();

            var result = new List<CustomerCreditNoteLine>(inputs.Count);
            foreach (var input in inputs)
            {
                if (input.Quantity <= 0 || input.UnitPrice < 0 || input.DiscountAmount < 0)
                    throw new SettlementCommandException(SettlementErrorCodes.Validation, "Credit line quantity must be positive and price/discount non-negative.", 400);

                Core.Domain.Sales.SalesInvoiceLine? srcLine = null;
                if (input.SourceSalesInvoiceLineId.HasValue)
                {
                    if (!invoiceLinesById.TryGetValue(input.SourceSalesInvoiceLineId.Value, out srcLine))
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Source invoice line does not belong to the source invoice.", 400);
                }

                var subtotal = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, input.Quantity * input.UnitPrice - input.DiscountAmount), precision);
                var taxId = input.TaxId ?? srcLine?.TaxId;
                decimal taxRate = 0m;
                int? taxAccountId = null;
                if (taxId.HasValue && taxesById.TryGetValue(taxId.Value, out var tax) && tax != null)
                {
                    taxRate = tax.Rate;
                    taxAccountId = tax.SalesAccountId;
                }

                decimal taxAmount;
                if (input.OverrideTaxAmount.HasValue)
                {
                    taxAmount = CurrencyPrecisionRules.RoundMoney(input.OverrideTaxAmount.Value, precision);
                }
                else if (srcLine != null && srcLine.Subtotal > 0 && srcLine.TaxAmount > 0)
                {
                    // Exact residual tax when consuming the full remaining line balance, otherwise proportional to original tax snapshot
                    var priorLineSubtotal = _db.CustomerCreditNoteLines
                        .Where(l => l.CompanyId == companyId && l.SourceSalesInvoiceLineId == srcLine.Id && l.CustomerCreditNote!.Status == SettlementDocumentStatus.Posted && l.CustomerCreditNoteId != excludeNoteId)
                        .Sum(l => (decimal?)l.Subtotal) ?? 0m;
                    var priorLineTax = _db.CustomerCreditNoteLines
                        .Where(l => l.CompanyId == companyId && l.SourceSalesInvoiceLineId == srcLine.Id && l.CustomerCreditNote!.Status == SettlementDocumentStatus.Posted && l.CustomerCreditNoteId != excludeNoteId)
                        .Sum(l => (decimal?)l.TaxAmount) ?? 0m;

                    if (priorLineSubtotal + subtotal >= srcLine.Subtotal)
                        taxAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, srcLine.TaxAmount - priorLineTax), precision);
                    else
                        taxAmount = CurrencyPrecisionRules.RoundMoney(srcLine.TaxAmount * (subtotal / srcLine.Subtotal), precision);
                }
                else
                {
                    taxAmount = taxRate > 0 ? CurrencyPrecisionRules.RoundMoney(subtotal * taxRate / 100m, precision) : 0m;
                }

                if (taxAmount > 0 && !taxAccountId.HasValue)
                    throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Tax sales account is not configured for the selected tax.", 422);

                result.Add(new CustomerCreditNoteLine
                {
                    SourceSalesInvoiceLineId = srcLine?.Id,
                    ItemId = input.ItemId ?? srcLine?.ItemId,
                    TaxId = taxId,
                    TaxAccountId = taxAccountId,
                    TaxRateSnapshot = taxRate,
                    TaxInclusiveSnapshot = false,
                    IsShippingLine = input.IsShippingLine,
                    Description = input.Description?.Trim() ?? srcLine?.DisplayDescription ?? "Credit adjustment",
                    Quantity = input.Quantity,
                    UnitPrice = input.UnitPrice,
                    DiscountAmount = input.DiscountAmount,
                    Subtotal = subtotal,
                    TaxAmount = taxAmount,
                    TotalAmount = CurrencyPrecisionRules.RoundMoney(subtotal + taxAmount, precision)
                });
            }

            return result;
        }

        private CustomerCreditNoteDto BuildDto(int companyId, int id)
        {
            var note = _db.CustomerCreditNotes.AsNoTracking()
                .Include(c => c.Customer).ThenInclude(c => c!.Party)
                .Include(c => c.SourceSalesInvoiceHeader)
                .Include(c => c.Lines)
                .FirstOrDefault(c => c.CompanyId == companyId && c.Id == id)
                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);

            var apps = _db.SettlementApplications.AsNoTracking()
                .Include(a => a.SalesInvoiceHeader)
                .Include(a => a.Reversal)
                .Where(a => a.CompanyId == companyId && a.SourceType == SettlementSourceType.CustomerCreditNote && a.CustomerCreditNoteId == note.Id)
                .OrderBy(a => a.EffectiveDate).ThenBy(a => a.Id)
                .Select(a => new SettlementApplicationSummaryDto(
                    a.Id, note.Id, "CustomerCreditNote", a.SalesInvoiceHeaderId!.Value,
                    a.SalesInvoiceHeader != null ? a.SalesInvoiceHeader.No! : "",
                    a.Amount, a.EffectiveDate, a.GeneralLedgerHeaderId, a.IsReversed,
                    a.Reversal != null ? a.Reversal.ReversalDate : null,
                    a.Reversal != null ? a.Reversal.Reason : null))
                .ToList();

            var settlementState = note.Status != SettlementDocumentStatus.Posted
                ? note.Status.ToString()
                : note.AvailableAmount <= 0 ? "Used" : (note.AppliedAmount > 0 || note.RefundedAmount > 0 ? "PartlyUsed" : "Available");

            return new CustomerCreditNoteDto(
                note.Id, note.No, note.CustomerId, note.Customer?.Party?.Name ?? "",
                note.SourceSalesInvoiceHeaderId, note.SourceSalesInvoiceHeader?.No,
                note.Date, note.CurrencyCode, note.CurrencyPrecision, (int)note.Kind,
                note.ReturnGoodsWithoutRestock, note.Reason, note.SourceReference, note.EvidenceReference,
                note.Subtotal, note.TaxAmount, note.TotalAmount, note.AppliedAmount, note.RefundedAmount, note.AvailableAmount,
                settlementState, (int)note.Status, note.Status.ToString(),
                note.GeneralLedgerHeaderId, note.VoidGeneralLedgerHeaderId,
                note.SubmittedBy, note.ReviewedBy, note.PostedBy, note.Version,
                note.Lines.Select(l => new CustomerCreditLineDto(
                    l.Id, l.SourceSalesInvoiceLineId, l.ItemId, l.TaxId, l.TaxRateSnapshot, l.IsShippingLine,
                    l.Description, l.Quantity, l.UnitPrice, l.DiscountAmount, l.Subtotal, l.TaxAmount, l.TotalAmount)).ToList(),
                apps);
        }

        private static string GenerateNumber(int companyId, string prefix, IEnumerable<string> existing)
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
