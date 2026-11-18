// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Core.Domain
{
    /// <summary>
    /// Explicit supported currency precision rules. Rejects unsupported or unknown currencies
    /// rather than silently assuming two decimal places.
    /// </summary>
    public static class CurrencyPrecisionRules
    {
        private static readonly IReadOnlyDictionary<string, int> SupportedPrecisions =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Zero-decimal currencies
                ["JPY"] = 0,
                ["KRW"] = 0,
                ["VND"] = 0,
                ["CLP"] = 0,
                // Two-decimal currencies
                ["USD"] = 2,
                ["EUR"] = 2,
                ["GBP"] = 2,
                ["CAD"] = 2,
                ["AUD"] = 2,
                ["NZD"] = 2,
                ["SGD"] = 2,
                ["HKD"] = 2,
                ["CHF"] = 2,
                ["SEK"] = 2,
                ["NOK"] = 2,
                ["DKK"] = 2,
                ["PHP"] = 2,
                ["THB"] = 2,
                ["MYR"] = 2,
                ["IDR"] = 2,
                ["INR"] = 2,
                ["MXN"] = 2,
                ["BRL"] = 2,
                ["ZAR"] = 2,
                ["AED"] = 2,
                ["SAR"] = 2,
                ["CNY"] = 2,
                ["TWD"] = 2,
                // Three-decimal currencies
                ["KWD"] = 3,
                ["BHD"] = 3,
                ["OMR"] = 3,
                ["JOD"] = 3,
                ["TND"] = 3,
            };

        public static int RequireSupportedPrecision(string? currencyCode)
        {
            if (string.IsNullOrWhiteSpace(currencyCode))
                throw new InvalidOperationException("Workspace currency code is required.");

            var normalized = currencyCode.Trim().ToUpperInvariant();
            if (!SupportedPrecisions.TryGetValue(normalized, out var precision))
                throw new InvalidOperationException($"Currency '{normalized}' does not have a supported ledger precision.");

            return precision;
        }

        public static decimal RoundMoney(decimal amount, int precision) =>
            Math.Round(amount, precision, MidpointRounding.AwayFromZero);

        public static bool HasValidPrecision(decimal amount, int precision) =>
            RoundMoney(amount, precision) == amount;
    }
}
