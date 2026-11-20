// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;
using Dto.Sales;

namespace Dto.Purchasing
{
    public sealed record SupplierCreditLineInputDto(
        [property: PublicId("bl")] int? SourcePurchaseInvoiceLineId,
        [property: PublicId("srd")] int? MatchedReturnDispatchId,
        [property: PublicId("item")] int? ItemId,
        [property: PublicId("acct")] int? ExpenseOrAssetAccountId,
        [property: PublicId("tax")] int? TaxId,
        bool TaxRecoverable,
        string Description,
        decimal Quantity,
        decimal UnitCost,
        decimal? OverrideTaxAmount = null);

    public sealed record SaveSupplierCreditDraftRequest(
        [property: PublicId("scn")] int? Id,
        [property: PublicId("ven")] int VendorId,
        [property: PublicId("bill")] int? SourcePurchaseInvoiceHeaderId,
        string VendorCreditNo,
        DateTime Date,
        DateTime? EffectiveDate,
        int Kind,
        bool IsStandalone,
        string Reason,
        string? EvidenceReference,
        IReadOnlyList<SupplierCreditLineInputDto> Lines,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record PostSupplierCreditRequest(
        decimal? ApplyToSourceBillAmount = null,
        DateTime? ApplicationDate = null,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record ApplySupplierCreditItemRequest(
        [property: PublicId("bill")] int PurchaseInvoiceHeaderId,
        decimal Amount,
        long? ExpectedBillVersion = null);

    public sealed record ApplySupplierCreditRequest(
        DateTime EffectiveDate,
        IReadOnlyList<ApplySupplierCreditItemRequest> Applications,
        string? IdempotencyKey = null,
        long? ExpectedCreditVersion = null);

    public sealed record SupplierCreditLineDto(
        [property: PublicId("scnl")] int Id,
        [property: PublicId("bl")] int? SourcePurchaseInvoiceLineId,
        [property: PublicId("srd")] int? MatchedReturnDispatchId,
        [property: PublicId("item")] int? ItemId,
        [property: PublicId("acct")] int? ExpenseOrAssetAccountId,
        [property: PublicId("tax")] int? TaxId,
        decimal TaxRateSnapshot,
        bool TaxRecoverableSnapshot,
        string Description,
        decimal Quantity,
        decimal UnitCost,
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount);

    public sealed record SupplierCreditNoteDto(
        [property: PublicId("scn")] int Id,
        string? No,
        string VendorCreditNo,
        [property: PublicId("ven")] int VendorId,
        string VendorName,
        [property: PublicId("bill")] int? SourcePurchaseInvoiceHeaderId,
        string? SourceBillNo,
        DateTime Date,
        DateTime EffectiveDate,
        string CurrencyCode,
        int CurrencyPrecision,
        int Kind,
        bool IsStandalone,
        string Reason,
        string? EvidenceReference,
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount,
        decimal AppliedAmount,
        decimal RefundedAmount,
        decimal AvailableAmount,
        string SettlementState,
        int Status,
        string StatusLabel,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<SupplierCreditLineDto> Lines,
        IReadOnlyList<SettlementApplicationSummaryDto> Applications);
}
