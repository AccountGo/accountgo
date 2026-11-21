// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;

namespace Dto.Sales
{
    public sealed record RequestWriteOffCommand(
        [property: PublicId("inv")] int SalesInvoiceHeaderId,
        DateTime Date,
        decimal Amount,
        decimal AllowanceAllocatedAmount,
        string Reason,
        string CollectionEvidence,
        string? IdempotencyKey = null,
        long? ExpectedInvoiceVersion = null);

    public sealed record PostWriteOffCommand(
        string? IdempotencyKey = null,
        long? ExpectedVersion = null);

    public sealed record RecordWriteOffRecoveryCommand(
        DateTime Date,
        decimal Amount,
        [property: PublicId("ba")] int BankId,
        string? ReferenceNo,
        string? EvidenceReference,
        string? IdempotencyKey = null,
        long? ExpectedWriteOffVersion = null);

    public sealed record WriteOffRecoveryDto(
        [property: PublicId("wor")] int Id,
        string? No,
        DateTime Date,
        decimal Amount,
        [property: PublicId("ba")] int BankId,
        string BankName,
        string? ReferenceNo,
        string? EvidenceReference,
        int Status,
        string StatusLabel,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
        long Version);

    public sealed record ReceivableWriteOffDto(
        [property: PublicId("wo")] int Id,
        string? No,
        [property: PublicId("cus")] int CustomerId,
        string CustomerName,
        [property: PublicId("inv")] int SalesInvoiceHeaderId,
        string InvoiceNo,
        DateTime Date,
        string CurrencyCode,
        decimal Amount,
        decimal AllowanceAllocatedAmount,
        decimal ImpairmentShortfallAmount,
        decimal RecoveredAmount,
        string Reason,
        string CollectionEvidence,
        int Status,
        string StatusLabel,
        [property: PublicId("gl")] int? GeneralLedgerHeaderId,
        [property: PublicId("gl")] int? VoidGeneralLedgerHeaderId,
        string? SubmittedBy,
        string? ReviewedBy,
        string? PostedBy,
        long Version,
        IReadOnlyList<WriteOffRecoveryDto> Recoveries);
}
