// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Inventory
{
    public sealed record CustomerReturnLineInputDto(
        int SalesInvoiceLineId,
        decimal Quantity,
        int Condition, // 1=Saleable, 2=Damaged, 3=ZeroValue
        decimal? RecoverableUnitCost = null);

    public sealed record PostCustomerReturnRequest(
        int SalesInvoiceHeaderId,
        int? CustomerCreditNoteId,
        DateTime ReceiptDate,
        string? Notes,
        IReadOnlyList<CustomerReturnLineInputDto> Lines,
        string? IdempotencyKey = null);

    public sealed record SupplierReturnLineInputDto(
        int? PurchaseReceiptLineId,
        int? PurchaseInvoiceLineId,
        int ItemId,
        decimal Quantity);

    public sealed record PostSupplierReturnDispatchRequest(
        int VendorId,
        int? PurchaseReceiptHeaderId,
        int? PurchaseInvoiceHeaderId,
        DateTime DispatchDate,
        DateTime? ControlTransferDate,
        bool HasControlTransferred,
        string Reason,
        string? EvidenceReference,
        IReadOnlyList<SupplierReturnLineInputDto> Lines,
        string? IdempotencyKey = null);

    public sealed record MatchSupplierReturnCreditRequest(
        int SupplierReturnDispatchId,
        string VendorCreditNo,
        DateTime EffectiveDate,
        decimal NetCreditAmount,
        int? TaxId,
        decimal? OverrideTaxAmount,
        string Reason,
        string? EvidenceReference,
        string? IdempotencyKey = null);

    public sealed record PostStockRebateRequest(
        int VendorId,
        int ItemId,
        DateTime EffectiveDate,
        string VendorCreditNo,
        decimal OnHandValueReduction,
        decimal SoldCogsReduction,
        int? TaxId,
        decimal? OverrideTaxAmount,
        string Reason,
        string EvidenceReference,
        string? IdempotencyKey = null);

    public sealed record CustomerReturnReceiptLineDto(
        int Id,
        int SalesInvoiceLineId,
        int ItemId,
        decimal Quantity,
        int Condition,
        decimal OriginalUnitCost,
        decimal RecoverableUnitCost,
        decimal TotalRestoredCost);

    public sealed record CustomerReturnReceiptDto(
        int Id,
        string? No,
        int CustomerId,
        int SalesInvoiceHeaderId,
        int? CustomerCreditNoteId,
        DateTime ReceiptDate,
        int Status,
        int? GeneralLedgerHeaderId,
        long Version,
        IReadOnlyList<CustomerReturnReceiptLineDto> Lines);

    public sealed record SupplierReturnDispatchLineDto(
        int Id,
        int ItemId,
        decimal Quantity,
        decimal CarryingUnitCost,
        decimal TotalCarryingCost);

    public sealed record SupplierReturnDispatchDto(
        int Id,
        string? No,
        int VendorId,
        DateTime DispatchDate,
        DateTime? ControlTransferDate,
        bool HasControlTransferred,
        int Status,
        int? GeneralLedgerHeaderId,
        int? SupplierCreditNoteId,
        decimal TotalCarryingCost,
        long Version,
        IReadOnlyList<SupplierReturnDispatchLineDto> Lines);
}
