// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain;

namespace Services.TaxSystem
{
    public interface ITaxPeriodReportService
    {
        TaxPeriodReport Build(DateTime from, DateTime to);
        TaxPeriodReport Build(DateTime from, DateTime to, int? documentLimit);
        TaxPeriodReport Build(DateTime from, DateTime to, int? documentLimit, AccountingBasis basis);
        IReadOnlyList<TaxPeriodSummaryItem> GetRecentPeriods(int count);
        TaxPeriodSettingsDto GetSettings();
        TaxPeriodSettingsDto UpdateSettings(TaxPeriodFrequency frequency, int? quarterCycleStartMonth);
    }

    public sealed class TaxPeriodReportValidationException : Exception
    {
        public string Code { get; }

        public TaxPeriodReportValidationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public sealed class TaxPeriodReportConflictException : Exception
    {
        public string Code { get; }

        public TaxPeriodReportConflictException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public sealed record TaxPeriodSettingsDto(
        string Frequency,
        int? QuarterCycleStartMonth,
        int EffectiveQuarterCycleStartMonth,
        int DefaultQuarterCycleStartMonth);

    public sealed record TaxPeriodSummaryItem(
        string Start,
        string End,
        string Label,
        string State,
        string? Lock,
        object? Filing);

    public sealed record TaxPeriodReport(
        string From,
        string To,
        string Basis,
        string Currency,
        bool IsWholePeriod,
        string? PeriodLabel,
        TaxPeriodSideSummary Output,
        TaxPeriodSideSummary Input,
        decimal NetTax,
        IReadOnlyList<TaxPeriodDocumentRow> Documents,
        bool DocumentsTruncated,
        TaxPeriodWarnings Warnings,
        TaxPeriodLedgerCheck? Ledger,
        TaxPeriodLockStatus? Lock);

    public sealed record TaxPeriodSideSummary(
        IReadOnlyList<TaxPeriodTaxRow> Rows,
        decimal Taxable,
        decimal Tax);

    public sealed record TaxPeriodTaxRow(
        int? TaxId,
        string TaxCode,
        string TaxName,
        decimal? Rate,
        decimal Taxable,
        decimal Tax,
        int DocumentCount);

    public sealed record TaxPeriodDocumentRow(
        string Side,
        string Kind,
        int Id,
        string No,
        string Date,
        string Party,
        int? TaxId,
        decimal Taxable,
        decimal Tax,
        bool Posted);

    public sealed record TaxPeriodWarnings(
        IReadOnlyList<TaxPeriodUnpostedWarning> Unposted);

    public sealed record TaxPeriodUnpostedWarning(
        string Kind,
        int Id,
        string No,
        string Date);

    public sealed record TaxPeriodLedgerCheck(
        IReadOnlyList<TaxPeriodLedgerAccount> Accounts);

    public sealed record TaxPeriodLedgerAccount(
        int AccountId,
        string Code,
        string Name,
        string PositiveSide,
        decimal Movement,
        decimal FromDocuments,
        decimal Other,
        IReadOnlyList<TaxPeriodLedgerOtherEntry> Entries);

    public sealed record TaxPeriodLedgerOtherEntry(
        int GlHeaderId,
        string Date,
        string Description,
        string DocumentType,
        decimal Amount);

    public sealed record TaxPeriodLockStatus(
        string? LockedThrough,
        string State,
        bool CanLock);
}
