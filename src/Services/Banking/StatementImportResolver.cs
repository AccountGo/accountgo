// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Turns a user <see cref="BankStatementColumnMap"/> plus the file into a
    /// <see cref="ResolvedStatementMap"/> the parser can apply without further detection (#447).
    /// </summary>
    public static class StatementImportResolver
    {
        public static readonly IReadOnlyDictionary<string, int> DefaultDirectionSigns =
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["in"] = 1,
                ["credit"] = 1,
                ["cr"] = 1,
                ["deposit"] = 1,
                ["incoming"] = 1,
                ["out"] = -1,
                ["debit"] = -1,
                ["dr"] = -1,
                ["withdrawal"] = -1,
                ["payment"] = -1,
                ["outgoing"] = -1
            });

        public static readonly IReadOnlySet<string> DefaultAllowedStatuses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "completed", "posted", "booked", "settled", "succeeded", "paid", "cleared"
            };

        public sealed class ResolveResult
        {
            public required ResolvedStatementMap Map { get; init; }
            public required StatementDetectionSummary Summary { get; init; }
            public IReadOnlyList<IReadOnlyList<string>> Records { get; init; } = Array.Empty<IReadOnlyList<string>>();
        }

        public static ResolveResult Resolve(
            string content,
            BankStatementColumnMap map,
            string? accountCurrencyCode = null)
        {
            ArgumentNullException.ThrowIfNull(map);
            content = content.TrimStart('\uFEFF');
            var delimiter = ResolveDelimiter(content, map.Delimiter);
            var records = BankStatementParser.SplitCsvRecords(content, delimiter);

            int? headerIndex = null;
            if (map.HasHeaderRow)
            {
                headerIndex = map.HeaderRowIndex
                    ?? StatementColumnDetector.DetectHeaderRow(records)
                    ?? FirstNonBlankIndex(records);
            }

            var dataStart = headerIndex.HasValue ? headerIndex.Value + 1 : 0;
            var dateSamples = records
                .Skip(dataStart)
                .Select(r => Cell(r, map.DateColumn))
                .ToList();

            var detection = StatementColumnDetector.DetectDateFormat(dateSamples);
            var userFormat = string.IsNullOrWhiteSpace(map.DateFormat) ? null : map.DateFormat.Trim();
            var needsDateChoice = detection.Ambiguous && userFormat == null;

            StatementDateMode dateMode;
            string? dateFormat = userFormat;
            if (userFormat != null)
            {
                dateMode = StatementDateMode.ExactFormat;
            }
            else
            {
                (dateMode, dateFormat) = detection.Kind switch
                {
                    StatementColumnDetector.DateKind.Iso8601 => (StatementDateMode.Iso8601, null),
                    StatementColumnDetector.DateKind.MonthName => (StatementDateMode.MonthName, null),
                    StatementColumnDetector.DateKind.ExcelSerial => (StatementDateMode.ExcelSerial, null),
                    StatementColumnDetector.DateKind.EpochSeconds => (StatementDateMode.EpochSeconds, null),
                    StatementColumnDetector.DateKind.EpochMilliseconds => (StatementDateMode.EpochMilliseconds, null),
                    StatementColumnDetector.DateKind.YearFirst => (StatementDateMode.UnambiguousBuiltIn, null),
                    StatementColumnDetector.DateKind.DayFirstNumeric
                        or StatementColumnDetector.DateKind.MonthFirstNumeric
                        or StatementColumnDetector.DateKind.CompactYmd
                        when detection.Format != null => (StatementDateMode.ExactFormat, detection.Format),
                    StatementColumnDetector.DateKind.AmbiguousNumeric => (StatementDateMode.ExactFormat, null),
                    _ => (StatementDateMode.UnambiguousBuiltIn, null)
                };
            }

            var amountSamples = CollectAmountSamples(records, dataStart, map);
            var amountDetection = StatementColumnDetector.DetectAmountFormat(amountSamples);
            var userDecimal = ParseUserDecimalSeparator(map.DecimalSeparator);
            var needsAmountChoice = amountDetection.Ambiguous && userDecimal == null;
            var amountDecimal = userDecimal ?? amountDetection.DecimalSeparator;

            var directionSigns = BuildDirectionSigns(map);
            var allowedStatuses = BuildAllowedStatuses(map);
            var accountCurrency = string.IsNullOrWhiteSpace(accountCurrencyCode)
                ? null
                : accountCurrencyCode.Trim().ToUpperInvariant();

            var resolved = new ResolvedStatementMap
            {
                HeaderRowIndex = headerIndex,
                Delimiter = delimiter,
                DateColumn = map.DateColumn,
                DateMode = dateMode,
                DateFormat = dateFormat,
                NeedsDateFormatChoice = needsDateChoice,
                AmountColumn = map.AmountColumn,
                DepositColumn = map.DepositColumn,
                WithdrawalColumn = map.WithdrawalColumn,
                FeeColumn = map.FeeColumn,
                DirectionColumn = map.DirectionColumn,
                DirectionSigns = directionSigns,
                PayeeColumn = map.PayeeColumn,
                MemoColumn = map.MemoColumn,
                ReferenceColumn = map.ReferenceColumn,
                ExternalIdColumn = map.ExternalIdColumn,
                CurrencyColumn = map.CurrencyColumn,
                AccountCurrencyCode = accountCurrency,
                StatusColumn = map.StatusColumn,
                AllowedStatuses = allowedStatuses,
                InvertSign = map.InvertSign,
                DateDetection = detection,
                AmountDetection = amountDetection,
                AmountDecimalSeparator = amountDecimal,
                NeedsAmountFormatChoice = needsAmountChoice
            };

            var amountSummary = needsAmountChoice
                ? "ambiguous (. vs , decimal) — choose a separator"
                : DescribeAmount(map, amountDetection);
            var currencySummary = map.CurrencyColumn.HasValue
                ? (accountCurrency == null
                    ? "currency column mapped (no account currency set)"
                    : $"{accountCurrency} only")
                : (accountCurrency != null ? $"{accountCurrency} (codes in amount checked)" : null);
            var statusSummary = map.StatusColumn.HasValue
                ? $"import {string.Join('/', allowedStatuses.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}"
                : null;
            var headerSummary = headerIndex.HasValue
                ? $"row {headerIndex.Value + 1}"
                : "none";

            return new ResolveResult
            {
                Map = resolved,
                Records = records,
                Summary = new StatementDetectionSummary
                {
                    Date = needsDateChoice
                        ? "ambiguous (dd/MM vs MM/dd) — choose a format"
                        : detection.Summary,
                    Amount = amountSummary,
                    Currency = currencySummary,
                    Status = statusSummary,
                    Header = headerSummary,
                    NeedsDateFormatChoice = needsDateChoice,
                    NeedsAmountFormatChoice = needsAmountChoice
                }
            };
        }

        private static IReadOnlyList<string?> CollectAmountSamples(
            IReadOnlyList<IReadOnlyList<string>> records,
            int dataStart,
            BankStatementColumnMap map)
        {
            var list = new List<string?>();
            foreach (var row in records.Skip(dataStart))
            {
                if (map.AmountColumn.HasValue)
                    list.Add(Cell(row, map.AmountColumn.Value));
                if (map.DepositColumn.HasValue)
                    list.Add(Cell(row, map.DepositColumn.Value));
                if (map.WithdrawalColumn.HasValue)
                    list.Add(Cell(row, map.WithdrawalColumn.Value));
                if (map.FeeColumn.HasValue)
                    list.Add(Cell(row, map.FeeColumn.Value));
            }
            return list;
        }

        private static char? ParseUserDecimalSeparator(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            if (trimmed is "." or ",")
                return trimmed[0];
            throw new InvalidOperationException("Decimal separator must be '.' or ','.");
        }

        private static string DescribeAmount(
            BankStatementColumnMap map,
            StatementColumnDetector.AmountDetection amountDetection)
        {
            var shape = map.AmountColumn.HasValue && map.DirectionColumn.HasValue
                ? "unsigned + direction"
                : map.AmountColumn.HasValue
                    ? "signed amount"
                    : map.DepositColumn.HasValue || map.WithdrawalColumn.HasValue
                        ? "deposit / withdrawal columns"
                        : "unmapped";
            if (amountDetection.Kind is StatementColumnDetector.AmountDecimalKind.Unknown
                or StatementColumnDetector.AmountDecimalKind.Ambiguous)
                return shape;
            return $"{shape} · {amountDetection.Summary}";
        }

        private static IReadOnlyDictionary<string, int> BuildDirectionSigns(BankStatementColumnMap map)
        {
            if (map.DirectionValueMap == null || map.DirectionValueMap.Count == 0)
                return DefaultDirectionSigns;

            var built = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in DefaultDirectionSigns)
                built[pair.Key] = pair.Value;
            foreach (var pair in map.DirectionValueMap)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                built[pair.Key.Trim()] = pair.Value >= 0 ? 1 : -1;
            }
            return new ReadOnlyDictionary<string, int>(built);
        }

        private static IReadOnlySet<string> BuildAllowedStatuses(BankStatementColumnMap map)
        {
            if (map.AllowedStatuses == null || map.AllowedStatuses.Count == 0)
                return DefaultAllowedStatuses;
            return new HashSet<string>(
                map.AllowedStatuses.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private static int? FirstNonBlankIndex(IReadOnlyList<IReadOnlyList<string>> records)
        {
            for (var i = 0; i < records.Count; i++)
            {
                if (records[i].Any(c => !string.IsNullOrWhiteSpace(c)))
                    return i;
            }
            return null;
        }

        private static string? Cell(IReadOnlyList<string> row, int column)
        {
            if (column < 0 || column >= row.Count)
                return null;
            var value = row[column].Trim();
            return value.Length == 0 ? null : value;
        }

        /// <summary>
        /// Prefer the delimiter that yields the strongest header-like row (not the first non-blank
        /// preamble line).
        /// </summary>
        private static char ResolveDelimiter(string content, string? requested)
        {
            if (!string.IsNullOrEmpty(requested))
                return requested == "\\t" || requested == "\t" ? '\t' : requested[0];

            var best = ',';
            var bestScore = -1;
            foreach (var candidate in new[] { ',', ';', '\t' })
            {
                var records = BankStatementParser.SplitCsvRecords(content, candidate);
                var headerIndex = StatementColumnDetector.DetectHeaderRow(records);
                int score;
                if (headerIndex.HasValue)
                {
                    var header = records[headerIndex.Value];
                    var nonBlank = header.Count(c => !string.IsNullOrWhiteSpace(c));
                    // Prefer a wide detected header over preamble lines.
                    score = 1_000_000 + nonBlank;
                }
                else
                {
                    var first = FirstNonBlankIndex(records);
                    score = first.HasValue
                        ? records[first.Value].Count(c => !string.IsNullOrWhiteSpace(c))
                        : 0;
                }
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }
    }
}
