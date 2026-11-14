// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain.TaxSystem;

namespace Services.TaxSystem
{
    public interface ITaxFilingService
    {
        TaxFilingDto Record(RecordTaxFilingRequest request);
        IReadOnlyList<TaxFilingDto> List();
        IReadOnlyList<TaxFilingDto> ForPeriod(DateTime periodStart, DateTime periodEnd);
        TaxChangedSinceFilingDto ChangedSinceFiling(TaxFiling filing);
        TaxChangedSinceFilingDto? ChangedSinceFilingForPeriod(DateTime periodStart, DateTime periodEnd);
        IReadOnlyDictionary<(string Start, string End), TaxPeriodFilingSummaryDto> GetPeriodSummaries(
            IEnumerable<(DateTime Start, DateTime End)> periods);
    }

    public sealed class TaxFilingValidationException : Exception
    {
        public string Code { get; }

        public TaxFilingValidationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public sealed class TaxFilingConflictException : Exception
    {
        public string Code { get; }

        public TaxFilingConflictException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public sealed class TaxFilingForbiddenException : Exception
    {
        public string Code { get; }

        public TaxFilingForbiddenException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public sealed record RecordTaxFilingRequest(
        DateTime PeriodStart,
        DateTime PeriodEnd,
        DateTime FiledOn,
        string? Reference,
        string? Notes,
        bool LockBooks,
        decimal ExpectedNetTax,
        int? AmendsFilingId,
        string? AmendmentReason,
        string Actor);

    public sealed record TaxFilingDto(
        int Id,
        string PeriodStart,
        string PeriodEnd,
        string PeriodLabel,
        string Frequency,
        string Basis,
        int Sequence,
        int? AmendsFilingId,
        string? AmendmentReason,
        string FiledOn,
        string? Reference,
        string? Notes,
        decimal OutputTax,
        decimal InputTax,
        decimal NetTax,
        string CreatedBy,
        string CreatedAt);

    public sealed record TaxPeriodFilingSummaryDto(
        int Id,
        int Sequence,
        string FiledOn,
        string? Reference,
        decimal NetTax,
        TaxChangedSinceFilingDto ChangedSinceFiling);

    public sealed record TaxFilingDiffRowDto(
        string Side,
        int? TaxId,
        string TaxCode,
        decimal Filed,
        decimal Current,
        decimal Difference);

    public sealed record TaxChangedSinceFilingDto(
        IReadOnlyList<TaxFilingDiffRowDto> Rows,
        decimal NetDifference,
        bool Changed);
}
