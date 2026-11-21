// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;

namespace Dto.Inventory
{
    public sealed record CustomerReturnLineInputDto(
        [property: PublicId("il")] int SalesInvoiceLineId,
        decimal Quantity,
        int Condition, // 1=Saleable, 2=Damaged, 3=ZeroValue
        decimal? RecoverableUnitCost = null);

    public sealed record PostCustomerReturnRequest(
        [property: PublicId("inv")] int SalesInvoiceHeaderId,
        [property: PublicId("cn")] int? CustomerCreditNoteId,
        DateTime ReceiptDate,
        string? Notes,
        IReadOnlyList<CustomerReturnLineInputDto> Lines,
        string? IdempotencyKey = null);

    public sealed record SupplierReturnLineInputDto(
        [property: PublicId("prcl")] int? PurchaseReceiptLineId,
        [property: PublicId("bl")] int? PurchaseInvoiceLineId,
        [property: PublicId("item")] int ItemId,
        decimal Quantity);

    public sealed record PostSupplierReturnDispatchRequest(
        [property: PublicId("ven")] int VendorId,
        [property: PublicId("prc")] int? PurchaseReceiptHeaderId,
        [property: PublicId("bill")] int? PurchaseInvoiceHeaderId,
        DateTime DispatchDate,
        DateTime? ControlTransferDate,
        bool HasControlTransferred,
        string Reason,
        string? EvidenceReference,
        IReadOnlyList<SupplierReturnLineInputDto> Lines,
        string? IdempotencyKey = null);

    public sealed record MatchSupplierReturnCreditRequest(
        [property: PublicId("srd")] int SupplierReturnDispatchId,
        string VendorCreditNo,
        DateTime EffectiveDate,
        decimal NetCreditAmount,
        [property: PublicId("tax")] int? TaxId,
        decimal? OverrideTaxAmount,
        string Reason,
        string? EvidenceReference,
        string? IdempotencyKey = null);

    public sealed record PostStockRebateRequest(
        [property: PublicId("ven")] int VendorId,
        [property: PublicId("item")] int ItemId,
        DateTime EffectiveDate,
        string VendorCreditNo,
        decimal OnHandValueReduction,
        decimal SoldCogsReduction,
        [property: PublicId("tax")] int? TaxId,
        decimal? OverrideTaxAmount,
        string Reason,
        string EvidenceReference,
        string? IdempotencyKey = null);

    public sealed record CustomerReturnReceiptLineDto(
        [property: PublicId("crrl")] int Id,
        [property: PublicId("il")] int SalesInvoiceLineId,
        [property: PublicId("item")] int ItemId,
        decimal Quantity,
        int Condition,
        decimal OriginalUnitCost,
        decimal RecoverableUnitCost,
        decimal TotalRestoredCost);

    public sealed record CustomerReturnReceiptDto(
        [property: PublicId("crr")] int Id,
        string? No,
        [property: PublicId("cus")] int CustomerId,
        [property: PublicId("inv")] int SalesInvoiceHeaderId,
        [property: PublicId("cn")] int? CustomerCreditNoteId,
        DateTime ReceiptDate,
        int Status,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        long Version,
        IReadOnlyList<CustomerReturnReceiptLineDto> Lines);

    public sealed record SupplierReturnDispatchLineDto(
        [property: PublicId("srdl")] int Id,
        [property: PublicId("item")] int ItemId,
        decimal Quantity,
        decimal CarryingUnitCost,
        decimal TotalCarryingCost);

    public sealed record SupplierReturnDispatchDto(
        [property: PublicId("srd")] int Id,
        string? No,
        [property: PublicId("ven")] int VendorId,
        DateTime DispatchDate,
        DateTime? ControlTransferDate,
        bool HasControlTransferred,
        int Status,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("scn")] int? SupplierCreditNoteId,
        decimal TotalCarryingCost,
        long Version,
        IReadOnlyList<SupplierReturnDispatchLineDto> Lines);
}
