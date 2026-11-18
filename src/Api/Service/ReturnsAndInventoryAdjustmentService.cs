// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Inventory;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;
using Services.Inventory;

namespace Api.Service
{
    public sealed class ReturnsAndInventoryAdjustmentService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly IStockService _stock;
        private readonly SettlementCommandCoordinator _coordinator;
        private readonly SettlementAccountMappingService _mappings;

        public ReturnsAndInventoryAdjustmentService(
            ApiDbContext db,
            ITenantContext tenant,
            Services.Financial.IFinancialService financial,
            IStockService stock,
            SettlementCommandCoordinator coordinator,
            SettlementAccountMappingService mappings)
        {
            _db = db;
            _tenant = tenant;
            _financial = financial;
            _stock = stock;
            _coordinator = coordinator;
            _mappings = mappings;
        }

        public CustomerReturnReceiptDto PostCustomerReturn(PostCustomerReturnRequest request)
        {
            return _coordinator.Execute(
                "customer_return.post",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.Lines == null || request.Lines.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "At least one return line is required.", 400);

                    var invoice = _db.SalesInvoiceHeaders
                        .Include(i => i.SalesInvoiceLines)
                        .FirstOrDefault(i => i.CompanyId == companyId && i.Id == request.SalesInvoiceHeaderId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source sales invoice not found.", 404);
                    if (invoice.Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot record return for a draft or void invoice.", 400);
                    if (request.ReceiptDate.Date < invoice.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Return receipt date cannot precede invoice date.", 400);

                    var no = GenerateNumber("CRTN", _db.CustomerReturnReceipts.Where(r => r.CompanyId == companyId && r.No != null).Select(r => r.No!));
                    var receipt = new CustomerReturnReceipt
                    {
                        CompanyId = companyId,
                        CustomerId = invoice.CustomerId,
                        SalesInvoiceHeaderId = invoice.Id,
                        CustomerCreditNoteId = request.CustomerCreditNoteId,
                        No = no,
                        ReceiptDate = request.ReceiptDate.Date,
                        Notes = request.Notes?.Trim(),
                        Status = SettlementDocumentStatus.Posted,
                        PostedBy = _tenant.UserId ?? "system",
                        PostedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    _db.CustomerReturnReceipts.Add(receipt);
                    _db.SaveChanges();

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.CustomerReturnReceipt,
                        receipt.ReceiptDate,
                        $"Customer Return {no} (Invoice {invoice.No})",
                        receipt.Id);

                    decimal totalRestoredGl = 0m;
                    foreach (var lineInput in request.Lines)
                    {
                        if (lineInput.Quantity <= 0)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Return quantity must be positive.", 400);

                        var invLine = invoice.SalesInvoiceLines.FirstOrDefault(l => l.Id == lineInput.SalesInvoiceLineId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.Validation, "Invoice line does not belong to source invoice.", 400);

                        var priorLines = _db.CustomerReturnReceiptLines
                            .Where(l => l.CompanyId == companyId && l.SalesInvoiceLineId == invLine.Id && l.CustomerReturnReceipt!.Status == SettlementDocumentStatus.Posted)
                            .Select(l => new { l.Quantity, l.TotalRestoredCost })
                            .ToList();
                        var priorReturnedQty = priorLines.Sum(l => l.Quantity);
                        var priorRestoredCost = priorLines.Sum(l => l.TotalRestoredCost);
                        if (priorReturnedQty + lineInput.Quantity > invLine.Quantity)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Return quantity exceeds remaining delivered quantity.", 409);

                        var item = _db.Items.Include(i => i.InventoryControlJournals)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == invLine.ItemId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Item not found.", 404);

                        EnsureNotBackdatedBeforeLatestMovement(item, receipt.ReceiptDate);

                        decimal originalUnitCost = 0m;
                        decimal? remainingOrigCost = null;
                        if (invLine.InventoryControlJournalId.HasValue)
                        {
                            var origMove = _db.InventoryControlJournals.AsNoTracking()
                                .FirstOrDefault(j => j.CompanyId == companyId && j.Id == invLine.InventoryControlJournalId.Value);
                            if (origMove != null && (origMove.OUTQty ?? 0m) > 0)
                            {
                                var origTotalCost = origMove.TotalCost ?? 0m;
                                originalUnitCost = Math.Round(origTotalCost / origMove.OUTQty!.Value, 4, MidpointRounding.AwayFromZero);
                                remainingOrigCost = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, origTotalCost - priorRestoredCost), precision);
                            }
                        }
                        if (originalUnitCost <= 0m)
                            originalUnitCost = item.Cost ?? item.AverageCost;

                        var condition = (ReturnCondition)lineInput.Condition;
                        var recoverableUnitCost = condition switch
                        {
                            ReturnCondition.Saleable => originalUnitCost,
                            ReturnCondition.ZeroValue => 0m,
                            ReturnCondition.Damaged => Math.Min(originalUnitCost, Math.Max(0m, lineInput.RecoverableUnitCost ?? 0m)),
                            _ => 0m
                        };

                        decimal totalRestored;
                        if (condition == ReturnCondition.Saleable && remainingOrigCost.HasValue && priorReturnedQty + lineInput.Quantity == invLine.Quantity)
                        {
                            totalRestored = remainingOrigCost.Value;
                            recoverableUnitCost = lineInput.Quantity > 0
                                ? Math.Round(totalRestored / lineInput.Quantity, 4, MidpointRounding.AwayFromZero)
                                : 0m;
                        }
                        else
                        {
                            totalRestored = CurrencyPrecisionRules.RoundMoney(lineInput.Quantity * recoverableUnitCost, precision);
                            if (remainingOrigCost.HasValue && totalRestored > remainingOrigCost.Value)
                                totalRestored = remainingOrigCost.Value;
                        }
                        int? invJournalId = null;
                        if (totalRestored > 0 || condition == ReturnCondition.Saleable)
                        {
                            var move = _stock.Receive(
                                item, lineInput.Quantity, recoverableUnitCost,
                                DocumentTypes.CustomerReturnReceipt, receipt.Id, receipt.ReceiptDate,
                                $"Customer Return {no}");
                            _db.SaveChanges();
                            invJournalId = move.Id;
                        }

                        var invAccId = item.InventoryAccountId;
                        var cogsAccId = item.CostOfGoodsSoldAccountId;
                        if (totalRestored > 0)
                        {
                            if (!invAccId.HasValue || !cogsAccId.HasValue)
                                throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, $"Inventory and COGS accounts must be configured on item {item.Code}.", 422);

                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, invAccId.Value, totalRestored));
                            glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, cogsAccId.Value, totalRestored));
                            totalRestoredGl += totalRestored;
                        }

                        receipt.Lines.Add(new CustomerReturnReceiptLine
                        {
                            CompanyId = companyId,
                            SalesInvoiceLineId = invLine.Id,
                            ItemId = item.Id,
                            Quantity = lineInput.Quantity,
                            Condition = condition,
                            OriginalUnitCost = originalUnitCost,
                            RecoverableUnitCost = recoverableUnitCost,
                            TotalRestoredCost = totalRestored,
                            InventoryControlJournalId = invJournalId,
                            InventoryAccountId = invAccId,
                            CogsAccountId = cogsAccId
                        });
                    }

                    if (totalRestoredGl > 0)
                    {
                        _financial.SaveGeneralLedgerEntry(glHeader);
                        receipt.GeneralLedgerHeaderId = glHeader.Id;
                    }

                    _db.SaveChanges();
                    return BuildCustomerReturnDto(companyId, receipt.Id);
                },
                dto => dto.Id,
                nameof(CustomerReturnReceipt));
        }

        public SupplierReturnDispatchDto PostSupplierReturnDispatch(PostSupplierReturnDispatchRequest request)
        {
            return _coordinator.Execute(
                "supplier_return.post",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.Lines == null || request.Lines.Count == 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "At least one dispatch line is required.", 400);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A return reason is required.", 400);

                    var vendor = _db.Vendors.FirstOrDefault(v => v.CompanyId == companyId && v.Id == request.VendorId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Vendor not found.", 404);

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var clearingAccId = request.HasControlTransferred
                        ? _mappings.RequireActiveAccount(companyId, glSetting?.SupplierReturnClearingAccountId, "Supplier Return Clearing")
                        : (int?)null;

                    var no = GenerateNumber("SRTN", _db.SupplierReturnDispatches.Where(d => d.CompanyId == companyId && d.No != null).Select(d => d.No!));
                    var dispatch = new SupplierReturnDispatch
                    {
                        CompanyId = companyId,
                        VendorId = vendor.Id,
                        PurchaseReceiptHeaderId = request.PurchaseReceiptHeaderId,
                        PurchaseInvoiceHeaderId = request.PurchaseInvoiceHeaderId,
                        No = no,
                        DispatchDate = request.DispatchDate.Date,
                        ControlTransferDate = request.HasControlTransferred ? (request.ControlTransferDate ?? request.DispatchDate).Date : null,
                        HasControlTransferred = request.HasControlTransferred,
                        Reason = request.Reason.Trim(),
                        EvidenceReference = request.EvidenceReference?.Trim(),
                        ReturnClearingAccountId = clearingAccId,
                        Status = SettlementDocumentStatus.Posted,
                        PostedBy = _tenant.UserId ?? "system",
                        PostedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    _db.SupplierReturnDispatches.Add(dispatch);
                    _db.SaveChanges();

                    var effectiveDate = dispatch.ControlTransferDate ?? dispatch.DispatchDate;
                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.SupplierReturnDispatch,
                        effectiveDate,
                        $"Supplier Return Dispatch {no}",
                        dispatch.Id);

                    decimal totalCarrying = 0m;
                    foreach (var lineInput in request.Lines)
                    {
                        if (lineInput.Quantity <= 0)
                            throw new SettlementCommandException(SettlementErrorCodes.Validation, "Dispatch quantity must be positive.", 400);

                        if (lineInput.PurchaseInvoiceLineId.HasValue)
                        {
                            var billLine = _db.PurchaseInvoiceLines.AsNoTracking()
                                .FirstOrDefault(l => l.CompanyId == companyId && l.Id == lineInput.PurchaseInvoiceLineId.Value)
                                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source purchase bill line not found.", 404);
                            if (request.PurchaseInvoiceHeaderId.HasValue && billLine.PurchaseInvoiceHeaderId != request.PurchaseInvoiceHeaderId.Value)
                                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Purchase bill line does not belong to the specified bill.", 400);
                            var priorDispatched = _db.SupplierReturnDispatchLines
                                .Where(l => l.CompanyId == companyId && l.PurchaseInvoiceLineId == billLine.Id && l.SupplierReturnDispatch!.Status == SettlementDocumentStatus.Posted)
                                .Sum(l => (decimal?)l.Quantity) ?? 0m;
                            if (priorDispatched + lineInput.Quantity > billLine.Quantity)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Return dispatch quantity exceeds remaining billed quantity.", 409);
                        }

                        if (lineInput.PurchaseReceiptLineId.HasValue)
                        {
                            var recLine = _db.PurchaseReceiptLines.AsNoTracking()
                                .FirstOrDefault(l => l.CompanyId == companyId && l.Id == lineInput.PurchaseReceiptLineId.Value)
                                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Source purchase receipt line not found.", 404);
                            if (request.PurchaseReceiptHeaderId.HasValue && recLine.PurchaseReceiptHeaderId != request.PurchaseReceiptHeaderId.Value)
                                throw new SettlementCommandException(SettlementErrorCodes.Validation, "Purchase receipt line does not belong to the specified receipt.", 400);
                            var priorDispatched = _db.SupplierReturnDispatchLines
                                .Where(l => l.CompanyId == companyId && l.PurchaseReceiptLineId == recLine.Id && l.SupplierReturnDispatch!.Status == SettlementDocumentStatus.Posted)
                                .Sum(l => (decimal?)l.Quantity) ?? 0m;
                            if (priorDispatched + lineInput.Quantity > recLine.Quantity)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, "Return dispatch quantity exceeds remaining received quantity.", 409);
                        }

                        var item = _db.Items.Include(i => i.InventoryControlJournals)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == lineInput.ItemId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Item not found.", 404);

                        if (request.HasControlTransferred)
                        {
                            EnsureNotBackdatedBeforeLatestMovement(item, effectiveDate);
                            if (lineInput.Quantity > item.QuantityOnHand)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Insufficient stock on hand for {item.Code} (on hand: {item.QuantityOnHand}).", 409);

                            var invAccId = _mappings.RequireActiveAccount(companyId, item.InventoryAccountId, $"Inventory Account ({item.Code})");
                            var move = _stock.Issue(item, lineInput.Quantity, DocumentTypes.SupplierReturnDispatch, dispatch.Id, effectiveDate, $"Supplier Return {no}");
                            if (item.StockValue < 0)
                                throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Return would result in negative stock value for {item.Code}.", 409);
                            _db.SaveChanges();

                            var cost = CurrencyPrecisionRules.RoundMoney(move.TotalCost ?? 0m, precision);
                            if (cost > 0)
                            {
                                glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, clearingAccId!.Value, cost));
                                glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, invAccId, cost));
                                totalCarrying += cost;
                            }

                            dispatch.Lines.Add(new SupplierReturnDispatchLine
                            {
                                CompanyId = companyId,
                                PurchaseReceiptLineId = lineInput.PurchaseReceiptLineId,
                                PurchaseInvoiceLineId = lineInput.PurchaseInvoiceLineId,
                                ItemId = item.Id,
                                Quantity = lineInput.Quantity,
                                CarryingUnitCost = move.UnitCost ?? item.AverageCost,
                                TotalCarryingCost = cost,
                                InventoryControlJournalId = move.Id,
                                InventoryAccountId = invAccId
                            });
                        }
                        else
                        {
                            // Control has not transferred (goods still owned in transit): no inventory credit, no clearing GL
                            dispatch.Lines.Add(new SupplierReturnDispatchLine
                            {
                                CompanyId = companyId,
                                PurchaseReceiptLineId = lineInput.PurchaseReceiptLineId,
                                PurchaseInvoiceLineId = lineInput.PurchaseInvoiceLineId,
                                ItemId = item.Id,
                                Quantity = lineInput.Quantity,
                                CarryingUnitCost = item.AverageCost,
                                TotalCarryingCost = 0m
                            });
                        }
                    }

                    if (request.HasControlTransferred && totalCarrying > 0)
                    {
                        _financial.SaveGeneralLedgerEntry(glHeader);
                        dispatch.GeneralLedgerHeaderId = glHeader.Id;
                    }

                    _db.SaveChanges();
                    return BuildSupplierReturnDto(companyId, dispatch.Id);
                },
                dto => dto.Id,
                nameof(SupplierReturnDispatch));
        }

        public SupplierReturnDispatchDto MatchSupplierReturnCredit(MatchSupplierReturnCreditRequest request)
        {
            return _coordinator.Execute(
                "supplier_return.match_credit",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var dispatch = _db.SupplierReturnDispatches.Include(d => d.Lines)
                        .FirstOrDefault(d => d.CompanyId == companyId && d.Id == request.SupplierReturnDispatchId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier return dispatch not found.", 404);
                    if (!dispatch.HasControlTransferred)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot clear return before control has transferred.", 409);
                    if (dispatch.SupplierCreditNoteId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Supplier return dispatch is already matched to a credit.", 409);

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var creditAssetId = _mappings.RequireActiveAccount(companyId, glSetting?.SupplierCreditAssetAccountId, "Supplier Credits Receivable");
                    var clearingAccId = _mappings.RequireActiveAccount(companyId, dispatch.ReturnClearingAccountId ?? glSetting?.SupplierReturnClearingAccountId, "Supplier Return Clearing");
                    var varianceAccId = _mappings.RequireActiveAccount(companyId, glSetting?.ReturnValuationDifferenceAccountId, "Return Valuation Difference");

                    decimal taxAmt = request.OverrideTaxAmount ?? 0m;
                    int? taxAccId = null;
                    decimal taxRate = 0m;
                    if (request.TaxId.HasValue)
                    {
                        var tax = _db.Taxes.AsNoTracking().FirstOrDefault(t => t.CompanyId == companyId && t.Id == request.TaxId.Value)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Tax not found.", 404);
                        taxRate = tax.Rate;
                        taxAccId = tax.PurchasingAccountId ?? tax.SalesAccountId;
                        if (!request.OverrideTaxAmount.HasValue)
                            taxAmt = CurrencyPrecisionRules.RoundMoney(request.NetCreditAmount * tax.Rate / 100m, precision);
                        if (taxAmt > 0 && !taxAccId.HasValue)
                            throw new SettlementCommandException(SettlementErrorCodes.MissingMapping, "Input tax account not configured.", 422);
                    }

                    var gross = CurrencyPrecisionRules.RoundMoney(request.NetCreditAmount + taxAmt, precision);
                    var carrying = CurrencyPrecisionRules.RoundMoney(dispatch.Lines.Sum(l => l.TotalCarryingCost), precision);
                    var diff = CurrencyPrecisionRules.RoundMoney(request.NetCreditAmount - carrying, precision);

                    var creditNo = GenerateNumber("SCN", _db.SupplierCreditNotes.Where(c => c.CompanyId == companyId && c.No != null).Select(c => c.No!));
                    var credit = new SupplierCreditNote
                    {
                        CompanyId = companyId,
                        VendorId = dispatch.VendorId,
                        SourcePurchaseInvoiceHeaderId = dispatch.PurchaseInvoiceHeaderId,
                        No = creditNo,
                        VendorCreditNo = request.VendorCreditNo.Trim(),
                        Date = request.EffectiveDate.Date,
                        EffectiveDate = request.EffectiveDate.Date,
                        CurrencyCode = setting.Currency,
                        CurrencyPrecision = precision,
                        Kind = CreditNoteKind.GoodsReturn,
                        IsStandalone = dispatch.PurchaseInvoiceHeaderId == null,
                        Reason = request.Reason.Trim(),
                        EvidenceReference = request.EvidenceReference?.Trim(),
                        SupplierCreditAssetAccountId = creditAssetId,
                        ReturnClearingAccountId = clearingAccId,
                        ValuationDifferenceAccountId = varianceAccId,
                        Subtotal = request.NetCreditAmount,
                        TaxAmount = taxAmt,
                        TotalAmount = gross,
                        Status = SettlementDocumentStatus.Posted,
                        PostedBy = _tenant.UserId ?? "system",
                        PostedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    credit.Lines.Add(new SupplierCreditNoteLine
                    {
                        CompanyId = companyId,
                        MatchedReturnDispatchId = dispatch.Id,
                        TaxId = request.TaxId,
                        TaxAccountId = taxAccId,
                        TaxRateSnapshot = taxRate,
                        Description = $"Return credit for {dispatch.No}",
                        Quantity = dispatch.Lines.Sum(l => l.Quantity),
                        UnitCost = request.NetCreditAmount / Math.Max(1m, dispatch.Lines.Sum(l => l.Quantity)),
                        Subtotal = request.NetCreditAmount,
                        TaxAmount = taxAmt,
                        TotalAmount = gross
                    });
                    _db.SupplierCreditNotes.Add(credit);
                    _db.SaveChanges();

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.SupplierCreditNote,
                        credit.EffectiveDate,
                        $"Supplier Return Credit {creditNo} (Dispatch {dispatch.No})",
                        credit.Id);
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, creditAssetId, gross));
                    if (carrying > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, clearingAccId, carrying));
                    if (taxAmt > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, taxAccId!.Value, taxAmt));
                    if (diff > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, varianceAccId, diff));
                    else if (diff < 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, varianceAccId, Math.Abs(diff)));
                    _financial.SaveGeneralLedgerEntry(glHeader);

                    credit.GeneralLedgerHeaderId = glHeader.Id;
                    dispatch.SupplierCreditNoteId = credit.Id;
                    dispatch.Version++;

                    _db.SaveChanges();
                    return BuildSupplierReturnDto(companyId, dispatch.Id);
                },
                dto => dto.Id,
                nameof(SupplierReturnDispatch));
        }

        public SupplierCreditNoteDto PostStockRebate(PostStockRebateRequest request)
        {
            return _coordinator.Execute(
                "supplier_rebate.post",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.OnHandValueReduction < 0 || request.SoldCogsReduction < 0 || (request.OnHandValueReduction + request.SoldCogsReduction) <= 0)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Rebate allocation must be positive.", 400);

                    var item = _db.Items.Include(i => i.InventoryControlJournals)
                        .FirstOrDefault(i => i.CompanyId == companyId && i.Id == request.ItemId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Item not found.", 404);
                    if (request.OnHandValueReduction > 0)
                        EnsureNotBackdatedBeforeLatestMovement(item, request.EffectiveDate.Date);
                    if (request.OnHandValueReduction > item.StockValue)
                        throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"On-hand rebate ({request.OnHandValueReduction}) exceeds current stock value ({item.StockValue}).", 409);

                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var creditAssetId = _mappings.RequireActiveAccount(companyId, glSetting?.SupplierCreditAssetAccountId, "Supplier Credits Receivable");
                    var invAccId = request.OnHandValueReduction > 0 ? _mappings.RequireActiveAccount(companyId, item.InventoryAccountId, "Inventory Account") : (int?)null;
                    var cogsAccId = request.SoldCogsReduction > 0 ? _mappings.RequireActiveAccount(companyId, item.CostOfGoodsSoldAccountId, "COGS Account") : (int?)null;

                    var netTotal = CurrencyPrecisionRules.RoundMoney(request.OnHandValueReduction + request.SoldCogsReduction, precision);
                    decimal taxAmt = request.OverrideTaxAmount ?? 0m;
                    int? taxAccId = null;
                    if (request.TaxId.HasValue)
                    {
                        var tax = _db.Taxes.AsNoTracking().First(t => t.CompanyId == companyId && t.Id == request.TaxId.Value);
                        taxAccId = tax.PurchasingAccountId ?? tax.SalesAccountId;
                        if (!request.OverrideTaxAmount.HasValue)
                            taxAmt = CurrencyPrecisionRules.RoundMoney(netTotal * tax.Rate / 100m, precision);
                    }
                    var gross = CurrencyPrecisionRules.RoundMoney(netTotal + taxAmt, precision);

                    if (request.OnHandValueReduction > 0)
                    {
                        item.StockValue = CurrencyPrecisionRules.RoundMoney(item.StockValue - request.OnHandValueReduction, precision);
                        if (item.QuantityOnHand > 0)
                            item.AverageCost = Math.Round(item.StockValue / item.QuantityOnHand, 4, MidpointRounding.AwayFromZero);
                    }

                    var no = GenerateNumber("SCN", _db.SupplierCreditNotes.Where(c => c.CompanyId == companyId && c.No != null).Select(c => c.No!));
                    var credit = new SupplierCreditNote
                    {
                        CompanyId = companyId,
                        VendorId = request.VendorId,
                        No = no,
                        VendorCreditNo = request.VendorCreditNo.Trim(),
                        Date = request.EffectiveDate.Date,
                        EffectiveDate = request.EffectiveDate.Date,
                        CurrencyCode = setting.Currency,
                        CurrencyPrecision = precision,
                        Kind = CreditNoteKind.Rebate,
                        IsStandalone = true,
                        Reason = request.Reason.Trim(),
                        EvidenceReference = request.EvidenceReference.Trim(),
                        SupplierCreditAssetAccountId = creditAssetId,
                        Subtotal = netTotal,
                        TaxAmount = taxAmt,
                        TotalAmount = gross,
                        Status = SettlementDocumentStatus.Posted,
                        PostedBy = _tenant.UserId ?? "system",
                        PostedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    _db.SupplierCreditNotes.Add(credit);
                    _db.SaveChanges();

                    if (request.OnHandValueReduction > 0)
                    {
                        item.InventoryControlJournals.Add(new Core.Domain.Items.InventoryControlJournal
                        {
                            CompanyId = companyId,
                            Item = item,
                            ItemId = item.Id,
                            MeasurementId = item.PurchaseMeasurementId ?? item.SmallestMeasurementId ?? item.SellMeasurementId ?? 1,
                            DocumentType = DocumentTypes.SupplierCreditNote,
                            DocumentId = credit.Id,
                            INQty = null,
                            OUTQty = 0m,
                            Date = credit.EffectiveDate,
                            UnitCost = 0m,
                            TotalCost = request.OnHandValueReduction,
                            TotalAmount = -request.OnHandValueReduction,
                            Memo = $"Stock Rebate {no}",
                            IsReverse = false
                        });
                    }

                    var glHeader = _financial.CreateGeneralLedgerHeader(DocumentTypes.SupplierCreditNote, credit.EffectiveDate, $"Stock Rebate {no}", credit.Id);
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, creditAssetId, gross));
                    if (request.OnHandValueReduction > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, invAccId!.Value, request.OnHandValueReduction));
                    if (request.SoldCogsReduction > 0)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, cogsAccId!.Value, request.SoldCogsReduction));
                    if (taxAmt > 0 && taxAccId.HasValue)
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, taxAccId.Value, taxAmt));
                    _financial.SaveGeneralLedgerEntry(glHeader);

                    credit.GeneralLedgerHeaderId = glHeader.Id;
                    _db.SaveChanges();

                    return new SupplierCreditNoteDto(
                        credit.Id, credit.No, credit.VendorCreditNo, credit.VendorId, "", null, null,
                        credit.Date, credit.EffectiveDate, credit.CurrencyCode, credit.CurrencyPrecision,
                        (int)credit.Kind, credit.IsStandalone, credit.Reason, credit.EvidenceReference,
                        credit.Subtotal, credit.TaxAmount, credit.TotalAmount, 0m, 0m, credit.TotalAmount,
                        "Available", (int)credit.Status, credit.Status.ToString(), credit.GeneralLedgerHeaderId, null,
                        null, null, credit.PostedBy, credit.Version, Array.Empty<SupplierCreditLineDto>(), Array.Empty<SettlementApplicationSummaryDto>());
                },
                dto => dto.Id,
                nameof(SupplierCreditNote));
        }

        public CustomerReturnReceiptDto ReverseCustomerReturn(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "customer_return.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var receipt = _db.CustomerReturnReceipts.Include(r => r.Lines)
                        .FirstOrDefault(r => r.CompanyId == companyId && r.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Customer return receipt not found.", 404);
                    if (receipt.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted customer returns can be reversed.", 409);
                    if (request.ReversalDate.Date < receipt.ReceiptDate.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede return receipt date.", 400);

                    int? revGlId = null;
                    if (receipt.GeneralLedgerHeaderId.HasValue)
                    {
                        var revGl = _financial.CreateReversingEntry(
                            receipt.GeneralLedgerHeaderId.Value,
                            $"Reverse Customer Return {receipt.No}: {request.Reason.Trim()}",
                            request.ReversalDate.Date);
                        _financial.SaveGeneralLedgerEntry(revGl);
                        revGlId = revGl.Id;
                    }

                    foreach (var line in receipt.Lines)
                    {
                        if (!line.InventoryControlJournalId.HasValue && line.TotalRestoredCost <= 0m)
                            continue;

                        var item = _db.Items.Include(i => i.InventoryControlJournals)
                            .FirstOrDefault(i => i.CompanyId == companyId && i.Id == line.ItemId)
                            ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Item not found.", 404);

                        EnsureNotBackdatedBeforeLatestMovement(item, request.ReversalDate.Date);
                        if (line.Quantity > item.QuantityOnHand)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Insufficient stock on hand to reverse return for {item.Code} (on hand: {item.QuantityOnHand}).", 409);

                        var reverseMove = _stock.Issue(
                            item,
                            line.Quantity,
                            DocumentTypes.CustomerReturnReceipt,
                            receipt.Id,
                            request.ReversalDate.Date,
                            $"Reverse Customer Return {receipt.No}: {request.Reason.Trim()}");
                        reverseMove.IsReverse = true;
                        if (item.StockValue < 0)
                            throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Reversal would result in negative stock value for {item.Code}.", 409);
                    }

                    receipt.Status = SettlementDocumentStatus.Reversed;
                    receipt.VoidGeneralLedgerHeaderId = revGlId;
                    receipt.ReversedBy = _tenant.UserId ?? "system";
                    receipt.ReversedAtUtc = DateTime.UtcNow;
                    receipt.ReversalReason = request.Reason.Trim();
                    receipt.Version++;

                    _db.SaveChanges();
                    return BuildCustomerReturnDto(companyId, receipt.Id);
                },
                dto => dto.Id,
                nameof(CustomerReturnReceipt));
        }

        public SupplierReturnDispatchDto ReverseSupplierReturnDispatch(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "supplier_return.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, _) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var dispatch = _db.SupplierReturnDispatches.Include(d => d.Lines)
                        .FirstOrDefault(d => d.CompanyId == companyId && d.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Supplier return dispatch not found.", 404);
                    if (dispatch.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted supplier return dispatches can be reversed.", 409);

                    if (dispatch.SupplierCreditNoteId.HasValue)
                    {
                        var matchedCredit = _db.SupplierCreditNotes.AsNoTracking()
                            .FirstOrDefault(c => c.CompanyId == companyId && c.Id == dispatch.SupplierCreditNoteId.Value);
                        if (matchedCredit != null && matchedCredit.Status != SettlementDocumentStatus.Reversed)
                            throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Reverse the matched supplier credit note before reversing this return dispatch.", 409);
                    }

                    var effectiveDate = (dispatch.ControlTransferDate ?? dispatch.DispatchDate).Date;
                    if (request.ReversalDate.Date < effectiveDate)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede dispatch effective date.", 400);

                    int? revGlId = null;
                    if (dispatch.GeneralLedgerHeaderId.HasValue)
                    {
                        var revGl = _financial.CreateReversingEntry(
                            dispatch.GeneralLedgerHeaderId.Value,
                            $"Reverse Supplier Return {dispatch.No}: {request.Reason.Trim()}",
                            request.ReversalDate.Date);
                        _financial.SaveGeneralLedgerEntry(revGl);
                        revGlId = revGl.Id;
                    }

                    if (dispatch.HasControlTransferred)
                    {
                        foreach (var line in dispatch.Lines)
                        {
                            if (!line.InventoryControlJournalId.HasValue)
                                continue;

                            var item = _db.Items.Include(i => i.InventoryControlJournals)
                                .FirstOrDefault(i => i.CompanyId == companyId && i.Id == line.ItemId)
                                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Item not found.", 404);

                            EnsureNotBackdatedBeforeLatestMovement(item, request.ReversalDate.Date);
                            var reverseMove = _stock.Receive(
                                item,
                                line.Quantity,
                                line.CarryingUnitCost,
                                DocumentTypes.SupplierReturnDispatch,
                                dispatch.Id,
                                request.ReversalDate.Date,
                                $"Reverse Supplier Return {dispatch.No}: {request.Reason.Trim()}");
                            reverseMove.IsReverse = true;
                        }
                    }

                    dispatch.Status = SettlementDocumentStatus.Reversed;
                    dispatch.VoidGeneralLedgerHeaderId = revGlId;
                    dispatch.ReversedBy = _tenant.UserId ?? "system";
                    dispatch.ReversedAtUtc = DateTime.UtcNow;
                    dispatch.ReversalReason = request.Reason.Trim();
                    dispatch.Version++;

                    _db.SaveChanges();
                    return BuildSupplierReturnDto(companyId, dispatch.Id);
                },
                dto => dto.Id,
                nameof(SupplierReturnDispatch));
        }

        private static void EnsureNotBackdatedBeforeLatestMovement(Core.Domain.Items.Item item, DateTime date)
        {
            var latest = item.InventoryControlJournals.OrderByDescending(j => j.Date).Select(j => (DateTime?)j.Date).FirstOrDefault();
            if (latest.HasValue && date.Date < latest.Value.Date)
                throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, $"Cannot backdate return movement for {item.Code} before its latest movement ({latest.Value:yyyy-MM-dd}).", 400);
        }

        private CustomerReturnReceiptDto BuildCustomerReturnDto(int companyId, int id)
        {
            var r = _db.CustomerReturnReceipts.AsNoTracking().Include(x => x.Lines)
                .First(x => x.CompanyId == companyId && x.Id == id);
            return new CustomerReturnReceiptDto(
                r.Id, r.No, r.CustomerId, r.SalesInvoiceHeaderId, r.CustomerCreditNoteId, r.ReceiptDate, (int)r.Status, r.GeneralLedgerHeaderId, r.Version,
                r.Lines.Select(l => new CustomerReturnReceiptLineDto(l.Id, l.SalesInvoiceLineId, l.ItemId, l.Quantity, (int)l.Condition, l.OriginalUnitCost, l.RecoverableUnitCost, l.TotalRestoredCost)).ToList());
        }

        private SupplierReturnDispatchDto BuildSupplierReturnDto(int companyId, int id)
        {
            var d = _db.SupplierReturnDispatches.AsNoTracking().Include(x => x.Lines)
                .First(x => x.CompanyId == companyId && x.Id == id);
            return new SupplierReturnDispatchDto(
                d.Id, d.No, d.VendorId, d.DispatchDate, d.ControlTransferDate, d.HasControlTransferred, (int)d.Status, d.GeneralLedgerHeaderId, d.SupplierCreditNoteId,
                d.Lines.Sum(l => l.TotalCarryingCost), d.Version,
                d.Lines.Select(l => new SupplierReturnDispatchLineDto(l.Id, l.ItemId, l.Quantity, l.CarryingUnitCost, l.TotalCarryingCost)).ToList());
        }

        private static string GenerateNumber(string prefix, IEnumerable<string> existing)
        {
            var max = 0;
            foreach (var s in existing)
            {
                if (s.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(s[(prefix.Length + 1)..], out var num) && num > max)
                    max = num;
            }
            return $"{prefix}-{(max + 1):D5}";
        }
    }
}
