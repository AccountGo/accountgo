// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Core.Domain;
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>One row read from a statement file, before it is checked against the database.</summary>
    public sealed class ParsedStatementRow
    {
        public int RowNumber { get; init; }
        public DateTime? PostedDate { get; set; }
        public decimal? Amount { get; set; }
        public string? Payee { get; set; }
        public string? Memo { get; set; }
        public string? Reference { get; set; }
        public string? ExternalId { get; set; }
        public string? Error { get; set; }
        public bool IsValid => Error == null && PostedDate.HasValue && Amount.HasValue;
    }

    /// <summary>CSV and OFX/QFX statement readers. No external dependencies; never throws for bad rows.</summary>
    public static class BankStatementParser
    {
        public const int MaxContentChars = 4_000_000;
        public const int MaxRows = 5000;
        public const int MaxPayeeLength = 200;
        public const int MaxMemoLength = 500;
        public const int MaxReferenceLength = 100;
        public const int MaxExternalIdLength = 200;
        private const int SyntheticHashLength = 32;

        /// <summary>
        /// Formats that read the same way everywhere (year first, or a month name). Numeric day/month orders such as
        /// 03/04/2026 are never guessed: the column map must carry the format, or the row is refused.
        /// </summary>
        private static readonly string[] UnambiguousDateFormats =
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "d-MMM-yyyy", "dd-MMM-yy",
            "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyyMMdd"
        };

        private const int MaxDecimalCommaFractionDigits = 2;

        private static readonly char[] CandidateDelimiters = { ',', ';', '\t' };

        public static IReadOnlyList<ParsedStatementRow> Parse(BankStatementSources source, string content, BankStatementColumnMap? map)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");
            if (content.Length > MaxContentChars)
                throw new InvalidOperationException("The statement file is too large.");

            return source switch
            {
                BankStatementSources.Csv => ParseCsv(content, map ?? throw new InvalidOperationException("Map the CSV columns first.")),
                BankStatementSources.Ofx => ParseOfx(content),
                _ => throw new InvalidOperationException("Pick a CSV or OFX file.")
            };
        }

        // ---------------------------------------------------------------- CSV

        public static IReadOnlyList<ParsedStatementRow> ParseCsv(string content, BankStatementColumnMap map)
        {
            ValidateMap(map);
            content = content.TrimStart('\uFEFF');

            var delimiter = ResolveDelimiter(content, map.Delimiter);
            var records = SplitCsv(content, delimiter);

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var recordNumber = 0;
            var headerSkipped = !map.HasHeaderRow;
            foreach (var fields in records)
            {
                recordNumber++;
                // Leading blank lines (and blanks between rows) are never the header or data.
                if (IsBlankRecord(fields))
                    continue;
                if (!headerSkipped)
                {
                    headerSkipped = true;
                    continue;
                }

                if (rows.Count >= MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {MaxRows} rows.");

                var row = new ParsedStatementRow { RowNumber = recordNumber };
                try
                {
                    row.PostedDate = ParseDate(Field(fields, map.DateColumn), map.DateFormat);
                    row.Amount = ReadAmount(fields, map);
                    row.Payee = Truncate(Field(fields, map.PayeeColumn), MaxPayeeLength);
                    row.Memo = Truncate(Field(fields, map.MemoColumn), MaxMemoLength);
                    row.Reference = Truncate(Field(fields, map.ReferenceColumn), MaxReferenceLength);
                    var external = Truncate(Field(fields, map.ExternalIdColumn), MaxExternalIdLength);
                    row.ExternalId = external ?? SyntheticExternalId(row, occurrences);
                }
                catch (FormatException ex)
                {
                    row.Error = ex.Message;
                }
                rows.Add(row);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("No data rows were found in the file.");
            return rows;
        }

        private static void ValidateMap(BankStatementColumnMap map)
        {
            if (map.DateColumn < 0)
                throw new InvalidOperationException("Pick the date column.");
            var hasAmount = map.AmountColumn.HasValue;
            var hasSplit = map.DepositColumn.HasValue || map.WithdrawalColumn.HasValue;
            if (!hasAmount && !hasSplit)
                throw new InvalidOperationException("Pick the amount column (or deposit and withdrawal columns).");
            foreach (var column in new[]
                     {
                         map.AmountColumn, map.DepositColumn, map.WithdrawalColumn,
                         map.PayeeColumn, map.MemoColumn, map.ReferenceColumn, map.ExternalIdColumn
                     })
            {
                if (column is < 0)
                    throw new InvalidOperationException("Column positions cannot be negative.");
            }
            if (!string.IsNullOrEmpty(map.Delimiter)
                && map.Delimiter != "\\t"
                && map.Delimiter.Length != 1)
                throw new InvalidOperationException("The delimiter must be a single character (or \\t for tab).");
        }

        private static char ResolveDelimiter(string content, string? requested)
        {
            if (!string.IsNullOrEmpty(requested))
                return requested == "\\t" ? '\t' : requested[0];

            // Auto-detect from the first non-blank line so a leading blank does not fall back to comma.
            var probeLine = FirstNonBlankLine(content);
            var best = ',';
            var bestCount = 0;
            foreach (var candidate in CandidateDelimiters)
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

        /// <summary>RFC 4180 reader: quoted fields may contain delimiters, doubled quotes, and line breaks.</summary>
        private static IEnumerable<List<string>> SplitCsv(string content, char delimiter)
        {
            var field = new StringBuilder();
            var fields = new List<string>();
            var inQuotes = false;
            var length = content.Length;

            for (var i = 0; i < length; i++)
            {
                var ch = content[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < length && content[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }
                    continue;
                }

                if (ch == '"' && field.Length == 0)
                {
                    inQuotes = true;
                }
                else if (ch == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < length && content[i + 1] == '\n')
                        i++;
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = new List<string>();
                }
                else
                {
                    field.Append(ch);
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                yield return fields;
            }
        }

        private static bool IsBlankRecord(List<string> fields)
        {
            foreach (var value in fields)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return false;
            }
            return true;
        }

        private static string? Field(List<string> fields, int? column)
        {
            if (!column.HasValue || column.Value >= fields.Count)
                return null;
            var value = fields[column.Value].Trim();
            return value.Length == 0 ? null : value;
        }

        private static DateTime ParseDate(string? raw, string? format)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException("Date is missing.");

            var parsed = false;
            DateTime date;
            if (!string.IsNullOrWhiteSpace(format))
            {
                parsed = DateTime.TryParseExact(raw, format.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }
            else
            {
                parsed = DateTime.TryParseExact(raw, UnambiguousDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }

            if (!parsed)
                throw new FormatException(string.IsNullOrWhiteSpace(format)
                    ? $"Date '{raw}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map."
                    : $"Date '{raw}' does not match the format '{format.Trim()}'.");
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }

        private static decimal ReadAmount(List<string> fields, BankStatementColumnMap map)
        {
            decimal amount;
            if (map.AmountColumn.HasValue)
            {
                amount = ParseAmount(Field(fields, map.AmountColumn), "Amount");
            }
            else
            {
                var depositRaw = Field(fields, map.DepositColumn);
                var withdrawalRaw = Field(fields, map.WithdrawalColumn);
                if (depositRaw == null && withdrawalRaw == null)
                    throw new FormatException("Amount is missing.");
                var deposit = depositRaw == null ? 0m : Math.Abs(ParseAmount(depositRaw, "Deposit"));
                var withdrawal = withdrawalRaw == null ? 0m : Math.Abs(ParseAmount(withdrawalRaw, "Withdrawal"));
                amount = deposit - withdrawal;
            }

            if (map.InvertSign)
                amount = -amount;
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount == 0m)
                throw new FormatException("Amount is zero.");
            return amount;
        }

        internal static decimal ParseAmount(string? raw, string label)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException($"{label} is missing.");

            var text = raw.Trim();
            var negative = false;
            if (text.StartsWith('(') && text.EndsWith(')'))
            {
                negative = true;
                text = text[1..^1];
            }

            var cleaned = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+')
                    cleaned.Append(ch);
                else if (ch is ',' or ' ' or '\u00a0' or '$' or '€' or '£' or '¥')
                    continue;
                else
                    throw new FormatException($"{label} '{raw}' is not a number.");
            }

            var value = cleaned.ToString();
            if (value.EndsWith('-'))
            {
                negative = !negative;
                value = value[..^1];
            }

            if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount))
                throw new FormatException($"{label} '{raw}' is not a number.");

            return negative ? -amount : amount;
        }

        private static string SyntheticExternalId(ParsedStatementRow row, Dictionary<string, int> occurrences)
        {
            var key = string.Join('|',
                row.PostedDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Amount!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                (row.Payee ?? string.Empty).ToLowerInvariant(),
                (row.Memo ?? string.Empty).ToLowerInvariant(),
                (row.Reference ?? string.Empty).ToLowerInvariant());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..SyntheticHashLength].ToLowerInvariant();
            occurrences.TryGetValue(hash, out var seen);
            occurrences[hash] = ++seen;
            return seen == 1 ? $"csv:{hash}" : $"csv:{hash}#{seen}";
        }

        // ---------------------------------------------------------------- OFX / QFX

        private static readonly Regex TransactionBlock = new(
            @"<STMTTRN>(?<body>.*?)(?=</STMTTRN>|<STMTTRN>|</BANKTRANLIST>|</CCSTMTTRNRS>|</STMTRS>|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

        public static IReadOnlyList<ParsedStatementRow> ParseOfx(string content)
        {
            content = content.TrimStart('\uFEFF');
            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var number = 0;

            foreach (Match match in TransactionBlock.Matches(content))
            {
                number++;
                if (rows.Count >= MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {MaxRows} rows.");

                var body = match.Groups["body"].Value;
                var row = new ParsedStatementRow { RowNumber = number };
                try
                {
                    row.PostedDate = ParseOfxDate(OfxField(body, "DTPOSTED"));
                    row.Amount = ParseOfxAmount(OfxField(body, "TRNAMT"));
                    row.Payee = Truncate(OfxField(body, "NAME") ?? OfxField(body, "PAYEE"), MaxPayeeLength);
                    row.Memo = Truncate(OfxField(body, "MEMO"), MaxMemoLength);
                    row.Reference = Truncate(OfxField(body, "CHECKNUM") ?? OfxField(body, "REFNUM"), MaxReferenceLength);
                    row.ExternalId = Truncate(OfxField(body, "FITID"), MaxExternalIdLength)
                        ?? SyntheticExternalId(row, occurrences);
                }
                catch (FormatException ex)
                {
                    row.Error = ex.Message;
                }
                rows.Add(row);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("No transactions were found in the OFX file.");
            return rows;
        }

        private static string? OfxField(string block, string tag)
        {
            var match = Regex.Match(
                block,
                @"<" + tag + @">\s*(?<value>[^<\r\n]*)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!match.Success)
                return null;
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
            return value.Length == 0 ? null : value;
        }

        private static DateTime ParseOfxDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8)
                throw new FormatException("Posted date is missing.");
            if (!DateTime.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException($"Posted date '{raw}' is not recognised.");
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }

        private static decimal ParseOfxAmount(string? raw)
        {
            var amount = Math.Round(ParseAmount(NormalizeOfxDecimalComma(raw), "Amount"), 2, MidpointRounding.AwayFromZero);
            if (amount == 0m)
                throw new FormatException("Amount is zero.");
            return amount;
        }

        /// <summary>
        /// Some OFX 1.x files write the decimal separator as a comma ("12,50"). A comma is only a decimal separator when
        /// it is the single separator and one or two digits follow it; every other comma is a thousands separator
        /// ("1,234" is 1234) and ParseAmount drops it.
        /// </summary>
        private static string? NormalizeOfxDecimalComma(string? raw)
        {
            if (raw == null || raw.Contains('.'))
                return raw;

            var lastComma = raw.LastIndexOf(',');
            if (lastComma < 0 || lastComma != raw.IndexOf(','))
                return raw;

            var fractionDigits = raw.Length - lastComma - 1;
            return fractionDigits is >= 1 and <= MaxDecimalCommaFractionDigits
                ? string.Concat(raw.AsSpan(0, lastComma), ".", raw.AsSpan(lastComma + 1))
                : raw;
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
