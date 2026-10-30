// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Dto.Banking;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementExtraFormatsTests
{
    private static string Corpus(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Banking", "StatementCorpus", name));

    [Fact]
    public void Qif_ParsesBankTransactions()
    {
        var rows = QifStatementParser.Parse(Corpus("sample.qif"));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        // File includes 03/13/2024 → month-first for the whole file (not silent 4 March from 03/04).
        Assert.Equal(new DateTime(2024, 3, 13), rows[0].PostedDate);
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal("Client Co", rows[0].Payee);
        Assert.Equal(-40.00m, rows[1].Amount);
    }

    [Fact]
    public void Qif_AmbiguousDatesRequireChoice()
    {
        const string qif = "!Type:Bank\nD03/04/26\nT5.00\nPAlpha\n^\nD05/06/26\nT-2.00\nPBeta\n^\n";
        var rows = QifStatementParser.Parse(qif);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(r.IsValid));
        Assert.All(rows, r => Assert.Contains("needs a date format", r.Error, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Mt940_ParsesCreditAndDebit()
    {
        var rows = Mt940StatementParser.Parse(Corpus("sample.mt940"));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(new DateTime(2024, 3, 7), rows[0].PostedDate);
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal(-40.00m, rows[1].Amount);
        Assert.Contains("CLIENT CO", rows[0].Payee, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Camt053_ParsesEntries()
    {
        var rows = CamtStatementParser.Parse(Corpus("sample.camt053.xml"));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal("CAMT-001", rows[0].ExternalId);
        Assert.Equal(-40.00m, rows[1].Amount);
        Assert.Equal("Client Co", rows[0].Payee);
    }

    [Fact]
    public void Xlsx_FirstSheetBecomesCsvAndParses()
    {
        var bytes = BuildMinimalXlsx();
        var csv = XlsxStatementReader.ToCsv(bytes);
        Assert.Contains("Date,Amount,Payee", csv, StringComparison.Ordinal);

        var rows = BankStatementParser.ParseCsv(csv, new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            PayeeColumn = 2
        });
        var row = Assert.Single(rows);
        Assert.True(row.IsValid, row.Error);
        Assert.Equal(new DateTime(2024, 3, 7), row.PostedDate);
        Assert.Equal(12.50m, row.Amount);
        Assert.Equal("Cafe", row.Payee);
    }

    [Fact]
    public void Upload_RejectsOversizedBase64BeforeDecode()
    {
        using var h = new BankingTestHarness();
        h.SeedCashTree();
        var bank = h.Banks.Create(new CreateBankAccountRequest { Name = "Big", Type = 1 }, includeFullNumbers: false);
        // ~6 MB of 'A' as base64 payload text → approx decoded size over the 5 MB cap.
        var oversized = new string('A', (BankStatementParser.MaxUploadBytes * 4 / 3) + 64);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            h.Statements.Preview(bank.Id, new BankStatementImportRequest
            {
                Source = 1,
                ContentBase64 = oversized,
                ColumnMap = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 }
            }));
        Assert.Contains("too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildMinimalXlsx()
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetData.Append(RowOf("A1", "Date", "B1", "Amount", "C1", "Payee"));
            sheetData.Append(RowOf("A2", "2024-03-07", "B2", "12.50", "C2", "Cafe"));
            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1"
            });
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static Row RowOf(params string[] refsAndValues)
    {
        var row = new Row();
        for (var i = 0; i + 1 < refsAndValues.Length; i += 2)
        {
            row.Append(new Cell
            {
                CellReference = refsAndValues[i],
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(refsAndValues[i + 1]))
            });
        }
        return row;
    }
}
