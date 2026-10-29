#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Service.Import
{
    /// <summary>Production adapter for <c># AccountGo-COA-v1</c> Structure CSV.</summary>
    public sealed class AccountGoV1CoaFormatAdapter : ICoaFormatAdapter
    {
        public const string Marker = "# AccountGo-COA-v1";
        public const string DocsPath = "docs/accounting/format-adapters.md";

        public string FormatKey => "accountgo-v1";
        public string DisplayName => "AccountGo Chart of Accounts (v1)";
        public bool IsInstalled => true;

        public bool CanHandle(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            bool sawForeignMarker = false;
            foreach (var row in rows.Take(5))
            {
                var first = row.Count > 0 ? row[0].Trim() : "";
                if (!first.StartsWith("#")) continue;
                if (first.StartsWith(Marker, StringComparison.OrdinalIgnoreCase))
                    return true;
                sawForeignMarker = true;
            }

            // Header-only AccountGo files (no foreign marker) with required columns
            if (sawForeignMarker) return false;
            var header = FindHeaderRow(rows);
            if (header == null) return false;
            var headers = header.Select(h => h.Trim()).ToList();
            bool hasCode = headers.Any(h => h.Equals("AccountCode", StringComparison.OrdinalIgnoreCase) || h.Equals("Code", StringComparison.OrdinalIgnoreCase));
            bool hasName = headers.Any(h => h.Equals("AccountName", StringComparison.OrdinalIgnoreCase) || h.Equals("Name", StringComparison.OrdinalIgnoreCase));
            bool hasClass = headers.Any(h => h.Equals("AccountClass", StringComparison.OrdinalIgnoreCase) || h.Equals("Class", StringComparison.OrdinalIgnoreCase));
            return hasCode && hasName && hasClass;
        }

        public CoaAdapterResult MapToCanonical(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var result = new CoaAdapterResult
            {
                FormatKey = FormatKey,
                DisplayName = DisplayName
            };

            int headerRowIndex = 0;
            bool sawMarker = false;
            for (int i = 0; i < rows.Count && i < 5; i++)
            {
                var firstCell = rows[i].Count > 0 ? rows[i][0].Trim() : "";
                if (firstCell.StartsWith("#"))
                {
                    if (!firstCell.StartsWith(Marker, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FileErrors.Add($"Unsupported file format marker '{firstCell}'. Expected {Marker}.");
                        return result;
                    }
                    sawMarker = true;
                    headerRowIndex = i + 1;
                    break;
                }
            }

            if (!sawMarker)
            {
                // Accept header-only AccountGo structure for round-trip convenience, but warn.
                result.FileWarnings.Add($"Missing format marker. Prefer files that start with '{Marker}'.");
                headerRowIndex = IndexOfHeaderRow(rows);
                if (headerRowIndex < 0)
                {
                    result.FileErrors.Add("CSV file contains no header row.");
                    return result;
                }
            }

            if (headerRowIndex >= rows.Count)
            {
                result.FileErrors.Add("CSV file contains no header row.");
                return result;
            }

            var headers = rows[headerRowIndex].Select(h => h.Trim()).ToList();

            var balanceCol = headers.FirstOrDefault(h => h.IndexOf("balance", StringComparison.OrdinalIgnoreCase) >= 0);
            if (balanceCol != null)
            {
                result.FileErrors.Add($"Chart of Accounts import only supports structure. Files containing balance column '{balanceCol}' cannot be imported. Opening balances must be loaded via conversion journal (see §8).");
                return result;
            }

            int codeIdx = headers.FindIndex(h => h.Equals("AccountCode", StringComparison.OrdinalIgnoreCase) || h.Equals("Code", StringComparison.OrdinalIgnoreCase));
            int nameIdx = headers.FindIndex(h => h.Equals("AccountName", StringComparison.OrdinalIgnoreCase) || h.Equals("Name", StringComparison.OrdinalIgnoreCase) || h.Equals("Account", StringComparison.OrdinalIgnoreCase));
            int parentIdx = headers.FindIndex(h => h.Equals("ParentAccountCode", StringComparison.OrdinalIgnoreCase) || h.Equals("ParentCode", StringComparison.OrdinalIgnoreCase) || h.Equals("Parent", StringComparison.OrdinalIgnoreCase));
            int classIdx = headers.FindIndex(h => h.Equals("AccountClass", StringComparison.OrdinalIgnoreCase) || h.Equals("Class", StringComparison.OrdinalIgnoreCase));
            int contraIdx = headers.FindIndex(h => h.Equals("Contra", StringComparison.OrdinalIgnoreCase) || h.Equals("IsContraAccount", StringComparison.OrdinalIgnoreCase) || h.Equals("ContraAccount", StringComparison.OrdinalIgnoreCase));
            int cashIdx = headers.FindIndex(h => h.Equals("Cash", StringComparison.OrdinalIgnoreCase) || h.Equals("IsCash", StringComparison.OrdinalIgnoreCase));
            int descIdx = headers.FindIndex(h => h.Equals("Description", StringComparison.OrdinalIgnoreCase));
            int statusIdx = headers.FindIndex(h => h.Equals("Status", StringComparison.OrdinalIgnoreCase));
            int taxIdx = headers.FindIndex(h => h.Equals("DefaultTaxCode", StringComparison.OrdinalIgnoreCase) || h.Equals("TaxCode", StringComparison.OrdinalIgnoreCase));
            int currIdx = headers.FindIndex(h => h.Equals("Currency", StringComparison.OrdinalIgnoreCase));

            if (codeIdx < 0 || nameIdx < 0 || classIdx < 0)
            {
                var missing = new List<string>();
                if (codeIdx < 0) missing.Add("AccountCode");
                if (nameIdx < 0) missing.Add("AccountName");
                if (classIdx < 0) missing.Add("AccountClass");
                result.FileErrors.Add($"Missing required columns in CSV header: {string.Join(", ", missing)}.");
                return result;
            }

            if (taxIdx >= 0)
                result.FileWarnings.Add("Column 'DefaultTaxCode' is reserved in v1 and will be ignored.");
            if (currIdx >= 0)
                result.FileWarnings.Add("Column 'Currency' is reserved in v1 and will be ignored.");

            for (int r = headerRowIndex + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;

                int rowNum = r + 1;
                var errors = new List<string>();
                var warnings = new List<string>();

                string code = (codeIdx < row.Count ? row[codeIdx] : "").Trim();
                string name = (nameIdx < row.Count ? row[nameIdx] : "").Trim();
                string parentCode = (parentIdx >= 0 && parentIdx < row.Count ? row[parentIdx] : "").Trim();
                string classVal = (classIdx < row.Count ? row[classIdx] : "").Trim();
                string contraVal = (contraIdx >= 0 && contraIdx < row.Count ? row[contraIdx] : "").Trim();
                string cashVal = (cashIdx >= 0 && cashIdx < row.Count ? row[cashIdx] : "").Trim();
                string desc = (descIdx >= 0 && descIdx < row.Count ? row[descIdx] : "").Trim();
                string status = (statusIdx >= 0 && statusIdx < row.Count ? row[statusIdx] : "").Trim();

                if (string.IsNullOrWhiteSpace(code))
                    errors.Add("AccountCode is required.");
                if (string.IsNullOrWhiteSpace(name))
                    errors.Add("AccountName is required.");

                int classId = 0;
                string className = "Unknown";
                if (!CoaAccountClassMap.TryResolve(classVal, out classId, out className))
                {
                    errors.Add($"Invalid AccountClass '{classVal}'. Must be 1-6 or Assets, Liabilities, Equity, Revenue, Expenses, Closing accounts.");
                }

                bool isContra = contraVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || contraVal.Equals("1") || contraVal.Equals("yes", StringComparison.OrdinalIgnoreCase);
                bool isCash = cashVal.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || cashVal.Equals("1") || cashVal.Equals("yes", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(status))
                    status = "Active";
                else if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase) && !status.Equals("Archived", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Unknown status '{status}'. Defaulting to Active.");
                    status = "Active";
                }
                else
                {
                    status = status.Equals("Archived", StringComparison.OrdinalIgnoreCase) ? "Archived" : "Active";
                }

                if (!string.IsNullOrWhiteSpace(code) && char.IsDigit(code[0]) && classId > 0)
                {
                    int firstDigit = code[0] - '0';
                    bool matchesRange = (classId == 1 && firstDigit == 1) ||
                                        (classId == 2 && firstDigit == 2) ||
                                        (classId == 3 && firstDigit == 3) ||
                                        (classId == 4 && firstDigit == 4) ||
                                        (classId == 5 && firstDigit == 5) ||
                                        (classId == 6 && firstDigit == 9);
                    if (!matchesRange)
                    {
                        warnings.Add($"Account code '{code}' does not match standard numbering range for class '{className}'.");
                    }
                }

                result.Rows.Add(new CoaCanonicalRow
                {
                    RowNumber = rowNum,
                    AccountCode = code,
                    AccountName = name,
                    ParentAccountCode = parentCode,
                    AccountClassId = classId,
                    AccountClassName = className,
                    IsContraAccount = isContra,
                    IsCash = isCash,
                    Description = desc,
                    Status = status,
                    Errors = errors,
                    Warnings = warnings
                });
            }

            return result;
        }

        private static IReadOnlyList<string>? FindHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            int idx = IndexOfHeaderRow(rows);
            return idx >= 0 ? rows[idx] : null;
        }

        private static int IndexOfHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var first = rows[i].Count > 0 ? rows[i][0].Trim() : "";
                if (first.StartsWith("#")) continue;
                if (rows[i].Any(c => !string.IsNullOrWhiteSpace(c)))
                    return i;
            }
            return -1;
        }
    }
}
