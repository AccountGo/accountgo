// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Services.Financial
{
    /// <summary>
    /// Canonical SHA-256 of the close inputs. Equal-and-opposite replacements must change the digest.
    /// Localized text, request time, and UI capabilities are not part of the input.
    /// </summary>
    public static class YearEndPreviewHash
    {
        public const int PolicyVersion = 1;

        public static string Compute(
            int companyId,
            int financialYearId,
            long accountingRevision,
            DateTime startDate,
            DateTime endDate,
            DateTime workspaceToday,
            string? timeZoneId,
            int entityType,
            int? permanentAccountId,
            DateTime? booksLockedThrough,
            IEnumerable<YearEndCheckItem> checks,
            IEnumerable<YearEndClosingLine> lines)
        {
            var builder = new StringBuilder();
            builder.Append("v").Append(PolicyVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append("|c").Append(companyId.ToString(CultureInfo.InvariantCulture));
            builder.Append("|y").Append(financialYearId.ToString(CultureInfo.InvariantCulture));
            builder.Append("|r").Append(accountingRevision.ToString(CultureInfo.InvariantCulture));
            builder.Append("|s").Append(Date(startDate));
            builder.Append("|e").Append(Date(endDate));
            builder.Append("|d").Append(Date(workspaceToday));
            builder.Append("|z").Append(timeZoneId ?? "");
            builder.Append("|t").Append(entityType.ToString(CultureInfo.InvariantCulture));
            builder.Append("|p").Append(permanentAccountId?.ToString(CultureInfo.InvariantCulture) ?? "");
            builder.Append("|l").Append(booksLockedThrough.HasValue ? Date(booksLockedThrough.Value) : "");
            foreach (var check in checks.OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                builder.Append("|k").Append(check.Code)
                    .Append(':').Append(check.State)
                    .Append(':').Append(check.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(Money(check.Amount));
            }
            foreach (var line in lines.OrderBy(l => l.AccountId).ThenBy(l => l.DrCr, StringComparer.Ordinal).ThenBy(l => l.Amount))
            {
                builder.Append("|n").Append(line.AccountId.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(line.DrCr)
                    .Append(':').Append(Money(line.Amount));
            }
            return Hex(builder.ToString());
        }

        public static string RequestDigest(string fiscalYearCode, bool acknowledgeWarnings, string? reason, string? previewHash)
        {
            var text = string.Join("|",
                fiscalYearCode ?? "",
                acknowledgeWarnings ? "1" : "0",
                (reason ?? "").Trim(),
                (previewHash ?? "").Trim().ToLowerInvariant());
            return Hex(text);
        }

        public static string Hex(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Money(decimal? amount) =>
            (amount ?? 0m).ToString("0.0000", CultureInfo.InvariantCulture);
    }

    public static class YearEndCheckCatalog
    {
        public static readonly string[] TierA =
        {
            "fy_not_ended",
            "earlier_year_missing",
            "earlier_year_open",
            "permanent_account_invalid",
            "ledger_unbalanced",
            "opening_balance_equity",
            "draft_invoices",
            "draft_bills",
            "unposted_journals",
            "unposted_issued_documents",
            "ar_ap_control_tieout_failed",
            "partner_equity_invalid"
        };

        public static readonly string[] TierB =
        {
            "banks_unreconciled",
            "unmatched_statement_lines",
            "suspense_clearing",
            "stale_ar_ap_as_of_cutoff",
            "wrong_side_balances"
        };

        public static IReadOnlyList<YearEndCheckItem> Complete(
            IReadOnlyList<YearEndCheckItem> blocking,
            IReadOnlyList<YearEndCheckItem> warnings,
            IReadOnlyDictionary<string, string> notApplicable)
        {
            var failed = blocking.Concat(warnings).ToDictionary(item => item.Code, item => item);
            var all = new List<YearEndCheckItem>();
            foreach (var code in TierA.Concat(TierB))
            {
                if (failed.TryGetValue(code, out var failure))
                {
                    failure.Tier = TierA.Contains(code) ? "A" : "B";
                    failure.State = "Fail";
                    all.Add(failure);
                    continue;
                }
                if (notApplicable.TryGetValue(code, out var reason))
                {
                    all.Add(new YearEndCheckItem
                    {
                        Code = code,
                        Tier = TierA.Contains(code) ? "A" : "B",
                        State = "NotApplicable",
                        Message = reason,
                        Count = 0
                    });
                    continue;
                }
                all.Add(new YearEndCheckItem
                {
                    Code = code,
                    Tier = TierA.Contains(code) ? "A" : "B",
                    State = "Pass",
                    Message = "",
                    Count = 0
                });
            }
            return all;
        }
    }
}
