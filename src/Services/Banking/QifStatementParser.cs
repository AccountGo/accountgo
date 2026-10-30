// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Services.Banking
{
    /// <summary>Quicken Interchange Format (!Type:Bank) reader (#447 Phase C).</summary>
    public static class QifStatementParser
    {
        public static IReadOnlyList<ParsedStatementRow> Parse(string content)
        {
            content = content.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            DateTime? date = null;
            decimal? amount = null;
            string? payee = null;
            string? memo = null;
            string? reference = null;
            var number = 0;

            void Flush()
            {
                if (date == null && amount == null && payee == null && memo == null)
                    return;
                number++;
                if (rows.Count >= BankStatementParser.MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {BankStatementParser.MaxRows} rows.");
                var row = new ParsedStatementRow { RowNumber = number };
                try
                {
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
                        date = ParseQifDate(value);
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

        private static DateTime ParseQifDate(string raw)
        {
            // Common: M/D/YY, M/D'YY, DD/MM/YYYY
            var normalized = raw.Replace('\'', '/');
            string[] formats =
            {
                "M/d/yy", "M/d/yyyy", "d/M/yy", "d/M/yyyy",
                "MM/dd/yy", "MM/dd/yyyy", "dd/MM/yy", "dd/MM/yyyy",
                "yyyy-MM-dd"
            };
            if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            throw new FormatException($"Date '{raw}' is not recognised.");
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
