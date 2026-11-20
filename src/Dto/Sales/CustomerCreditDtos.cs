// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;

namespace Dto.Sales
{
    public sealed record CustomerCreditLineInputDto(
        [property: PublicId("il")] int? SourceSalesInvoiceLineId,
        [property: PublicId("item")] int? ItemId,
        [property: PublicId("tax")] int? TaxId,
        bool IsShippingLine,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal? OverrideTaxAmount = null);

    public sealed record SaveCustomerCreditDraftRequest(
        [property: PublicId("cn")] int? Id,
        [property: PublicId("cus")] int CustomerId,
        [property: PublicId("inv")] int? SourceSalesInvoiceHeaderId,
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
        [property: PublicId("inv")] int SalesInvoiceHeaderId,
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
        [property: PublicId("cnl")] int Id,
        [property: PublicId("il")] int? SourceSalesInvoiceLineId,
        [property: PublicId("item")] int? ItemId,
        [property: PublicId("tax")] int? TaxId,
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
        [property: PublicId("sapp")] int Id,
        [property: PublicIdByType("SourceType")] int SourceId,
        string SourceType,
        [property: PublicIdByType("SourceType", PublicIdTypeDiscriminatorKind.SettlementDestinationFromSourceType)] int DestinationId,
        string DestinationNo,
        decimal Amount,
        DateTime EffectiveDate,
        [property: PublicId("gl")] int GeneralLedgerHeaderId,
        bool IsReversed,
        DateTime? ReversalDate,
        string? ReversalReason);

    public sealed record CustomerCreditNoteDto(
        [property: PublicId("cn")] int Id,
        string? No,
        [property: PublicId("cus")] int CustomerId,
        string CustomerName,
        [property: PublicId("inv")] int? SourceSalesInvoiceHeaderId,
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
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
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
