// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Services.Banking
{
    /// <summary>
    /// Reads the first worksheet of an .xlsx into a CSV-shaped string so existing column detection applies (#447 Phase C).
    /// </summary>
    public static class XlsxStatementReader
    {
        /// <summary>OpenXml part character budget (~2M) — keeps a shared container from expanding a malicious sheet.</summary>
        public const long MaxCharactersInPart = 2_000_000;

        public static string ToCsv(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("The statement file is empty.");
            if (bytes.Length > BankStatementParser.MaxUploadBytes)
                throw new InvalidOperationException(
                    $"The statement file is too large (max {BankStatementParser.MaxUploadBytes / (1024 * 1024)} MB).");

            using var stream = new MemoryStream(bytes, writable: false);
            var openSettings = new OpenSettings { MaxCharactersInPart = MaxCharactersInPart };
            using var doc = SpreadsheetDocument.Open(stream, false, openSettings);
            var workbook = doc.WorkbookPart
                ?? throw new InvalidOperationException("The Excel file has no workbook.");
            var sheet = workbook.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
                ?? throw new InvalidOperationException("The Excel file has no sheets.");
            var sheetId = sheet.Id?.Value
                ?? throw new InvalidOperationException("The Excel sheet reference is missing.");
            var worksheet = (WorksheetPart)workbook.GetPartById(sheetId);
            var sharedCache = BuildSharedStringCache(workbook.SharedStringTablePart?.SharedStringTable);
            var sheetData = worksheet.Worksheet.GetFirstChild<SheetData>()
                ?? throw new InvalidOperationException("The Excel sheet is empty.");

            var rows = new List<List<string>>();
            foreach (var row in sheetData.Elements<Row>())
            {
                if (rows.Count >= BankStatementParser.MaxRows + 1) // header + MaxRows data
                    break;
                var cells = new List<string>();
                var expectedCol = 0;
                foreach (var cell in row.Elements<Cell>())
                {
                    var colIndex = ColumnIndex(cell.CellReference?.Value) ?? expectedCol;
                    while (expectedCol < colIndex)
                    {
                        cells.Add(string.Empty);
                        expectedCol++;
                    }
                    cells.Add(GetCellText(cell, sharedCache));
                    expectedCol = colIndex + 1;
                }
                if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                    rows.Add(cells);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("No data rows were found in the Excel file.");

            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(EscapeCsv(row[i]));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static IReadOnlyList<string> BuildSharedStringCache(SharedStringTable? shared)
        {
            if (shared == null)
                return Array.Empty<string>();
            return shared.Elements<SharedStringItem>().Select(i => i.InnerText ?? string.Empty).ToList();
        }

        private static string GetCellText(Cell cell, IReadOnlyList<string> sharedCache)
        {
            if (cell.DataType?.Value == CellValues.InlineString)
                return cell.InlineString?.InnerText ?? string.Empty;

            var value = cell.CellValue?.InnerText ?? string.Empty;
            if (cell.DataType?.Value == CellValues.SharedString
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < sharedCache.Count)
            {
                return sharedCache[index];
            }
            if (cell.DataType?.Value == CellValues.Boolean)
                return value == "1" ? "TRUE" : "FALSE";
            return value;
        }

        private static int? ColumnIndex(string? cellRef)
        {
            if (string.IsNullOrEmpty(cellRef))
                return null;
            var letters = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
            if (letters.Length == 0)
                return null;
            var index = 0;
            foreach (var ch in letters.ToUpperInvariant())
                index = index * 26 + (ch - 'A' + 1);
            return index - 1;
        }

        private static string EscapeCsv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
    }
}
