// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Services.Banking
{
    public enum StatementDateMode
    {
        /// <summary>Parse with <see cref="ResolvedStatementMap.DateFormat"/> only.</summary>
        ExactFormat = 1,
        Iso8601 = 2,
        MonthName = 3,
        ExcelSerial = 4,
        EpochSeconds = 5,
        EpochMilliseconds = 6,
        /// <summary>Year-first / compact / month-name style formats that need no user choice.</summary>
        UnambiguousBuiltIn = 7
    }

    /// <summary>
    /// Fully resolved CSV mapping for <see cref="BankStatementParser"/>. Built by
    /// <see cref="StatementImportResolver"/> from the user map plus column detection — the parser
    /// never detects formats itself.
    /// </summary>
    public sealed class ResolvedStatementMap
    {
        /// <summary>Absolute record index of the header row, or null when there is no header.</summary>
        public int? HeaderRowIndex { get; init; }
        public char Delimiter { get; init; } = ',';
        public int DateColumn { get; init; }
        public StatementDateMode DateMode { get; init; } = StatementDateMode.UnambiguousBuiltIn;
        public string? DateFormat { get; init; }
        /// <summary>True when the date column is ambiguous and the user has not chosen a format.</summary>
        public bool NeedsDateFormatChoice { get; init; }
        public int? AmountColumn { get; init; }
        public int? DepositColumn { get; init; }
        public int? WithdrawalColumn { get; init; }
        public int? DirectionColumn { get; init; }
        public IReadOnlyDictionary<string, int> DirectionSigns { get; init; } =
            StatementImportResolver.DefaultDirectionSigns;
        public int? PayeeColumn { get; init; }
        public int? MemoColumn { get; init; }
        public int? ReferenceColumn { get; init; }
        public int? ExternalIdColumn { get; init; }
        public int? CurrencyColumn { get; init; }
        /// <summary>Bank account currency (ISO). Rows with a different currency become error rows.</summary>
        public string? AccountCurrencyCode { get; init; }
        public int? StatusColumn { get; init; }
        public IReadOnlySet<string> AllowedStatuses { get; init; } =
            StatementImportResolver.DefaultAllowedStatuses;
        public bool InvertSign { get; init; }
        public StatementColumnDetector.DateDetection? DateDetection { get; init; }
        public StatementColumnDetector.AmountDetection? AmountDetection { get; init; }
        /// <summary>`.` or `,` when resolved; null → per-value grouping rules (OFX-style).</summary>
        public char? AmountDecimalSeparator { get; init; }
        /// <summary>True when the amount column is ambiguous and the user has not chosen a separator.</summary>
        public bool NeedsAmountFormatChoice { get; init; }
        public int? FeeColumn { get; init; }
    }

    /// <summary>Human-readable detection notes for the import preview.</summary>
    public sealed class StatementDetectionSummary
    {
        public string? Date { get; init; }
        public string? Amount { get; init; }
        public string? Currency { get; init; }
        public string? Status { get; init; }
        public string? Header { get; init; }
        public bool NeedsDateFormatChoice { get; init; }
        public bool NeedsAmountFormatChoice { get; init; }
    }
}
