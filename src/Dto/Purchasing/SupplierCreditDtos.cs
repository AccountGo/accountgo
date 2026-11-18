// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Dto.Sales;

namespace Dto.Purchasing
{
    public sealed record SupplierCreditLineInputDto(
        int? SourcePurchaseInvoiceLineId,
        int? MatchedReturnDispatchId,
        int? ItemId,
        int? ExpenseOrAssetAccountId,
        int? TaxId,
        bool TaxRecoverable,
        string Description,
        decimal Quantity,
        decimal UnitCost,
        decimal? OverrideTaxAmount = null);

    public sealed record SaveSupplierCreditDraftRequest(
        int? Id,
        int VendorId,
        int? SourcePurchaseInvoiceHeaderId,
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
        int PurchaseInvoiceHeaderId,
        decimal Amount,
        long? ExpectedBillVersion = null);

    public sealed record ApplySupplierCreditRequest(
        DateTime EffectiveDate,
        IReadOnlyList<ApplySupplierCreditItemRequest> Applications,
        string? IdempotencyKey = null,
        long? ExpectedCreditVersion = null);

    public sealed record SupplierCreditLineDto(
        int Id,
        int? SourcePurchaseInvoiceLineId,
        int? MatchedReturnDispatchId,
        int? ItemId,
        int? ExpenseOrAssetAccountId,
        int? TaxId,
        decimal TaxRateSnapshot,
        bool TaxRecoverableSnapshot,
        string Description,
        decimal Quantity,
        decimal UnitCost,
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount);

    public sealed record SupplierCreditNoteDto(
        int Id,
        string? No,
        string VendorCreditNo,
        int VendorId,
        string VendorName,
        int? SourcePurchaseInvoiceHeaderId,
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
        int? GeneralLedgerHeaderId,
        int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<SupplierCreditLineDto> Lines,
        IReadOnlyList<SettlementApplicationSummaryDto> Applications);
}
