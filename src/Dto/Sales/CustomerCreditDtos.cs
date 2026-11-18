// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Sales
{
    public sealed record CustomerCreditLineInputDto(
        int? SourceSalesInvoiceLineId,
        int? ItemId,
        int? TaxId,
        bool IsShippingLine,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal? OverrideTaxAmount = null);

    public sealed record SaveCustomerCreditDraftRequest(
        int? Id,
        int CustomerId,
        int? SourceSalesInvoiceHeaderId,
        DateTime Date,
        int Kind,
        bool ReturnGoodsWithoutRestock,
        string Reason,
        string? SourceReference,
        string? EvidenceReference,
        IReadOnlyList<CustomerCreditLineInputDto> Lines,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record PostCustomerCreditRequest(
        decimal? ApplyToSourceInvoiceAmount = null,
        DateTime? ApplicationDate = null,
        bool SubmitAndPost = true,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record ApplyCustomerCreditItemRequest(
        int SalesInvoiceHeaderId,
        decimal Amount,
        long? ExpectedInvoiceVersion = null);

    public sealed record ApplyCustomerCreditRequest(
        DateTime EffectiveDate,
        IReadOnlyList<ApplyCustomerCreditItemRequest> Applications,
        string? IdempotencyKey = null,
        long? ExpectedCreditVersion = null);

    public sealed record ReverseSettlementRequest(
        DateTime ReversalDate,
        string Reason,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record CustomerCreditLineDto(
        int Id,
        int? SourceSalesInvoiceLineId,
        int? ItemId,
        int? TaxId,
        decimal TaxRateSnapshot,
        bool IsShippingLine,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount);

    public sealed record SettlementApplicationSummaryDto(
        int Id,
        int SourceId,
        string SourceType,
        int DestinationId,
        string DestinationNo,
        decimal Amount,
        DateTime EffectiveDate,
        int GeneralLedgerHeaderId,
        bool IsReversed,
        DateTime? ReversalDate,
        string? ReversalReason);

    public sealed record CustomerCreditNoteDto(
        int Id,
        string? No,
        int CustomerId,
        string CustomerName,
        int? SourceSalesInvoiceHeaderId,
        string? SourceInvoiceNo,
        DateTime Date,
        string CurrencyCode,
        int CurrencyPrecision,
        int Kind,
        bool ReturnGoodsWithoutRestock,
        string Reason,
        string? SourceReference,
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
        IReadOnlyList<CustomerCreditLineDto> Lines,
        IReadOnlyList<SettlementApplicationSummaryDto> Applications);

    public sealed record CustomerCreditPreviewDto(
        decimal Subtotal,
        decimal TaxAmount,
        decimal TotalAmount,
        decimal RemainingCreditableOnInvoice,
        decimal InvoiceBalanceDue,
        decimal DefaultApplyAmount,
        decimal RemainingAvailableAfterDefaultApply,
        bool RequiresTeamReview,
        bool MappingsReady,
        IReadOnlyList<CustomerCreditLineDto> Lines);
}
