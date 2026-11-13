// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Services.Banking
{
    /// <summary>What the selected bank account is known as, so a statement balance can be matched to it.</summary>
    public sealed record StatementAccountContext(string? CurrencyCode, string? Iban = null, string? Number = null);

    /// <summary>Stable reason codes for why no (or an incomplete) statement closing balance is offered.</summary>
    public static class StatementBalanceReasons
    {
        public const string NotBooked = "not_booked";
        public const string OtherAccount = "other_account";
        public const string MultipleAccounts = "multiple_accounts";
        public const string CurrencyMismatch = "currency_mismatch";
        public const string CurrencyMissing = "currency_missing";
        public const string AmbiguousLatest = "ambiguous_latest";
        public const string DateMissing = "date_missing";
    }

    /// <summary>One booked closing balance read from a statement block.</summary>
    internal sealed record StatementBalanceCandidate(decimal Amount, DateTime? AsOfDate, string? CurrencyCode);

    /// <summary>One statement block (OFX STMTRS, CAMT Stmt, MT940 :20:..) with what it says about its account and balance.</summary>
    internal sealed class StatementBalanceBlock
    {
        /// <summary>Account identifier as written, for display.</summary>
        public string? AccountIdentifier { get; init; }
        /// <summary>Normalised identifiers that can match the bank's IBAN or number. The first is the grouping key.</summary>
        public IReadOnlyList<string> AccountKeys { get; init; } = Array.Empty<string>();
        public StatementBalanceCandidate? Booked { get; init; }
        /// <summary>True when the block carried a balance that is not the booked closing balance.</summary>
        public bool HasNonBookedBalance { get; init; }
        /// <summary>True when the block carries multiple conflicting booked closing balances.</summary>
        public bool IsAmbiguous { get; init; }
    }

    internal static class StatementBalanceSelector
    {
        internal static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var cleaned = new string(value.Trim().ToUpperInvariant()
                .Where(c => c != ' ' && c != '-' && c != '.').ToArray());
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>Calendar date exactly as written (no time zone conversion); impossible dates are treated as missing.</summary>
        internal static DateTime? ParseCalendarDate(string? raw, string format)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var text = raw.Trim();
            if (text.Length < format.Length)
                return null;
            return DateTime.TryParseExact(text[..format.Length], format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
                ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
                : null;
        }

        /// <summary>
        /// Two normalised identifiers match when they are equal, or when the longer identifier
        /// ends with the shorter identifier and the shorter identifier has at least 4 characters.
        /// A domestic number is the trailing BBAN part of an IBAN in GB, DE, NL, BE and similar
        /// countries; users also often record only the trailing digits of a bank account.
        /// Matching is strictly exact or suffix-based; arbitrary middle-of-string substrings are rejected.
        /// Both inputs are expected to be normalised (uppercase, no whitespace or punctuation).
        /// </summary>
        internal static bool IsAccountMatch(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;

            var (longer, shorter) = a.Length >= b.Length ? (a, b) : (b, a);
            return shorter.Length >= 4 && longer.EndsWith(shorter, StringComparison.Ordinal);
        }

        internal static (StatementClosingBalance? Balance, string? Reason) Select(
            IReadOnlyList<StatementBalanceBlock> blocks,
            StatementAccountContext? context)
        {
            if (blocks.Count == 0)
                return (null, null);

            var bankKeys = new[] { Normalize(context?.Iban), Normalize(context?.Number) }
                .Where(k => k != null).Select(k => k!).ToList();
            var bankKnown = bankKeys.Count > 0;

            // Group by the primary identifier. Blocks without one share a single "unidentified" group.
            var groups = blocks.GroupBy(b => b.AccountKeys.FirstOrDefault()).ToList();
            IReadOnlyList<StatementBalanceBlock> chosen;
            if (groups.Count == 1)
            {
                var key = groups[0].Key;
                // If there are multiple blocks and none carry an account identifier, do not assume they belong to the same account.
                if (key == null && blocks.Count > 1)
                    return (null, StatementBalanceReasons.MultipleAccounts);

                if (key != null && bankKnown && !groups[0].Any(b => Matches(b, bankKeys)))
                    return (null, StatementBalanceReasons.OtherAccount);
                chosen = blocks;
            }
            else
            {
                var matching = bankKnown
                    ? groups.Where(g => g.Key != null && g.Any(b => Matches(b, bankKeys))).ToList()
                    : new List<IGrouping<string?, StatementBalanceBlock>>();
                if (matching.Count != 1)
                    return (null, StatementBalanceReasons.MultipleAccounts);
                chosen = matching[0].ToList();
            }

            var candidates = chosen.Where(b => b.Booked != null).ToList();
            if (candidates.Count == 0)
                return (null, chosen.Any(b => b.HasNonBookedBalance) ? StatementBalanceReasons.NotBooked : null);

            // A single statement block with conflicting booked closing balances is ambiguous.
            if (chosen.Any(b => b.IsAmbiguous))
                return (null, StatementBalanceReasons.AmbiguousLatest);

            // Keep only candidates whose currency equals the bank's currency.
            var bankCurrency = context?.CurrencyCode?.Trim().ToUpperInvariant();
            var currencyMatched = candidates.Where(c =>
            {
                var ccy = c.Booked!.CurrencyCode?.Trim().ToUpperInvariant();
                return !string.IsNullOrEmpty(ccy) && string.Equals(ccy, bankCurrency, StringComparison.Ordinal);
            }).ToList();

            if (currencyMatched.Count == 0)
            {
                if (candidates.Any(c => !string.IsNullOrWhiteSpace(c.Booked!.CurrencyCode)))
                    return (null, StatementBalanceReasons.CurrencyMismatch);
                return (null, StatementBalanceReasons.CurrencyMissing);
            }

            StatementBalanceBlock winner;
            if (currencyMatched.Count == 1)
            {
                winner = currencyMatched[0];
            }
            else
            {
                if (currencyMatched.Any(c => c.Booked!.AsOfDate == null))
                    return (null, StatementBalanceReasons.AmbiguousLatest);
                var latest = currencyMatched.Max(c => c.Booked!.AsOfDate!.Value);
                var onLatest = currencyMatched.Where(c => c.Booked!.AsOfDate == latest).ToList();
                var distinct = onLatest
                    .Select(c => (c.Booked!.Amount, Ccy: c.Booked.CurrencyCode?.Trim().ToUpperInvariant()))
                    .Distinct().Count();
                if (distinct > 1)
                    return (null, StatementBalanceReasons.AmbiguousLatest);
                winner = onLatest[0];
            }

            var booked = winner.Booked!;
            var currency = booked.CurrencyCode!.Trim().ToUpperInvariant();

            var result = new StatementClosingBalance
            {
                Amount = booked.Amount,
                AsOfDate = booked.AsOfDate,
                CurrencyCode = currency,
                AccountIdentifier = winner.AccountIdentifier
            };
            return (result, booked.AsOfDate == null ? StatementBalanceReasons.DateMissing : null);
        }

        /// <summary>
        /// Checks whether any normalised account key on the block matches any of the bank's account keys.
        /// Two identifiers match when equal, or when the longer ends with the shorter (minimum 4 characters).
        /// </summary>
        private static bool Matches(StatementBalanceBlock block, List<string> bankKeys) =>
            block.AccountKeys.Any(k => bankKeys.Any(bk => IsAccountMatch(k, bk)));
    }
}
