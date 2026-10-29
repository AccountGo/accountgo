#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Service.Import
{
    /// <summary>
    /// Detect → MapToCanonical seam. Snapshot/balance files fail closed before any adapter maps rows.
    /// </summary>
    public sealed class CoaFormatAdapterRegistry : ICoaFormatAdapterRegistry
    {
        public const string SnapshotMarker = "# AccountGo-COA-Snapshot-v1";
        public const string AdapterNotInstalledSuffix = " See docs/accounting/format-adapters.md.";

        private readonly IReadOnlyList<ICoaFormatAdapter> _adapters;

        public CoaFormatAdapterRegistry(IEnumerable<ICoaFormatAdapter> adapters)
        {
            _adapters = (adapters ?? Enumerable.Empty<ICoaFormatAdapter>()).ToList();
        }

        public CoaAdapterResult Adapt(string csvContent)
        {
            var result = new CoaAdapterResult();
            if (string.IsNullOrWhiteSpace(csvContent))
            {
                result.FileErrors.Add("CSV content is empty.");
                return result;
            }

            var rows = CoaCsvParser.Parse(csvContent);
            if (rows.Count == 0)
            {
                result.FileErrors.Add("CSV file contains no rows.");
                return result;
            }

            // 1) Snapshot / balance-bearing fail closed (§7.1 / §8) before adapters
            foreach (var row in rows.Take(5))
            {
                var first = row.Count > 0 ? row[0].Trim() : "";
                if (first.StartsWith(SnapshotMarker, StringComparison.OrdinalIgnoreCase))
                {
                    result.FormatKey = "accountgo-snapshot-v1";
                    result.FileErrors.Add("Chart of Accounts import only supports structure. Snapshot files containing balances cannot be imported. Opening balances must be loaded via conversion journal (see §8).");
                    return result;
                }
            }

            var header = FindHeader(rows);
            if (header != null)
            {
                var balanceCol = header.FirstOrDefault(h => h.IndexOf("balance", StringComparison.OrdinalIgnoreCase) >= 0);
                if (balanceCol != null)
                {
                    result.FileErrors.Add($"Chart of Accounts import only supports structure. Files containing balance column '{balanceCol}' cannot be imported. Opening balances must be loaded via conversion journal (see §8).");
                    return result;
                }
            }

            // 2) Prefer installed AccountGo adapter, then stubs, by registration order among CanHandle matches
            var accountGo = _adapters.FirstOrDefault(a => a.FormatKey == "accountgo-v1");
            if (accountGo != null && accountGo.CanHandle(rows))
                return Invoke(accountGo, rows);

            foreach (var adapter in _adapters.Where(a => a.FormatKey != "accountgo-v1"))
            {
                if (!adapter.CanHandle(rows))
                    continue;
                return Invoke(adapter, rows);
            }

            // Marker that is not AccountGo / Snapshot
            foreach (var row in rows.Take(5))
            {
                var first = row.Count > 0 ? row[0].Trim() : "";
                if (first.StartsWith("#") && !first.StartsWith(AccountGoV1CoaFormatAdapter.Marker, StringComparison.OrdinalIgnoreCase))
                {
                    result.FileErrors.Add($"Unsupported file format marker '{first}'. Expected {AccountGoV1CoaFormatAdapter.Marker}. Unknown adapters: convert to AccountGo Structure CSV or see docs/accounting/format-adapters.md.");
                    return result;
                }
            }

            result.FileErrors.Add($"Unrecognized Chart of Accounts file format. Expected {AccountGoV1CoaFormatAdapter.Marker} Structure CSV. Xero and QuickBooks Online adapters are documented stubs only — see docs/accounting/format-adapters.md.");
            return result;
        }

        private static CoaAdapterResult Invoke(ICoaFormatAdapter adapter, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            if (!adapter.IsInstalled)
            {
                return new CoaAdapterResult
                {
                    FormatKey = adapter.FormatKey,
                    DisplayName = adapter.DisplayName,
                    FileErrors =
                    {
                        $"{adapter.DisplayName} adapter is not installed. Export Structure from AccountGo or convert your file to {AccountGoV1CoaFormatAdapter.Marker}.{AdapterNotInstalledSuffix}"
                    }
                };
            }

            var mapped = adapter.MapToCanonical(rows);
            mapped.FormatKey = adapter.FormatKey;
            mapped.DisplayName ??= adapter.DisplayName;
            return mapped;
        }

        private static IReadOnlyList<string>? FindHeader(IReadOnlyList<IReadOnlyList<string>> rows)
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
    }
}
