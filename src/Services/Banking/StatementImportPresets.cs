// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Built-in starting maps matched when the preset's required headers are a subset of the file's.
    /// Columns are resolved by header name, not position. Presets never skip preview (#447 Phase B).
    /// </summary>
    public static class StatementImportPresets
    {
        public sealed class Preset
        {
            public required string Name { get; init; }
            /// <summary>Normalised header tokens that must all appear in the file.</summary>
            public required IReadOnlyList<string> RequiredHeaders { get; init; }
            /// <summary>Role → preferred header names (first match wins).</summary>
            public required IReadOnlyDictionary<string, string[]> RoleHeaders { get; init; }

            public BankStatementColumnMap Bind(IReadOnlyList<string> fileHeaders)
            {
                int? Find(params string[] names)
                {
                    foreach (var name in names)
                    {
                        var needle = StatementColumnDetector.NormalizeHeaderToken(name);
                        for (var i = 0; i < fileHeaders.Count; i++)
                        {
                            if (StatementColumnDetector.NormalizeHeaderToken(fileHeaders[i]) == needle)
                                return i;
                        }
                    }
                    return null;
                }

                int Require(string role)
                {
                    if (!RoleHeaders.TryGetValue(role, out var names) || names.Length == 0)
                        throw new InvalidOperationException($"Preset '{Name}' is missing role '{role}'.");
                    return Find(names) ?? throw new InvalidOperationException(
                        $"Preset '{Name}' could not find a '{role}' column.");
                }

                var amountNames = RoleHeaders.GetValueOrDefault("amount") ?? Array.Empty<string>();
                var amountColumn = Find(amountNames);
                // signed_amount already encodes direction — only map direction for unsigned amount columns.
                var amountHeader = amountColumn.HasValue
                    ? StatementColumnDetector.NormalizeHeaderToken(fileHeaders[amountColumn.Value])
                    : null;
                var usesSignedAmount = amountHeader is "signed_amount" or "signedamount";

                var map = new BankStatementColumnMap
                {
                    HasHeaderRow = true,
                    DateColumn = Require("date"),
                    AmountColumn = amountColumn,
                    DirectionColumn = usesSignedAmount
                        ? null
                        : Find(RoleHeaders.GetValueOrDefault("direction") ?? Array.Empty<string>()),
                    CurrencyColumn = Find(RoleHeaders.GetValueOrDefault("currency") ?? Array.Empty<string>()),
                    StatusColumn = Find(RoleHeaders.GetValueOrDefault("status") ?? Array.Empty<string>()),
                    ExternalIdColumn = Find(RoleHeaders.GetValueOrDefault("externalId") ?? Array.Empty<string>()),
                    PayeeColumn = Find(RoleHeaders.GetValueOrDefault("payee") ?? Array.Empty<string>()),
                    MemoColumn = Find(RoleHeaders.GetValueOrDefault("memo") ?? Array.Empty<string>()),
                    FeeColumn = Find(RoleHeaders.GetValueOrDefault("fee") ?? Array.Empty<string>()),
                    ReferenceColumn = Find(RoleHeaders.GetValueOrDefault("reference") ?? Array.Empty<string>())
                };

                if (map.AmountColumn == null)
                    throw new InvalidOperationException($"Preset '{Name}' could not find an amount column.");
                return map;
            }

            public bool Matches(IReadOnlyList<string> fileHeaders)
            {
                var present = new HashSet<string>(
                    fileHeaders.Select(StatementColumnDetector.NormalizeHeaderToken).Where(t => t.Length > 0),
                    StringComparer.Ordinal);
                return RequiredHeaders.All(h => present.Contains(h));
            }
        }

        /// <summary>
        /// Header sets taken from anonymised real exports (Wise activity, PayPal activity, Revolut statement).
        /// </summary>
        private static readonly Preset[] All =
        {
            new()
            {
                Name = "Wise activity",
                RequiredHeaders = NormalizeMany(
                    "profile_id", "activity_id", "created_on", "status", "direction", "amount", "currency"),
                RoleHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["date"] = new[] { "created_on" },
                    // Prefer signed_amount when present; else unsigned amount + direction.
                    ["amount"] = new[] { "signed_amount", "amount" },
                    ["direction"] = new[] { "direction" },
                    ["currency"] = new[] { "currency" },
                    ["status"] = new[] { "status" },
                    ["externalId"] = new[] { "activity_id" },
                    ["payee"] = new[] { "source_name", "target_name", "merchant" },
                    ["memo"] = new[] { "target_name", "reference_number" }
                }
            },
            new()
            {
                Name = "PayPal activity",
                RequiredHeaders = NormalizeMany("date", "name", "status", "currency", "net"),
                RoleHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["date"] = new[] { "date" },
                    ["amount"] = new[] { "net" },
                    ["currency"] = new[] { "currency" },
                    ["status"] = new[] { "status" },
                    ["payee"] = new[] { "name" },
                    ["memo"] = new[] { "type", "item title", "item_title" },
                    ["externalId"] = new[] { "transaction id", "transaction_id" }
                }
            },
            new()
            {
                Name = "Revolut statement",
                RequiredHeaders = NormalizeMany("completed_date", "description", "amount", "fee", "currency", "state"),
                RoleHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["date"] = new[] { "completed_date", "started_date" },
                    ["amount"] = new[] { "amount" },
                    ["fee"] = new[] { "fee" },
                    ["currency"] = new[] { "currency" },
                    ["status"] = new[] { "state" },
                    ["payee"] = new[] { "description" },
                    ["externalId"] = new[] { "payment_reference", "reference" }
                }
            }
        };

        public static Preset? FindMatching(IReadOnlyList<string> fileHeaders)
        {
            // Prefer the most specific preset (most required headers).
            return All
                .Where(p => p.Matches(fileHeaders))
                .OrderByDescending(p => p.RequiredHeaders.Count)
                .FirstOrDefault();
        }

        /// <summary>Exact-signature lookup kept for tests that still pass a joined signature.</summary>
        public static Preset? Find(string headerSignature)
        {
            if (string.IsNullOrWhiteSpace(headerSignature))
                return null;
            var parts = headerSignature.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return FindMatching(parts);
        }

        public static IReadOnlyList<Preset> List() => All;

        private static IReadOnlyList<string> NormalizeMany(params string[] headers) =>
            headers.Select(StatementColumnDetector.NormalizeHeaderToken).ToList();
    }
}
