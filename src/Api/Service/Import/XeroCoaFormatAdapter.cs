#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Service.Import
{
    /// <summary>
    /// Detection stub for Xero Chart of Accounts CSV exports.
    /// Full mapping is documented in docs/accounting/format-adapters.md; parser is not installed.
    /// </summary>
    public sealed class XeroCoaFormatAdapter : ICoaFormatAdapter
    {
        public const string NotInstalledMessage =
            "Xero Chart of Accounts adapter is not installed. Export Structure from AccountGo or convert your file to # AccountGo-COA-v1. See docs/accounting/format-adapters.md.";

        public string FormatKey => "xero";
        public string DisplayName => "Xero Chart of Accounts";
        public bool IsInstalled => false;

        public bool CanHandle(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var header = FirstDataHeader(rows);
            if (header == null) return false;
            var normalized = header.Select(Normalize).ToList();
            // Xero export uses *Code / *Name / *Type (and variants with "Account")
            bool hasCode = normalized.Any(h => h is "code" or "*code" or "accountcode" or "*accountcode");
            bool hasName = normalized.Any(h => h is "name" or "*name" or "accountname" or "*accountname");
            bool hasType = normalized.Any(h => h is "type" or "*type" or "accounttype" or "*accounttype");
            bool looksAccountGo = normalized.Any(h => h == "accountclass") && normalized.Any(h => h == "accountcode");
            return hasCode && hasName && hasType && !looksAccountGo;
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
            (h ?? "").Trim().TrimStart('*').Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}
