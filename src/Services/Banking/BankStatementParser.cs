// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    /// <summary>
    /// CSV and OFX/QFX statement readers. Detection lives in <see cref="StatementColumnDetector"/> /
    /// <see cref="StatementImportResolver"/>; this type only applies a resolved mapping. Never throws for bad rows.
    /// </summary>
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
        /// 03/04/2026 are never guessed here — the resolver must supply an exact format.
        /// </summary>
        private static readonly string[] UnambiguousDateFormats =
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "d-MMM-yyyy", "dd-MMM-yy",
            "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyyMMdd"
        };

        private static readonly Regex CurrencyCodeInAmount = new(
            @"\b[A-Z]{3}\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        private static readonly Regex CrDrSuffix = new(
            @"\s*(?<tag>CR|DR|Cr|Dr|cr|dr)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        public static IReadOnlyList<ParsedStatementRow> Parse(
            BankStatementSources source,
            string content,
            BankStatementColumnMap? map,
            string? accountCurrencyCode = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");
            if (content.Length > MaxContentChars)
                throw new InvalidOperationException("The statement file is too large.");

            return source switch
            {
                BankStatementSources.Csv or BankStatementSources.Xlsx => ParseCsv(
                    content,
                    map ?? throw new InvalidOperationException("Map the CSV columns first."),
                    accountCurrencyCode),
                BankStatementSources.Ofx => ParseOfx(content),
                BankStatementSources.Camt => CamtStatementParser.Parse(content),
                BankStatementSources.Mt940 => Mt940StatementParser.Parse(content),
                BankStatementSources.Qif => QifStatementParser.Parse(content),
                _ => throw new InvalidOperationException("Pick a supported statement file.")
            };
        }

        // ---------------------------------------------------------------- CSV

        /// <summary>Resolve the user map, then parse. Prefer this over <see cref="ParseCsv(string, ResolvedStatementMap)"/> from callers that hold a DTO map.</summary>
        public static IReadOnlyList<ParsedStatementRow> ParseCsv(
            string content,
            BankStatementColumnMap map,
            string? accountCurrencyCode = null)
        {
            var resolved = StatementImportResolver.Resolve(content, map, accountCurrencyCode);
            return ParseCsv(content, resolved.Map, resolved.Records);
        }

        public static IReadOnlyList<ParsedStatementRow> ParseCsv(string content, ResolvedStatementMap map) =>
            ParseCsv(content, map, records: null);

        public static IReadOnlyList<ParsedStatementRow> ParseCsv(
            string content,
            ResolvedStatementMap map,
            IReadOnlyList<IReadOnlyList<string>>? records)
        {
            ValidateResolvedMap(map);
            content = content.TrimStart('\uFEFF');
            records ??= SplitCsvRecords(content, map.Delimiter);

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var recordNumber = 0;

            foreach (var fields in records)
            {
                recordNumber++;
                if (IsBlankRecord(fields))
                    continue;
                if (map.HeaderRowIndex.HasValue && recordNumber - 1 <= map.HeaderRowIndex.Value)
                {
                    // Skip preamble and the header row (recordNumber is 1-based; HeaderRowIndex is 0-based).
                    continue;
                }

                if (rows.Count >= MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {MaxRows} rows.");

                var row = new ParsedStatementRow { RowNumber = recordNumber };
                try
                {
                    if (map.NeedsDateFormatChoice)
                        throw new FormatException(
                            $"Date '{Field(fields, map.DateColumn)}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.");

                    if (map.StatusColumn.HasValue)
                    {
                        var status = Field(fields, map.StatusColumn);
                        if (status == null || !map.AllowedStatuses.Contains(status))
                            throw new FormatException(
                                $"{(status ?? "blank")} row skipped");
                    }

                    if (map.CurrencyColumn.HasValue && map.AccountCurrencyCode != null)
                    {
                        var currency = Field(fields, map.CurrencyColumn);
                        if (currency != null
                            && !string.Equals(currency, map.AccountCurrencyCode, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new FormatException(
                                $"{currency.ToUpperInvariant()} row skipped: this account is {map.AccountCurrencyCode}");
                        }
                    }

                    row.PostedDate = ParseDate(Field(fields, map.DateColumn), map);
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

        private static void ValidateResolvedMap(ResolvedStatementMap map)
        {
            if (map.DateColumn < 0)
                throw new InvalidOperationException("Pick the date column.");
            var hasAmount = map.AmountColumn.HasValue;
            var hasSplit = map.DepositColumn.HasValue || map.WithdrawalColumn.HasValue;
            if (!hasAmount && !hasSplit)
                throw new InvalidOperationException("Pick the amount column (or deposit and withdrawal columns).");
            foreach (var column in new int?[]
                     {
                         map.AmountColumn, map.DepositColumn, map.WithdrawalColumn, map.DirectionColumn,
                         map.PayeeColumn, map.MemoColumn, map.ReferenceColumn, map.ExternalIdColumn,
                         map.CurrencyColumn, map.StatusColumn
                     })
            {
                if (column is < 0)
                    throw new InvalidOperationException("Column positions cannot be negative.");
            }
        }

        /// <summary>Public for the resolver and tests — RFC 4180 reader.</summary>
        public static IReadOnlyList<IReadOnlyList<string>> SplitCsvRecords(string content, char delimiter)
        {
            var list = new List<IReadOnlyList<string>>();
            foreach (var row in SplitCsv(content, delimiter))
                list.Add(row);
            return list;
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

        private static bool IsBlankRecord(IReadOnlyList<string> fields)
        {
            foreach (var value in fields)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return false;
            }
            return true;
        }

        private static string? Field(IReadOnlyList<string> fields, int? column)
        {
            if (!column.HasValue || column.Value >= fields.Count)
                return null;
            var value = fields[column.Value].Trim();
            return value.Length == 0 ? null : value;
        }

        private static DateTime ParseDate(string? raw, ResolvedStatementMap map)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException("Date is missing.");

            return map.DateMode switch
            {
                StatementDateMode.ExactFormat => ParseExactDate(raw, map.DateFormat),
                StatementDateMode.Iso8601 => ParseIso8601Date(raw),
                StatementDateMode.MonthName => ParseMonthNameDate(raw),
                StatementDateMode.ExcelSerial => ParseExcelSerialDate(raw),
                StatementDateMode.EpochSeconds => ParseEpochDate(raw, milliseconds: false),
                StatementDateMode.EpochMilliseconds => ParseEpochDate(raw, milliseconds: true),
                StatementDateMode.UnambiguousBuiltIn => ParseUnambiguousDate(raw),
                _ => throw new FormatException($"Date '{raw}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.")
            };
        }

        private static DateTime ParseExactDate(string raw, string? format)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new FormatException(
                    $"Date '{raw}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.");

            // Allow both single- and double-digit day/month tokens against flexible patterns.
            var formats = ExpandFlexibleNumericFormat(format.Trim());
            if (!DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && !DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                throw new FormatException($"Date '{raw}' does not match the format '{format.Trim()}'.");
            }
            return AsUtcCalendarDate(date);
        }

        private static string[] ExpandFlexibleNumericFormat(string format)
        {
            // Keep day-first and month-first families separate — never cross the ambiguous boundary.
            var trimmed = format.Trim();
            if (trimmed.Contains("MMM", StringComparison.Ordinal))
                return new[] { trimmed };
            if (trimmed.StartsWith('d') || trimmed.StartsWith('D'))
            {
                return new[]
                {
                    trimmed, "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy",
                    "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy"
                };
            }
            if (trimmed.StartsWith('M'))
            {
                return new[]
                {
                    trimmed, "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy",
                    "M-d-yyyy", "MM-dd-yyyy", "M.d.yyyy", "MM.dd.yyyy"
                };
            }
            return new[] { trimmed };
        }

        private static DateTime ParseIso8601Date(string raw)
        {
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var dto))
            {
                // Calendar date as written in the file's offset — never convert via server local time.
                return AsUtcCalendarDate(dto.DateTime);
            }
            throw new FormatException($"Date '{raw}' is not ISO 8601.");
        }

        private static DateTime ParseMonthNameDate(string raw)
        {
            foreach (var format in StatementColumnDetector.MonthNameExactFormats)
            {
                if (DateTime.TryParseExact(raw, format, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var date))
                    return AsUtcCalendarDate(date);
            }
            throw new FormatException($"Date '{raw}' does not match a month-name format.");
        }

        private static DateTime ParseExcelSerialDate(string raw)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                || serial is < 32874 or > 73050)
                throw new FormatException($"Date '{raw}' is not a recognised Excel serial.");
            return AsUtcCalendarDate(DateTime.FromOADate(serial));
        }

        private static DateTime ParseEpochDate(string raw, bool milliseconds)
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                throw new FormatException($"Date '{raw}' is not a Unix epoch value.");
            try
            {
                var dto = milliseconds
                    ? DateTimeOffset.FromUnixTimeMilliseconds(n)
                    : DateTimeOffset.FromUnixTimeSeconds(n);
                return AsUtcCalendarDate(dto.UtcDateTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FormatException($"Date '{raw}' is not a Unix epoch value.");
            }
        }

        private static DateTime ParseUnambiguousDate(string raw)
        {
            // ISO first — unambiguous even with time / offset / fractional seconds.
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var dto)
                && LooksIsoLike(raw))
                return AsUtcCalendarDate(dto.DateTime);

            if (DateTime.TryParseExact(raw, UnambiguousDateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)
                || DateTime.TryParseExact(raw, UnambiguousDateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out date))
                return AsUtcCalendarDate(date);

            throw new FormatException(
                $"Date '{raw}' needs a date format (for example MM/dd/yyyy or dd/MM/yyyy). Set it in the column map.");
        }

        private static bool LooksIsoLike(string raw) =>
            raw.Length >= 10 && raw[4] == '-' && raw[7] == '-';

        /// <summary>Store the calendar day only; Kind=Utc means "date as written", not a TZ conversion.</summary>
        private static DateTime AsUtcCalendarDate(DateTime date) =>
            DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        private static decimal ReadAmount(IReadOnlyList<string> fields, ResolvedStatementMap map)
        {
            decimal amount;
            if (map.AmountColumn.HasValue)
            {
                amount = ParseAmount(Field(fields, map.AmountColumn), "Amount");
                if (map.DirectionColumn.HasValue)
                {
                    var direction = Field(fields, map.DirectionColumn);
                    if (direction == null)
                        throw new FormatException("Direction is missing.");
                    if (!map.DirectionSigns.TryGetValue(direction, out var sign))
                        throw new FormatException($"unknown direction '{direction}'");
                    amount = Math.Abs(amount) * sign;
                }
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

        /// <summary>
        /// Shared CSV/OFX amount parser: currency symbols or codes, parentheses, trailing minus,
        /// CR/DR suffixes, and US/European grouping (same rules as former OFX normaliser).
        /// </summary>
        public static decimal ParseAmount(string? raw, string label)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException($"{label} is missing.");

            var text = raw.Trim();
            var negative = false;

            if (text.StartsWith('(') && text.EndsWith(')'))
            {
                negative = true;
                text = text[1..^1].Trim();
            }

            var crDr = CrDrSuffix.Match(text);
            if (crDr.Success)
            {
                var tag = crDr.Groups["tag"].Value;
                if (tag.Equals("DR", StringComparison.OrdinalIgnoreCase))
                    negative = !negative;
                // CR keeps the sign (typically positive credit).
                text = text[..crDr.Index].Trim();
            }

            if (text.EndsWith('-'))
            {
                negative = !negative;
                text = text[..^1].Trim();
            }
            if (text.StartsWith('+'))
                text = text[1..].Trim();
            if (text.StartsWith('-'))
            {
                negative = !negative;
                text = text[1..].Trim();
            }

            text = CurrencyCodeInAmount.Replace(text, string.Empty).Trim();

            var stripped = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsDigit(ch) || ch is '.' or ',')
                    stripped.Append(ch);
                else if (ch is ' ' or '\u00a0' or '\'' or '$' or '€' or '£' or '¥' or '₱')
                    continue;
                else
                    throw new FormatException($"{label} '{raw}' is not a number.");
            }

            var normalized = NormalizeGroupedNumber(stripped.ToString());
            if (string.IsNullOrWhiteSpace(normalized)
                || !decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
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
            return AsUtcCalendarDate(date);
        }

        private static decimal ParseOfxAmount(string? raw)
        {
            var amount = Math.Round(ParseAmount(raw, "Amount"), 2, MidpointRounding.AwayFromZero);
            if (amount == 0m)
                throw new FormatException("Amount is zero.");
            return amount;
        }

        /// <summary>
        /// US / European grouping. When both <c>.</c> and <c>,</c> appear, the last one is the decimal;
        /// a lone <c>,</c> with exactly three trailing digits is thousands.
        /// </summary>
        internal static string? NormalizeGroupedNumber(string? raw)
        {
            if (raw == null)
                return null;

            var firstDot = raw.IndexOf('.');
            var lastDot = raw.LastIndexOf('.');
            var firstComma = raw.IndexOf(',');
            var lastComma = raw.LastIndexOf(',');
            var hasDot = firstDot >= 0;
            var hasComma = firstComma >= 0;

            int? decimalAt = null;
            if (hasDot && hasComma)
            {
                decimalAt = Math.Max(lastDot, lastComma);
            }
            else if (hasDot)
            {
                if (firstDot == lastDot)
                    decimalAt = firstDot;
            }
            else if (hasComma)
            {
                if (firstComma != lastComma)
                {
                    // Repeated commas are thousands grouping.
                }
                else
                {
                    var digitsAfter = 0;
                    var allDigits = true;
                    for (var i = firstComma + 1; i < raw.Length; i++)
                    {
                        if (!char.IsDigit(raw[i]))
                        {
                            allDigits = false;
                            break;
                        }
                        digitsAfter++;
                    }
                    if (!(allDigits && digitsAfter == 3))
                        decimalAt = firstComma;
                }
            }

            var cleaned = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch is '.' or ',')
                {
                    if (decimalAt == i)
                        cleaned.Append('.');
                    continue;
                }
                cleaned.Append(ch);
            }
            return cleaned.ToString();
        }

        /// <summary>Kept for existing OFX tests; delegates to the shared normaliser.</summary>
        internal static string? NormalizeOfxNumber(string? raw) => NormalizeGroupedNumber(raw);

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
