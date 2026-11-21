// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;
using Dto.Sales;

namespace Dto.Financial
{
    public sealed record SaveSupplierAdvanceDraftRequest(
        [property: PublicId("adv")] int? Id,
        [property: PublicId("ven")] int VendorId,
        [property: PublicId("po")] int? PurchaseOrderHeaderId,
        DateTime Date,
        decimal Amount,
        [property: PublicId("ba")] int BankId,
        string Purpose,
        bool IsRefundable,
        string? ReferenceNo,
        string? EvidenceReference,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record ApplySupplierAdvanceItemRequest(
        [property: PublicId("bill")] int PurchaseInvoiceHeaderId,
        decimal Amount,
        long? ExpectedBillVersion = null);

    public sealed record ApplySupplierAdvanceRequest(
        DateTime EffectiveDate,
        IReadOnlyList<ApplySupplierAdvanceItemRequest> Applications,
        string? IdempotencyKey = null,
        long? ExpectedAdvanceVersion = null);

    public sealed record SupplierAdvanceDto(
        [property: PublicId("adv")] int Id,
        string? No,
        [property: PublicId("ven")] int VendorId,
        string VendorName,
        [property: PublicId("po")] int? PurchaseOrderHeaderId,
        DateTime Date,
        string CurrencyCode,
        int CurrencyPrecision,
        decimal Amount,
        decimal AppliedAmount,
        decimal RefundedAmount,
        decimal AvailableAmount,
        [property: PublicId("ba")] int BankId,
        string BankName,
        string Purpose,
        bool IsRefundable,
        string? ReferenceNo,
        string? EvidenceReference,
        int Status,
        string StatusLabel,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<SettlementApplicationSummaryDto> Applications);

    public sealed record RefundAllocationInputDto(
        int SourceType, // 1=CustomerCreditNote, 2=SupplierCreditNote, 3=SupplierAdvance
        [property: PublicIdByType("SourceType", PublicIdTypeDiscriminatorKind.SettlementSourceType)] int SourceId,
        decimal Amount);

    public sealed record SavePartyRefundDraftRequest(
        [property: PublicId("rf")] int? Id,
        int Direction, // 1=CustomerPayout, 2=SupplierInflow
        [property: PublicId("cus")] int? CustomerId,
        [property: PublicId("ven")] int? VendorId,
        DateTime Date,
        [property: PublicId("ba")] int BankId,
        string PaymentMethod,
        string? ExternalReference,
        string? EvidenceReference,
        IReadOnlyList<RefundAllocationInputDto> Allocations,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record PostPartyRefundRequest(
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record PartyRefundAllocationDto(
        [property: PublicId("rfa")] int Id,
        int SourceType,
        string SourceTypeLabel,
        [property: PublicIdByType("SourceType", PublicIdTypeDiscriminatorKind.SettlementSourceType)] int SourceId,
        string SourceNo,
        decimal Amount);

    public sealed record PartyRefundDto(
        [property: PublicId("rf")] int Id,
        string? No,
        int Direction,
        string DirectionLabel,
        [property: PublicId("cus")] int? CustomerId,
        [property: PublicId("ven")] int? VendorId,
        string PartyName,
        DateTime Date,
        string CurrencyCode,
        int CurrencyPrecision,
        decimal Amount,
        [property: PublicId("ba")] int BankId,
        string BankName,
        string PaymentMethod,
        string? ExternalReference,
        string? EvidenceReference,
        int Status,
        string StatusLabel,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<PartyRefundAllocationDto> Allocations);
}
