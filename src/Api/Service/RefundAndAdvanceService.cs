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
using Dto.Financial;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed class RefundAndAdvanceService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly SettlementCommandCoordinator _coordinator;
        private readonly SettlementAccountMappingService _mappings;

        public RefundAndAdvanceService(
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

        // --- Supplier Advances ---

        public SupplierAdvanceDto SaveAdvanceDraft(SaveSupplierAdvanceDraftRequest request)
        {
            return _coordinator.Execute(
                "supplier_advance.save_draft",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.Amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(request.Amount, precision))
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Advance amount must be positive and match currency precision.", 400);
                    if (string.IsNullOrWhiteSpace(request.Purpose))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "Advance purpose is required.", 400);

                    var vendor = _db.Vendors.FirstOrDefault(v => v.CompanyId == companyId && v.Id == request.VendorId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Vendor not found.", 404);
                    var bank = _db.Banks.FirstOrDefault(b => b.CompanyId == companyId && b.Id == request.BankId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Bank account not found.", 404);
                    var bankAccountId = _mappings.RequireActiveAccount(companyId, bank.AccountId, "Bank GL Account");

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var advAccountId = _mappings.RequireActiveAccount(companyId, glSetting?.SupplierAdvanceAccountId, "Supplier Advances");

                    SupplierAdvance adv;
                    if (request.Id.HasValue)
                    {
                        adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == request.Id.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                        if (adv.Status != SettlementDocumentStatus.Draft)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft advances can be edited.", 409);
                        if (request.ExpectedVersion.HasValue && adv.Version != request.ExpectedVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier advance changed concurrently.", 409);
                    }
                    else
                    {
                        adv = new SupplierAdvance { CompanyId = companyId, Status = SettlementDocumentStatus.Draft };
                        _db.SupplierAdvances.Add(adv);
                    }

                    adv.VendorId = vendor.Id;
                    adv.PurchaseOrderHeaderId = request.PurchaseOrderHeaderId;
                    adv.Date = request.Date.Date;
                    adv.CurrencyCode = setting.Currency;
                    adv.CurrencyPrecision = precision;
                    adv.Amount = request.Amount;
                    adv.BankId = bank.Id;
                    adv.BankAccountId = bankAccountId;
                    adv.SupplierAdvanceAccountId = advAccountId;
                    adv.Purpose = request.Purpose.Trim();
                    adv.IsRefundable = request.IsRefundable;
                    adv.ReferenceNo = request.ReferenceNo?.Trim();
                    adv.EvidenceReference = request.EvidenceReference?.Trim();
                    adv.Version++;

                    _db.SaveChanges();
                    return BuildAdvanceDto(companyId, adv.Id);
                },
                dto => dto.Id,
                nameof(SupplierAdvance));
        }

        public SupplierAdvanceDto SubmitAdvance(int id, string? idempotencyKey = null)
        {
            return _coordinator.Execute(
                "supplier_advance.submit",
                idempotencyKey,
                new { id },
                (companyId, _) =>
                {
                    var adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                    if (adv.Status != SettlementDocumentStatus.Draft)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft advances can be submitted.", 409);

                    adv.Status = SettlementDocumentStatus.PendingReview;
                    adv.SubmittedBy = _tenant.UserId ?? "system";
                    adv.SubmittedAtUtc = DateTime.UtcNow;
                    adv.Version++;
                    _db.SaveChanges();
                    return BuildAdvanceDto(companyId, adv.Id);
                },
                dto => dto.Id,
                nameof(SupplierAdvance));
        }

        public SupplierAdvanceDto PostAdvance(int id, PostPartyRefundRequest request)
        {
            return _coordinator.Execute(
                "supplier_advance.post",
                request.IdempotencyKey,
                new { id, request },
                (companyId, _) =>
                {
                    var adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                    if (adv.Status is not (SettlementDocumentStatus.Draft or SettlementDocumentStatus.PendingReview))
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft or pending-review advances can be posted.", 409);
                    if (request.ExpectedVersion.HasValue && adv.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier advance changed concurrently.", 409);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureReviewPolicyForPosting(companyId, adv.SubmittedBy, actor, requiresTeamReview: adv.Status == SettlementDocumentStatus.PendingReview);

                    var bankAccId = _mappings.RequireActiveAccount(companyId, adv.BankAccountId, "Bank GL Account");
                    var advAccId = _mappings.RequireActiveAccount(companyId, adv.SupplierAdvanceAccountId, "Supplier Advances");
                    adv.No ??= GenerateNumber("SADV", _db.SupplierAdvances.Where(a => a.CompanyId == companyId && a.No != null).Select(a => a.No!));

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.SupplierAdvance,
                        adv.Date,
                        $"Supplier Advance {adv.No}",
                        adv.Id);
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, advAccId, adv.Amount));
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, bankAccId, adv.Amount));
                    _financial.SaveGeneralLedgerEntry(glHeader);

                    adv.GeneralLedgerHeaderId = glHeader.Id;
                    adv.Status = SettlementDocumentStatus.Posted;
                    adv.PostedBy = actor;
                    adv.PostedAtUtc = DateTime.UtcNow;
                    if (adv.SubmittedBy == null) { adv.SubmittedBy = actor; adv.SubmittedAtUtc = adv.PostedAtUtc; }
                    else { adv.ReviewedBy = actor; adv.ReviewedAtUtc = adv.PostedAtUtc; }
                    adv.Version++;

                    _db.SaveChanges();
                    return BuildAdvanceDto(companyId, adv.Id);
                },
                dto => dto.Id,
                nameof(SupplierAdvance));
        }

        public SupplierAdvanceDto ApplyAdvance(int id, ApplySupplierAdvanceRequest request)
        {
            return _coordinator.Execute(
                "supplier_advance.apply",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                    if (adv.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted advances can be applied.", 409);
                    if (request.ExpectedAdvanceVersion.HasValue && adv.Version != request.ExpectedAdvanceVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Supplier advance changed concurrently.", 409);
                    if (request.Applications == null || request.Applications.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Select at least one bill to apply.", 400);

                    var actor = _tenant.UserId ?? "system";
                    var effectiveDate = request.EffectiveDate.Date;

                    foreach (var item in request.Applications)
                    {
                        if (item.Amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(item.Amount, precision))
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Application amount must be positive.", 400);

                        var bill = _db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Include(b => b.VendorPayments)
                            .FirstOrDefault(b => b.CompanyId == companyId && b.Id == item.PurchaseInvoiceHeaderId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, $"Bill {item.PurchaseInvoiceHeaderId} not found.", 404);

                        if (bill.VendorId != adv.VendorId)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Advance and bill must belong to the same vendor.", 400);
                        if (bill.IsDraft || bill.IsVoid)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot apply advance to a draft or void bill.", 400);
                        if (effectiveDate < adv.Date.Date || effectiveDate < bill.Date.Date)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Application date must be on or after both advance date and bill date.", 400);
                        if (item.ExpectedBillVersion.HasValue && bill.Version != item.ExpectedBillVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Bill changed concurrently.", 409);
                        if (item.Amount > adv.AvailableAmount)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application ({item.Amount}) exceeds available advance ({adv.AvailableAmount}).", 409);

                        var billDue = CurrencyPrecisionRules.RoundMoney(bill.BalanceDue(), precision);
                        if (item.Amount > billDue)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Application ({item.Amount}) exceeds bill balance due ({billDue}).", 409);

                        var vendor = _db.Vendors.AsNoTracking().First(v => v.CompanyId == companyId && v.Id == bill.VendorId!.Value);
                        var apAccountId = _mappings.RequireActiveAccount(companyId, bill.PayableAccountId ?? vendor.AccountsPayableAccountId, "Accounts Payable");
                        bill.PayableAccountId ??= apAccountId;

                        var glHeader = _financial.CreateGeneralLedgerHeader(
                            DocumentTypes.SettlementApplication,
                            effectiveDate,
                            $"Apply Advance {adv.No} to Bill {bill.No ?? bill.VendorInvoiceNo}",
                            adv.Id);
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, apAccountId, item.Amount));
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, adv.SupplierAdvanceAccountId, item.Amount));
                        _financial.SaveGeneralLedgerEntry(glHeader);

                        _db.SettlementApplications.Add(new SettlementApplication
                        {
                            CompanyId = companyId,
                            SourceType = SettlementSourceType.SupplierAdvance,
                            SupplierAdvanceId = adv.Id,
                            DestinationType = SettlementDestinationType.PurchaseInvoice,
                            PurchaseInvoiceHeaderId = bill.Id,
                            CurrencyCode = adv.CurrencyCode,
                            Amount = item.Amount,
                            EffectiveDate = effectiveDate,
                            SourceAccountId = adv.SupplierAdvanceAccountId,
                            DestinationAccountId = apAccountId,
                            GeneralLedgerHeaderId = glHeader.Id,
                            CreatedBy = actor,
                            CreatedAtUtc = DateTime.UtcNow
                        });

                        adv.AppliedAmount = CurrencyPrecisionRules.RoundMoney(adv.AppliedAmount + item.Amount, precision);
                        adv.Version++;
                        bill.AdvanceAppliedAmount = CurrencyPrecisionRules.RoundMoney(bill.AdvanceAppliedAmount + item.Amount, precision);
                        bill.Version++;
                    }

                    _db.SaveChanges();
                    return BuildAdvanceDto(companyId, adv.Id);
                },
                dto => dto.Id,
                nameof(SupplierAdvance));
        }

        public SupplierAdvanceDto ReverseAdvance(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "supplier_advance.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, _) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);
                    var adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                    if (adv.Status != SettlementDocumentStatus.Posted || !adv.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted advances can be reversed.", 409);
                    if (request.ReversalDate.Date < adv.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede advance date.", 400);
                    if (adv.AppliedAmount > 0 || adv.RefundedAmount > 0)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Reverse all applications and refunds before reversing the advance.", 409);

                    var revGl = _financial.CreateReversingEntry(
                        adv.GeneralLedgerHeaderId.Value,
                        $"Reverse supplier advance {adv.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    adv.Status = SettlementDocumentStatus.Reversed;
                    adv.VoidGeneralLedgerHeaderId = revGl.Id;
                    adv.ReversedBy = _tenant.UserId ?? "system";
                    adv.ReversedAtUtc = DateTime.UtcNow;
                    adv.ReversalReason = request.Reason.Trim();
                    adv.Version++;

                    _db.SaveChanges();
                    return BuildAdvanceDto(companyId, adv.Id);
                },
                dto => dto.Id,
                nameof(SupplierAdvance));
        }

        // --- Party Refunds ---

        public PartyRefundDto SaveRefundDraft(SavePartyRefundDraftRequest request)
        {
            return _coordinator.Execute(
                "party_refund.save_draft",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var direction = (PartyRefundDirection)request.Direction;
                    if (request.Allocations == null || request.Allocations.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "At least one refund allocation is required.", 400);

                    var bank = _db.Banks.FirstOrDefault(b => b.CompanyId == companyId && b.Id == request.BankId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Bank account not found.", 404);
                    var bankAccountId = _mappings.RequireActiveAccount(companyId, bank.AccountId, "Bank GL Account");

                    PartyRefund refund;
                    if (request.Id.HasValue)
                    {
                        refund = _db.PartyRefunds.Include(r => r.Allocations)
                            .FirstOrDefault(r => r.CompanyId == companyId && r.Id == request.Id.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Refund not found.", 404);
                        if (refund.Status != SettlementDocumentStatus.Draft)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft refunds can be edited.", 409);
                        if (request.ExpectedVersion.HasValue && refund.Version != request.ExpectedVersion.Value)
                            throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Refund changed concurrently.", 409);
                        _db.PartyRefundAllocations.RemoveRange(refund.Allocations);
                        refund.Allocations.Clear();
                    }
                    else
                    {
                        refund = new PartyRefund { CompanyId = companyId, Status = SettlementDocumentStatus.Draft };
                        _db.PartyRefunds.Add(refund);
                    }

                    decimal total = 0m;
                    foreach (var alloc in request.Allocations)
                    {
                        if (alloc.Amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(alloc.Amount, precision))
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Allocation amount must be positive.", 400);

                        var srcType = (SettlementSourceType)alloc.SourceType;
                        int sourceAccountId;
                        if (direction == PartyRefundDirection.CustomerPayout)
                        {
                            if (srcType != SettlementSourceType.CustomerCreditNote)
                                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Customer refunds can only allocate from customer credit notes.", 400);
                            var cn = _db.CustomerCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == alloc.SourceId)
                                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer credit note not found.", 404);
                            if (request.CustomerId.HasValue && cn.CustomerId != request.CustomerId.Value)
                                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Customer credit note belongs to another customer.", 400);
                            if (cn.Status != SettlementDocumentStatus.Posted)
                                throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Customer credit note must be posted.", 409);
                            if (request.Date.Date < cn.Date.Date)
                                throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Refund date cannot precede credit note date.", 400);
                            if (alloc.Amount > cn.AvailableAmount)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Refund exceeds available customer credit.", 409);
                            sourceAccountId = cn.CustomerCreditLiabilityAccountId!.Value;
                            refund.Allocations.Add(new PartyRefundAllocation
                            {
                                CompanyId = companyId,
                                SourceType = srcType,
                                CustomerCreditNoteId = cn.Id,
                                SourceAccountId = sourceAccountId,
                                Amount = alloc.Amount
                            });
                        }
                        else
                        {
                            if (srcType == SettlementSourceType.SupplierCreditNote)
                            {
                                var sc = _db.SupplierCreditNotes.FirstOrDefault(c => c.CompanyId == companyId && c.Id == alloc.SourceId)
                                    ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier credit note not found.", 404);
                                if (request.VendorId.HasValue && sc.VendorId != request.VendorId.Value)
                                    throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier credit belongs to another vendor.", 400);
                                if (sc.Status != SettlementDocumentStatus.Posted)
                                    throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Supplier credit must be posted.", 409);
                                if (request.Date.Date < sc.EffectiveDate.Date)
                                    throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Refund date cannot precede supplier credit effective date.", 400);
                                if (alloc.Amount > sc.AvailableAmount)
                                    throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Refund exceeds available supplier credit.", 409);
                                sourceAccountId = sc.SupplierCreditAssetAccountId!.Value;
                                refund.Allocations.Add(new PartyRefundAllocation
                                {
                                    CompanyId = companyId,
                                    SourceType = srcType,
                                    SupplierCreditNoteId = sc.Id,
                                    SourceAccountId = sourceAccountId,
                                    Amount = alloc.Amount
                                });
                            }
                            else if (srcType == SettlementSourceType.SupplierAdvance)
                            {
                                var adv = _db.SupplierAdvances.FirstOrDefault(a => a.CompanyId == companyId && a.Id == alloc.SourceId)
                                    ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);
                                if (request.VendorId.HasValue && adv.VendorId != request.VendorId.Value)
                                    throw new SettlementCommandException(SettlementErrorCodes.Validation, "Supplier advance belongs to another vendor.", 400);
                                if (adv.Status != SettlementDocumentStatus.Posted)
                                    throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Supplier advance must be posted.", 409);
                                if (!adv.IsRefundable)
                                    throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Selected supplier advance is marked non-refundable.", 400);
                                if (request.Date.Date < adv.Date.Date)
                                    throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Refund date cannot precede advance date.", 400);
                                if (alloc.Amount > adv.AvailableAmount)
                                    throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Refund exceeds available supplier advance.", 409);
                                sourceAccountId = adv.SupplierAdvanceAccountId;
                                refund.Allocations.Add(new PartyRefundAllocation
                                {
                                    CompanyId = companyId,
                                    SourceType = srcType,
                                    SupplierAdvanceId = adv.Id,
                                    SourceAccountId = sourceAccountId,
                                    Amount = alloc.Amount
                                });
                            }
                            else
                            {
                                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Invalid source type for supplier refund.", 400);
                            }
                        }

                        total = CurrencyPrecisionRules.RoundMoney(total + alloc.Amount, precision);
                    }

                    refund.Direction = direction;
                    refund.CustomerId = direction == PartyRefundDirection.CustomerPayout ? request.CustomerId : null;
                    refund.VendorId = direction == PartyRefundDirection.SupplierInflow ? request.VendorId : null;
                    refund.Date = request.Date.Date;
                    refund.CurrencyCode = setting.Currency;
                    refund.CurrencyPrecision = precision;
                    refund.Amount = total;
                    refund.BankId = bank.Id;
                    refund.BankAccountId = bankAccountId;
                    refund.PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "Bank Transfer" : request.PaymentMethod.Trim();
                    refund.ExternalReference = request.ExternalReference?.Trim();
                    refund.EvidenceReference = request.EvidenceReference?.Trim();
                    refund.Version++;

                    _db.SaveChanges();
                    return BuildRefundDto(companyId, refund.Id);
                },
                dto => dto.Id,
                nameof(PartyRefund));
        }

        public PartyRefundDto SubmitRefund(int id, string? idempotencyKey = null)
        {
            return _coordinator.Execute(
                "party_refund.submit",
                idempotencyKey,
                new { id },
                (companyId, _) =>
                {
                    var refund = _db.PartyRefunds.FirstOrDefault(r => r.CompanyId == companyId && r.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Refund not found.", 404);
                    if (refund.Status != SettlementDocumentStatus.Draft)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft refunds can be submitted.", 409);

                    refund.Status = SettlementDocumentStatus.PendingReview;
                    refund.SubmittedBy = _tenant.UserId ?? "system";
                    refund.SubmittedAtUtc = DateTime.UtcNow;
                    refund.Version++;
                    _db.SaveChanges();
                    return BuildRefundDto(companyId, refund.Id);
                },
                dto => dto.Id,
                nameof(PartyRefund));
        }

        public PartyRefundDto PostRefund(int id, PostPartyRefundRequest request)
        {
            return _coordinator.Execute(
                "party_refund.post",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var refund = _db.PartyRefunds.Include(r => r.Allocations)
                        .FirstOrDefault(r => r.CompanyId == companyId && r.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Refund not found.", 404);

                    if (refund.Status is not (SettlementDocumentStatus.Draft or SettlementDocumentStatus.PendingReview))
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft or pending-review refunds can be posted.", 409);
                    if (request.ExpectedVersion.HasValue && refund.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Refund changed concurrently.", 409);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureReviewPolicyForPosting(companyId, refund.SubmittedBy, actor, requiresTeamReview: refund.Status == SettlementDocumentStatus.PendingReview);

                    var bankAccId = _mappings.RequireActiveAccount(companyId, refund.BankAccountId, "Bank GL Account");
                    refund.No ??= GenerateNumber("RFND", _db.PartyRefunds.Where(r => r.CompanyId == companyId && r.No != null).Select(r => r.No!));

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.PartyRefund,
                        refund.Date,
                        $"Party Refund {refund.No}",
                        refund.Id);

                    if (refund.Direction == PartyRefundDirection.CustomerPayout)
                    {
                        foreach (var alloc in refund.Allocations)
                        {
                            var cn = _db.CustomerCreditNotes.First(c => c.CompanyId == companyId && c.Id == alloc.CustomerCreditNoteId!.Value);
                            if (alloc.Amount > cn.AvailableAmount)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Credit note {cn.No} has insufficient available balance ({cn.AvailableAmount}).", 409);
                            cn.RefundedAmount = CurrencyPrecisionRules.RoundMoney(cn.RefundedAmount + alloc.Amount, precision);
                            cn.Version++;
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, alloc.SourceAccountId, alloc.Amount));
                        }
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, bankAccId, refund.Amount));
                    }
                    else
                    {
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, bankAccId, refund.Amount));
                        foreach (var alloc in refund.Allocations)
                        {
                            if (alloc.SourceType == SettlementSourceType.SupplierCreditNote)
                            {
                                var sc = _db.SupplierCreditNotes.First(c => c.CompanyId == companyId && c.Id == alloc.SupplierCreditNoteId!.Value);
                                if (alloc.Amount > sc.AvailableAmount)
                                    throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Supplier credit {sc.No} has insufficient available balance ({sc.AvailableAmount}).", 409);
                                sc.RefundedAmount = CurrencyPrecisionRules.RoundMoney(sc.RefundedAmount + alloc.Amount, precision);
                                sc.Version++;
                            }
                            else if (alloc.SourceType == SettlementSourceType.SupplierAdvance)
                            {
                                var adv = _db.SupplierAdvances.First(a => a.CompanyId == companyId && a.Id == alloc.SupplierAdvanceId!.Value);
                                if (alloc.Amount > adv.AvailableAmount)
                                    throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Supplier advance {adv.No} has insufficient available balance ({adv.AvailableAmount}).", 409);
                                adv.RefundedAmount = CurrencyPrecisionRules.RoundMoney(adv.RefundedAmount + alloc.Amount, precision);
                                adv.Version++;
                            }
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, alloc.SourceAccountId, alloc.Amount));
                        }
                    }

                    _financial.SaveGeneralLedgerEntry(glHeader);

                    refund.GeneralLedgerHeaderId = glHeader.Id;
                    refund.Status = SettlementDocumentStatus.Posted;
                    refund.PostedBy = actor;
                    refund.PostedAtUtc = DateTime.UtcNow;
                    if (refund.SubmittedBy == null) { refund.SubmittedBy = actor; refund.SubmittedAtUtc = refund.PostedAtUtc; }
                    else { refund.ReviewedBy = actor; refund.ReviewedAtUtc = refund.PostedAtUtc; }
                    refund.Version++;

                    _db.SaveChanges();
                    return BuildRefundDto(companyId, refund.Id);
                },
                dto => dto.Id,
                nameof(PartyRefund));
        }

        public PartyRefundDto ReverseRefund(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "party_refund.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var refund = _db.PartyRefunds.Include(r => r.Allocations)
                        .FirstOrDefault(r => r.CompanyId == companyId && r.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Refund not found.", 404);
                    if (refund.Status != SettlementDocumentStatus.Posted || !refund.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted refunds can be reversed.", 409);
                    if (request.ReversalDate.Date < refund.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede refund date.", 400);

                    var revGl = _financial.CreateReversingEntry(
                        refund.GeneralLedgerHeaderId.Value,
                        $"Reverse refund {refund.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    foreach (var alloc in refund.Allocations)
                    {
                        if (alloc.CustomerCreditNoteId.HasValue)
                        {
                            var cn = _db.CustomerCreditNotes.First(c => c.CompanyId == companyId && c.Id == alloc.CustomerCreditNoteId.Value);
                            cn.RefundedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, cn.RefundedAmount - alloc.Amount), precision);
                            cn.Version++;
                        }
                        else if (alloc.SupplierCreditNoteId.HasValue)
                        {
                            var sc = _db.SupplierCreditNotes.First(c => c.CompanyId == companyId && c.Id == alloc.SupplierCreditNoteId.Value);
                            sc.RefundedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, sc.RefundedAmount - alloc.Amount), precision);
                            sc.Version++;
                        }
                        else if (alloc.SupplierAdvanceId.HasValue)
                        {
                            var adv = _db.SupplierAdvances.First(a => a.CompanyId == companyId && a.Id == alloc.SupplierAdvanceId.Value);
                            adv.RefundedAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, adv.RefundedAmount - alloc.Amount), precision);
                            adv.Version++;
                        }
                    }

                    refund.Status = SettlementDocumentStatus.Reversed;
                    refund.VoidGeneralLedgerHeaderId = revGl.Id;
                    refund.ReversedBy = _tenant.UserId ?? "system";
                    refund.ReversedAtUtc = DateTime.UtcNow;
                    refund.ReversalReason = request.Reason.Trim();
                    refund.Version++;

                    _db.SaveChanges();
                    return BuildRefundDto(companyId, refund.Id);
                },
                dto => dto.Id,
                nameof(PartyRefund));
        }

        private SupplierAdvanceDto BuildAdvanceDto(int companyId, int id)
        {
            var adv = _db.SupplierAdvances.AsNoTracking()
                .Include(a => a.Vendor).ThenInclude(v => v!.Party)
                .Include(a => a.Bank)
                .FirstOrDefault(a => a.CompanyId == companyId && a.Id == id)
                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier advance not found.", 404);

            var apps = _db.SettlementApplications.AsNoTracking()
                .Include(a => a.PurchaseInvoiceHeader)
                .Include(a => a.Reversal)
                .Where(a => a.CompanyId == companyId && a.SourceType == SettlementSourceType.SupplierAdvance && a.SupplierAdvanceId == adv.Id)
                .OrderBy(a => a.EffectiveDate).ThenBy(a => a.Id)
                .Select(a => new SettlementApplicationSummaryDto(
                    a.Id, adv.Id, "SupplierAdvance", a.PurchaseInvoiceHeaderId!.Value,
                    a.PurchaseInvoiceHeader != null ? (a.PurchaseInvoiceHeader.No ?? a.PurchaseInvoiceHeader.VendorInvoiceNo) : "",
                    a.Amount, a.EffectiveDate, a.GeneralLedgerHeaderId, a.IsReversed,
                    a.Reversal != null ? a.Reversal.ReversalDate : null,
                    a.Reversal != null ? a.Reversal.Reason : null))
                .ToList();

            return new SupplierAdvanceDto(
                adv.Id, adv.No, adv.VendorId, adv.Vendor?.Party?.Name ?? "", adv.PurchaseOrderHeaderId,
                adv.Date, adv.CurrencyCode, adv.CurrencyPrecision, adv.Amount, adv.AppliedAmount, adv.RefundedAmount, adv.AvailableAmount,
                adv.BankId, adv.Bank?.Name ?? "", adv.Purpose, adv.IsRefundable, adv.ReferenceNo, adv.EvidenceReference,
                (int)adv.Status, adv.Status.ToString(), adv.GeneralLedgerHeaderId, adv.VoidGeneralLedgerHeaderId,
                adv.SubmittedBy, adv.ReviewedBy, adv.PostedBy, adv.Version, apps);
        }

        private PartyRefundDto BuildRefundDto(int companyId, int id)
        {
            var r = _db.PartyRefunds.AsNoTracking()
                .Include(x => x.Customer).ThenInclude(c => c!.Party)
                .Include(x => x.Vendor).ThenInclude(v => v!.Party)
                .Include(x => x.Bank)
                .Include(x => x.Allocations).ThenInclude(a => a.CustomerCreditNote)
                .Include(x => x.Allocations).ThenInclude(a => a.SupplierCreditNote)
                .Include(x => x.Allocations).ThenInclude(a => a.SupplierAdvance)
                .FirstOrDefault(x => x.CompanyId == companyId && x.Id == id)
                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Refund not found.", 404);

            var partyName = r.Direction == PartyRefundDirection.CustomerPayout
                ? (r.Customer?.Party?.Name ?? "")
                : (r.Vendor?.Party?.Name ?? "");

            var allocDtos = r.Allocations.Select(a =>
            {
                var srcId = a.CustomerCreditNoteId ?? a.SupplierCreditNoteId ?? a.SupplierAdvanceId ?? 0;
                var srcNo = a.CustomerCreditNote?.No ?? a.SupplierCreditNote?.No ?? a.SupplierAdvance?.No ?? "";
                return new PartyRefundAllocationDto(a.Id, (int)a.SourceType, a.SourceType.ToString(), srcId, srcNo, a.Amount);
            }).ToList();

            return new PartyRefundDto(
                r.Id, r.No, (int)r.Direction, r.Direction.ToString(), r.CustomerId, r.VendorId, partyName,
                r.Date, r.CurrencyCode, r.CurrencyPrecision, r.Amount, r.BankId, r.Bank?.Name ?? "",
                r.PaymentMethod, r.ExternalReference, r.EvidenceReference, (int)r.Status, r.Status.ToString(),
                r.GeneralLedgerHeaderId, r.VoidGeneralLedgerHeaderId, r.SubmittedBy, r.ReviewedBy, r.PostedBy,
                r.Version, allocDtos);
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
