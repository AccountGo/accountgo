// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Dto.Sales;

namespace Dto.Financial
{
    public sealed record SaveSupplierAdvanceDraftRequest(
        int? Id,
        int VendorId,
        int? PurchaseOrderHeaderId,
        DateTime Date,
        decimal Amount,
        int BankId,
        string Purpose,
        bool IsRefundable,
        string? ReferenceNo,
        string? EvidenceReference,
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record ApplySupplierAdvanceItemRequest(
        int PurchaseInvoiceHeaderId,
        decimal Amount,
        long? ExpectedBillVersion = null);

    public sealed record ApplySupplierAdvanceRequest(
        DateTime EffectiveDate,
        IReadOnlyList<ApplySupplierAdvanceItemRequest> Applications,
        string? IdempotencyKey = null,
        long? ExpectedAdvanceVersion = null);

    public sealed record SupplierAdvanceDto(
        int Id,
        string? No,
        int VendorId,
        string VendorName,
        int? PurchaseOrderHeaderId,
        DateTime Date,
        string CurrencyCode,
        int CurrencyPrecision,
        decimal Amount,
        decimal AppliedAmount,
        decimal RefundedAmount,
        decimal AvailableAmount,
        int BankId,
        string BankName,
        string Purpose,
        bool IsRefundable,
        string? ReferenceNo,
        string? EvidenceReference,
        int Status,
        string StatusLabel,
        int? GeneralLedgerHeaderId,
        int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<SettlementApplicationSummaryDto> Applications);

    public sealed record RefundAllocationInputDto(
        int SourceType, // 1=CustomerCreditNote, 2=SupplierCreditNote, 3=SupplierAdvance
        int SourceId,
        decimal Amount);

    public sealed record SavePartyRefundDraftRequest(
        int? Id,
        int Direction, // 1=CustomerPayout, 2=SupplierInflow
        int? CustomerId,
        int? VendorId,
        DateTime Date,
        int BankId,
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
        int Id,
        int SourceType,
        string SourceTypeLabel,
        int SourceId,
        string SourceNo,
        decimal Amount);

    public sealed record PartyRefundDto(
        int Id,
        string? No,
        int Direction,
        string DirectionLabel,
        int? CustomerId,
        int? VendorId,
        string PartyName,
        DateTime Date,
        string CurrencyCode,
        int CurrencyPrecision,
        decimal Amount,
        int BankId,
        string BankName,
        string PaymentMethod,
        string? ExternalReference,
        string? EvidenceReference,
        int Status,
        string StatusLabel,
        int? GeneralLedgerHeaderId,
        int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<PartyRefundAllocationDto> Allocations);
}
