// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.Banking
{
    /// <summary>SWIFT MT940 statement reader (#447 Phase C). Parses :61: / :86: pairs.</summary>
    public static class Mt940StatementParser
    {
        private static readonly Regex Tag61 = new(
            @"^:61:(?<yy>\d{2})(?<mm>\d{2})(?<dd>\d{2})(?:\d{4})?(?<dc>[CD]|RC|RD)(?<amount>[\d,\.]+)(?<rest>.*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        public static IReadOnlyList<ParsedStatementRow> Parse(string content)
        {
            content = content.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var number = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (!line.StartsWith(":61:", StringComparison.Ordinal))
                    continue;

                number++;
                if (rows.Count >= BankStatementParser.MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {BankStatementParser.MaxRows} rows.");

                var row = new ParsedStatementRow { RowNumber = number };
                try
                {
                    var match = Tag61.Match(line);
                    if (!match.Success)
                        throw new FormatException($"MT940 :61: line is not recognised: '{line}'.");

                    var year = 2000 + int.Parse(match.Groups["yy"].Value, CultureInfo.InvariantCulture);
                    var month = int.Parse(match.Groups["mm"].Value, CultureInfo.InvariantCulture);
                    var day = int.Parse(match.Groups["dd"].Value, CultureInfo.InvariantCulture);
                    row.PostedDate = DateTime.SpecifyKind(new DateTime(year, month, day), DateTimeKind.Utc);

                    var dc = match.Groups["dc"].Value;
                    var amountRaw = match.Groups["amount"].Value.Replace(',', '.');
                    var amount = BankStatementParser.ParseAmount(amountRaw, "Amount");
                    if (dc is "D" or "RD")
                        amount = -Math.Abs(amount);
                    else
                        amount = Math.Abs(amount);
                    amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
                    if (amount == 0m)
                        throw new FormatException("Amount is zero.");
                    row.Amount = amount;

                    var rest = match.Groups["rest"].Value;
                    // Reference often after //NTRF or similar; take trailing alphanumerics.
                    row.Reference = Truncate(ExtractReference(rest), BankStatementParser.MaxReferenceLength);

                    if (i + 1 < lines.Length && lines[i + 1].StartsWith(":86:", StringComparison.Ordinal))
                    {
                        var narrative = lines[i + 1][4..].Trim();
                        i++;
                        while (i + 1 < lines.Length
                               && !lines[i + 1].StartsWith(":", StringComparison.Ordinal)
                               && !lines[i + 1].StartsWith("-", StringComparison.Ordinal))
                        {
                            i++;
                            narrative += " " + lines[i].Trim();
                        }
                        row.Payee = Truncate(narrative, BankStatementParser.MaxPayeeLength);
                        row.Memo = Truncate(narrative, BankStatementParser.MaxMemoLength);
                    }

                    row.ExternalId = Truncate(row.Reference, BankStatementParser.MaxExternalIdLength)
                        ?? SyntheticId(row, occurrences);
                }
                catch (FormatException ex)
                {
                    row.Error = ex.Message;
                }
                catch (ArgumentOutOfRangeException)
                {
                    row.Error = "Posted date is not recognised.";
                }
                rows.Add(row);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("No transactions were found in the MT940 file.");
            return rows;
        }

        private static string? ExtractReference(string rest)
        {
            var idx = rest.IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0 && idx + 2 < rest.Length)
                return rest[(idx + 2)..].Trim();
            return string.IsNullOrWhiteSpace(rest) ? null : rest.Trim();
        }

        private static string SyntheticId(ParsedStatementRow row, Dictionary<string, int> occurrences)
        {
            var key = string.Join('|',
                row.PostedDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Amount!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                (row.Payee ?? string.Empty).ToLowerInvariant());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
            occurrences.TryGetValue(hash, out var seen);
            occurrences[hash] = ++seen;
            return seen == 1 ? $"mt940:{hash}" : $"mt940:{hash}#{seen}";
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
