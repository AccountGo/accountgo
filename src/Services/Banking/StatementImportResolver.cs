// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
                "completed", "posted", "booked", "settled"
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
            var needsChoice = detection.Ambiguous && userFormat == null;

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
                NeedsDateFormatChoice = needsChoice,
                AmountColumn = map.AmountColumn,
                DepositColumn = map.DepositColumn,
                WithdrawalColumn = map.WithdrawalColumn,
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
                DateDetection = detection
            };

            var amountSummary = DescribeAmount(map);
            var currencySummary = map.CurrencyColumn.HasValue
                ? (accountCurrency == null
                    ? "currency column mapped (no account currency set)"
                    : $"{accountCurrency} only")
                : null;
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
                    Date = needsChoice
                        ? "ambiguous (dd/MM vs MM/dd) — choose a format"
                        : detection.Summary,
                    Amount = amountSummary,
                    Currency = currencySummary,
                    Status = statusSummary,
                    Header = headerSummary,
                    NeedsDateFormatChoice = needsChoice
                }
            };
        }

        private static string DescribeAmount(BankStatementColumnMap map)
        {
            if (map.AmountColumn.HasValue && map.DirectionColumn.HasValue)
                return "unsigned + direction";
            if (map.AmountColumn.HasValue)
                return "signed amount";
            if (map.DepositColumn.HasValue || map.WithdrawalColumn.HasValue)
                return "deposit / withdrawal columns";
            return "unmapped";
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

        private static char ResolveDelimiter(string content, string? requested)
        {
            if (!string.IsNullOrEmpty(requested))
                return requested == "\\t" || requested == "\t" ? '\t' : requested[0];

            var probeLine = FirstNonBlankLine(content);
            var best = ',';
            var bestCount = 0;
            foreach (var candidate in new[] { ',', ';', '\t' })
            {
                var count = 0;
                var inQuotes = false;
                foreach (var ch in probeLine)
                {
                    if (ch == '"') inQuotes = !inQuotes;
                    else if (ch == candidate && !inQuotes) count++;
                }
                if (count > bestCount)
                {
                    best = candidate;
                    bestCount = count;
                }
            }
            return best;
        }

        private static string FirstNonBlankLine(string content)
        {
            var start = 0;
            var length = content.Length;
            while (start < length)
            {
                var end = content.IndexOfAny(new[] { '\r', '\n' }, start);
                if (end < 0)
                    end = length;
                var line = content[start..end];
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
                start = end;
                if (start < length && content[start] == '\r')
                    start++;
                if (start < length && content[start] == '\n')
                    start++;
            }
            return string.Empty;
        }
    }
}
