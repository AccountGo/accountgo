#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Service.Import
{
    /// <summary>
    /// Detection stub for QuickBooks Online Chart of Accounts import/export spreadsheets.
    /// Full mapping is documented in docs/accounting/format-adapters.md; parser is not installed.
    /// </summary>
    public sealed class QboCoaFormatAdapter : ICoaFormatAdapter
    {
        public const string NotInstalledMessage =
            "QuickBooks Online Chart of Accounts adapter is not installed. Export Structure from AccountGo or convert your file to # AccountGo-COA-v1. See docs/accounting/format-adapters.md.";

        public string FormatKey => "qbo";
        public string DisplayName => "QuickBooks Online Chart of Accounts";
        public bool IsInstalled => false;

        public bool CanHandle(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var header = FirstDataHeader(rows);
            if (header == null) return false;
            var normalized = header.Select(Normalize).ToList();
            bool hasAccount = normalized.Any(h => h is "account" or "accountname");
            bool hasType = normalized.Contains("type");
            bool hasDetailType = normalized.Any(h => h is "detailtype" or "detail type");
            bool looksAccountGo = normalized.Contains("accountcode") && normalized.Contains("accountclass");
            // QBO template: Account Name / Type / Detail Type (Number optional)
            return hasAccount && hasType && hasDetailType && !looksAccountGo;
        }

        public CoaAdapterResult MapToCanonical(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            return new CoaAdapterResult
            {
                FormatKey = FormatKey,
                DisplayName = DisplayName,
                FileErrors = { NotInstalledMessage }
            };
        }

        private static IReadOnlyList<string>? FirstDataHeader(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            foreach (var row in rows)
            {
                var first = row.Count > 0 ? row[0].Trim() : "";
                if (first.StartsWith("#")) continue;
                if (row.Any(c => !string.IsNullOrWhiteSpace(c)))
                    return row;
            }
            return null;
        }

        private static string Normalize(string h) =>
            (h ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}
