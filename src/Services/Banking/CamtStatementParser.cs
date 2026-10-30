// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Services.Banking
{
    /// <summary>ISO 20022 CAMT.053 bank-to-customer statement XML reader (#447 Phase C).</summary>
    public static class CamtStatementParser
    {
        public static IReadOnlyList<ParsedStatementRow> Parse(string content)
        {
            content = content.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The statement file is empty.");

            XDocument doc;
            try
            {
                doc = XDocument.Parse(content, LoadOptions.None);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The CAMT file is not valid XML.", ex);
            }

            var entries = doc.Descendants().Where(e => e.Name.LocalName == "Ntry").ToList();
            if (entries.Count == 0)
                throw new InvalidOperationException("No transactions were found in the CAMT file.");

            var rows = new List<ParsedStatementRow>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var number = 0;

            foreach (var entry in entries)
            {
                number++;
                if (rows.Count >= BankStatementParser.MaxRows)
                    throw new InvalidOperationException($"A statement can have at most {BankStatementParser.MaxRows} rows.");

                var row = new ParsedStatementRow { RowNumber = number };
                try
                {
                    var amtEl = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "Amt")
                        ?? throw new FormatException("Amount is missing.");
                    var amount = BankStatementParser.ParseAmount(amtEl.Value, "Amount");
                    var cdtDbt = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "CdtDbtInd")?.Value;
                    if (string.Equals(cdtDbt, "DBIT", StringComparison.OrdinalIgnoreCase))
                        amount = -Math.Abs(amount);
                    else
                        amount = Math.Abs(amount);
                    amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
                    if (amount == 0m)
                        throw new FormatException("Amount is zero.");
                    row.Amount = amount;

                    var booking = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "BookgDt")
                        ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Dt" || e.Name.LocalName == "DtTm")
                        ?.Value
                        ?? entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "ValDt")
                            ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Dt" || e.Name.LocalName == "DtTm")
                            ?.Value;
                    if (string.IsNullOrWhiteSpace(booking))
                        throw new FormatException("Date is missing.");
                    if (DateTimeOffset.TryParse(booking, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var dto))
                        row.PostedDate = DateTime.SpecifyKind(dto.DateTime.Date, DateTimeKind.Utc);
                    else if (DateTime.TryParseExact(booking[..Math.Min(10, booking.Length)], "yyyy-MM-dd",
                                 CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        row.PostedDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
                    else
                        throw new FormatException($"Date '{booking}' is not recognised.");

                    var acctSvcrRef = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "AcctSvcrRef")?.Value;
                    var endToEnd = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "EndToEndId")?.Value;
                    row.ExternalId = Truncate(acctSvcrRef ?? endToEnd, BankStatementParser.MaxExternalIdLength);
                    row.Reference = Truncate(endToEnd ?? acctSvcrRef, BankStatementParser.MaxReferenceLength);

                    var name = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "Nm")?.Value;
                    var info = string.Join(" ",
                        entry.Descendants().Where(e => e.Name.LocalName == "Ustrd").Select(e => e.Value.Trim())
                            .Where(v => v.Length > 0));
                    row.Payee = Truncate(name ?? (info.Length > 0 ? info : null), BankStatementParser.MaxPayeeLength);
                    row.Memo = Truncate(info.Length > 0 ? info : name, BankStatementParser.MaxMemoLength);
                    row.ExternalId ??= SyntheticId(row, occurrences);
                }
                catch (FormatException ex)
                {
                    row.Error = ex.Message;
                }
                rows.Add(row);
            }

            return rows;
        }

        private static string SyntheticId(ParsedStatementRow row, Dictionary<string, int> occurrences)
        {
            var key = string.Join('|',
                row.PostedDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Amount!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                (row.Payee ?? string.Empty).ToLowerInvariant(),
                (row.Memo ?? string.Empty).ToLowerInvariant());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
            occurrences.TryGetValue(hash, out var seen);
            occurrences[hash] = ++seen;
            return seen == 1 ? $"camt:{hash}" : $"camt:{hash}#{seen}";
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
