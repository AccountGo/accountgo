// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Services.Banking
{
    /// <summary>Quicken Interchange Format (!Type:Bank) reader (#447 Phase C).</summary>
    public static class QifStatementParser
    {
        /// <summary>Whole-file date detection from every D field (never guess from a single row).</summary>
        public static StatementColumnDetector.DateDetection DetectDates(string content)
        {
            content = content.TrimStart('\uFEFF');
            var dateRaw = new List<string>();
            foreach (var rawLine in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.TrimEnd();
                if (line.Length > 1 && line[0] == 'D')
                    dateRaw.Add(NormalizeQifDateToken(line[1..].Trim()));
            }
            return StatementColumnDetector.DetectDateFormat(dateRaw);
        }

        /// <param name="dateFormat">
        /// User-chosen format such as <c>dd/MM/yyyy</c> or <c>MM/dd/yyyy</c>. Required when
        /// <see cref="DetectDates"/> is ambiguous — without it every slash date becomes an error row.
        /// </param>
        public static IReadOnlyList<ParsedStatementRow> Parse(string content, string? dateFormat = null)
        {
            content = content.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");

            var dateDetection = DetectDates(content);
            var userFormat = string.IsNullOrWhiteSpace(dateFormat) ? null : dateFormat.Trim();
            var resolvedFormat = userFormat ?? (dateDetection.Ambiguous ? null : dateDetection.Format);
            var needsDateChoice = userFormat == null
                && (dateDetection.Ambiguous
                    || (dateDetection.Kind == StatementColumnDetector.DateKind.Unknown
                        && CollectDateTokens(content).Any(LooksNumericSlash)));

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            DateTime? date = null;
            string? dateError = null;
            decimal? amount = null;
            string? payee = null;
            string? memo = null;
            string? reference = null;
            var number = 0;

            void Flush()
            {
                if (date == null && dateError == null && amount == null && payee == null && memo == null)
                    return;
                number++;
                if (rows.Count >= BankStatementParser.MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {BankStatementParser.MaxRows} rows.");
                var row = new ParsedStatementRow { RowNumber = number };
                try
                {
                    if (dateError != null)
                        throw new FormatException(dateError);
                    if (!date.HasValue)
                        throw new FormatException("Date is missing.");
                    if (!amount.HasValue || amount.Value == 0m)
                        throw new FormatException(amount.HasValue ? "Amount is zero." : "Amount is missing.");
                    row.PostedDate = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc);
                    row.Amount = Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
                    row.Payee = Truncate(payee, BankStatementParser.MaxPayeeLength);
                    row.Memo = Truncate(memo, BankStatementParser.MaxMemoLength);
                    row.Reference = Truncate(reference, BankStatementParser.MaxReferenceLength);
                    row.ExternalId = SyntheticId(row, occurrences);
                }
                catch (FormatException ex)
                {
                    row.Error = ex.Message;
                }
                rows.Add(row);
                date = null;
                dateError = null;
                amount = null;
                payee = null;
                memo = null;
                reference = null;
            }

            foreach (var rawLine in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0 || line.StartsWith('!'))
                    continue;
                if (line == "^")
                {
                    Flush();
                    continue;
                }
                var code = line[0];
                var value = line.Length > 1 ? line[1..].Trim() : string.Empty;
                switch (code)
                {
                    case 'D':
                        try
                        {
                            var token = NormalizeQifDateToken(value);
                            if (needsDateChoice && LooksNumericSlash(token))
                                throw new FormatException(
                                    $"Date '{value}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.");
                            date = ParseQifDate(token, resolvedFormat, dateDetection);
                            dateError = null;
                        }
                        catch (FormatException ex)
                        {
                            date = null;
                            dateError = ex.Message;
                        }
                        break;
                    case 'T':
                    case 'U':
                        amount = BankStatementParser.ParseAmount(value, "Amount");
                        break;
                    case 'P':
                        payee = value;
                        break;
                    case 'M':
                        memo = value;
                        break;
                    case 'N':
                        reference = value;
                        break;
                }
            }
            Flush();

            if (rows.Count == 0)
                throw new InvalidOperationException("No transactions were found in the QIF file.");
            return rows;
        }

        private static IReadOnlyList<string> CollectDateTokens(string content)
        {
            var list = new List<string>();
            foreach (var rawLine in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = rawLine.TrimEnd();
                if (line.Length > 1 && line[0] == 'D')
                    list.Add(NormalizeQifDateToken(line[1..].Trim()));
            }
            return list;
        }

        private static DateTime ParseQifDate(
            string normalized,
            string? resolvedFormat,
            StatementColumnDetector.DateDetection detection)
        {
            if (detection.Kind == StatementColumnDetector.DateKind.Iso8601
                || (normalized.Length >= 10 && normalized[4] == '-' && normalized[7] == '-'))
            {
                if (DateTime.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
                    return iso;
            }

            if (!string.IsNullOrWhiteSpace(resolvedFormat))
            {
                var formats = Expand(resolvedFormat);
                if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                    return exact;
                throw new FormatException($"Date '{normalized}' does not match the format '{resolvedFormat}'.");
            }

            if (detection.Kind == StatementColumnDetector.DateKind.MonthName)
            {
                foreach (var f in StatementColumnDetector.MonthNameExactFormats)
                {
                    if (DateTime.TryParseExact(normalized, f, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var named))
                        return named;
                }
            }

            throw new FormatException(
                $"Date '{normalized}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.");
        }

        private static string[] Expand(string format)
        {
            var trimmed = format.Trim();
            if (trimmed.StartsWith('d') || trimmed.StartsWith('D'))
                return new[] { trimmed, "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "d-M-yyyy", "dd-MM-yyyy" };
            if (trimmed.StartsWith('M'))
                return new[] { trimmed, "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy", "M-d-yyyy", "MM-dd-yyyy" };
            return new[] { trimmed };
        }

        private static string NormalizeQifDateToken(string raw) => raw.Replace('\'', '/').Trim();

        private static bool LooksNumericSlash(string value)
        {
            var parts = value.Split('/', '-', '.');
            return parts.Length == 3 && parts.All(p => p.Length > 0 && p.All(char.IsDigit));
        }

        private static string SyntheticId(ParsedStatementRow row, Dictionary<string, int> occurrences)
        {
            var key = string.Join('|',
                row.PostedDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Amount!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                (row.Payee ?? string.Empty).ToLowerInvariant(),
                (row.Memo ?? string.Empty).ToLowerInvariant(),
                (row.Reference ?? string.Empty).ToLowerInvariant());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
            occurrences.TryGetValue(hash, out var seen);
            occurrences[hash] = ++seen;
            return seen == 1 ? $"qif:{hash}" : $"qif:{hash}#{seen}";
        }

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
