// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Services.Banking
{
    /// <summary>
    /// Pure column-format detection for statement CSV import (#447 Phase A).
    /// Looks at a whole column at once; never picks day/month order from a single ambiguous row.
    /// </summary>
    public static class StatementColumnDetector
    {
        public enum DateKind
        {
            Unknown,
            Iso8601,
            YearFirst,
            MonthName,
            CompactYmd,
            DayFirstNumeric,
            MonthFirstNumeric,
            ExcelSerial,
            EpochSeconds,
            EpochMilliseconds,
            AmbiguousNumeric
        }

        public sealed class DateDetection
        {
            public DateKind Kind { get; init; }
            /// <summary>.NET format when ExactFormat applies (day/month-first, year-first, compact).</summary>
            public string? Format { get; init; }
            public bool Ambiguous => Kind == DateKind.AmbiguousNumeric;
            public string Summary { get; init; } = "unknown";
            public int SampleCount { get; init; }
        }

        private static readonly Regex IsoLike = new(
            @"^\d{4}-\d{2}-\d{2}([Tt ]\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?)?(Z|[+-]\d{2}:?\d{2})?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private static readonly Regex CompactYmd = new(
            @"^\d{8}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private static readonly Regex NumericSlash = new(
            @"^(?<a>\d{1,2})[/\-.](?<b>\d{1,2})[/\-.](?<y>\d{2}|\d{4})$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private static readonly string[] MonthNameFormats =
        {
            "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy",
            "MMM d, yyyy", "MMMM d, yyyy", "MMM d yyyy", "d-MMM-yyyy", "dd-MMM-yy"
        };

        private static readonly HashSet<string> KnownHeaderTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "date", "posted", "posted_date", "created", "created_on", "completed", "finished",
            "finished_on", "time", "amount", "signed_amount", "value", "deposit", "withdrawal",
            "debit", "credit", "direction", "type", "payee", "description", "memo", "narrative",
            "details", "reference", "ref", "id", "activity_id", "transaction_id", "fitid",
            "currency", "status", "state"
        };

        /// <summary>Decide the date format for a whole column from its non-blank values.</summary>
        public static DateDetection DetectDateFormat(IEnumerable<string?> values)
        {
            var samples = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Take(500)
                .ToList();
            if (samples.Count == 0)
                return new DateDetection { Kind = DateKind.Unknown, Summary = "no dates", SampleCount = 0 };

            if (samples.All(v => IsoLike.IsMatch(v)))
                return new DateDetection
                {
                    Kind = DateKind.Iso8601,
                    Summary = "ISO 8601",
                    SampleCount = samples.Count
                };

            if (samples.All(v => CompactYmd.IsMatch(v) && LooksLikeYmd(v)))
                return new DateDetection
                {
                    Kind = DateKind.CompactYmd,
                    Format = "yyyyMMdd",
                    Summary = "yyyyMMdd",
                    SampleCount = samples.Count
                };

            if (samples.All(IsMonthNameDate))
                return new DateDetection
                {
                    Kind = DateKind.MonthName,
                    Summary = "month name",
                    SampleCount = samples.Count
                };

            if (samples.All(v => LooksLikeExcelSerial(v, out _)))
                return new DateDetection
                {
                    Kind = DateKind.ExcelSerial,
                    Summary = "Excel serial",
                    SampleCount = samples.Count
                };

            if (samples.All(v => LooksLikeEpoch(v, out _)) && samples.Count > 0)
            {
                var allMs = samples.All(v => LooksLikeEpoch(v, out var ms) && ms);
                return new DateDetection
                {
                    Kind = allMs ? DateKind.EpochMilliseconds : DateKind.EpochSeconds,
                    Summary = allMs ? "Unix epoch (ms)" : "Unix epoch (s)",
                    SampleCount = samples.Count
                };
            }

            var slash = samples.Select(v => NumericSlash.Match(v)).Where(m => m.Success).ToList();
            if (slash.Count == samples.Count && slash.Count > 0)
            {
                var anyDayFirst = slash.Any(m => int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture) > 12);
                var anyMonthFirst = slash.Any(m => int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) > 12);
                if (anyDayFirst && !anyMonthFirst)
                    return new DateDetection
                    {
                        Kind = DateKind.DayFirstNumeric,
                        Format = "d/M/yyyy",
                        Summary = "dd/MM/yyyy",
                        SampleCount = samples.Count
                    };
                if (anyMonthFirst && !anyDayFirst)
                    return new DateDetection
                    {
                        Kind = DateKind.MonthFirstNumeric,
                        Format = "M/d/yyyy",
                        Summary = "MM/dd/yyyy",
                        SampleCount = samples.Count
                    };
                return new DateDetection
                {
                    Kind = DateKind.AmbiguousNumeric,
                    Summary = "ambiguous (dd/MM vs MM/dd)",
                    SampleCount = samples.Count
                };
            }

            if (samples.All(v =>
                    DateTime.TryParseExact(v, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                return new DateDetection
                {
                    Kind = DateKind.YearFirst,
                    Format = "yyyy-MM-dd",
                    Summary = "yyyy-MM-dd",
                    SampleCount = samples.Count
                };

            return new DateDetection { Kind = DateKind.Unknown, Summary = "unrecognised", SampleCount = samples.Count };
        }

        /// <summary>
        /// First record index that looks like a header: mostly non-numeric cells and at least one
        /// known column name. Returns null when nothing matches.
        /// </summary>
        public static int? DetectHeaderRow(IReadOnlyList<IReadOnlyList<string>> records)
        {
            for (var i = 0; i < Math.Min(records.Count, 30); i++)
            {
                var row = records[i];
                if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
                    continue;
                var nonBlank = row.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                // Bank-info preambles are usually 1–2 cells ("Currency,USD"); real headers are wider.
                if (nonBlank.Count < 3)
                    continue;
                var numericish = nonBlank.Count(c => LooksMostlyNumeric(c));
                if (numericish > nonBlank.Count / 2)
                    continue;
                var knownCount = nonBlank.Count(c => KnownHeaderTokens.Contains(NormalizeHeaderToken(c)));
                if (knownCount >= 2)
                    return i;
            }
            return null;
        }

        public static string NormalizeHeaderToken(string value)
        {
            var trimmed = value.Trim().ToLowerInvariant();
            var cleaned = Regex.Replace(trimmed, @"[^a-z0-9]+", "_", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            return cleaned.Trim('_');
        }

        internal static IReadOnlyList<string> MonthNameExactFormats => MonthNameFormats;

        private static bool LooksLikeYmd(string v)
        {
            if (v.Length != 8 || !int.TryParse(v[..4], out var y) || y is < 1990 or > 2100)
                return false;
            if (!int.TryParse(v.Substring(4, 2), out var m) || m is < 1 or > 12)
                return false;
            return int.TryParse(v.Substring(6, 2), out var d) && d is >= 1 and <= 31;
        }

        private static bool IsMonthNameDate(string v) =>
            MonthNameFormats.Any(f =>
                DateTime.TryParseExact(v, f, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _));

        private static bool LooksLikeExcelSerial(string v, out double serial)
        {
            serial = 0;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out serial))
                return false;
            // Excel day serials roughly 1990-01-01 .. 2100-01-01.
            return serial is >= 32874 and <= 73050;
        }

        private static bool LooksLikeEpoch(string v, out bool milliseconds)
        {
            milliseconds = false;
            if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return false;
            if (v.Length == 13 && n is >= 946684800000L and <= 4102444800000L)
            {
                milliseconds = true;
                return true;
            }
            if (v.Length == 10 && n is >= 946684800L and <= 4102444800L)
                return true;
            return false;
        }

        private static bool LooksMostlyNumeric(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return false;
            var digits = trimmed.Count(char.IsDigit);
            return digits >= trimmed.Length / 2;
        }
    }
}
