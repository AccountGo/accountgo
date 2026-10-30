// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Dto.Banking;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>Anonymised sample exports — detection + parse for #447 Phase A.</summary>
public class StatementCorpusTests
{
    private static string CorpusPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Banking", "StatementCorpus", fileName);

    private static string ReadCorpus(string fileName) => File.ReadAllText(CorpusPath(fileName));

    private static BankStatementColumnMap WiseMap(string content)
    {
        var records = BankStatementParser.SplitCsvRecords(content, ',');
        var headerIndex = StatementColumnDetector.DetectHeaderRow(records)
            ?? throw new InvalidOperationException("Wise header missing.");
        var preset = StatementImportPresets.FindMatching(records[headerIndex])
            ?? throw new InvalidOperationException("Wise preset missing.");
        return preset.Bind(records[headerIndex]);
    }

    [Fact]
    public void WiseActivity_IsoDatesDirectionCurrencyAndStatus()
    {
        var content = ReadCorpus("wise-activity-usd.csv");
        var resolved = StatementImportResolver.Resolve(content, WiseMap(content), accountCurrencyCode: "USD");

        Assert.Equal(StatementColumnDetector.DateKind.Iso8601, resolved.Map.DateDetection!.Kind);
        Assert.False(resolved.Summary.NeedsDateFormatChoice);
        Assert.Contains("ISO 8601", resolved.Summary.Date, StringComparison.Ordinal);
        Assert.Contains("signed amount", resolved.Summary.Amount, StringComparison.Ordinal);

        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.Equal(5, rows.Count);

        var usdIn = rows[0];
        Assert.True(usdIn.IsValid, usdIn.Error);
        Assert.Equal(new DateTime(2024, 3, 7), usdIn.PostedDate);
        Assert.Equal(1250.50m, usdIn.Amount);
        Assert.Equal("ACT-USD-001", usdIn.ExternalId);

        var usdOut = rows[1];
        Assert.True(usdOut.IsValid, usdOut.Error);
        Assert.Equal(-40.00m, usdOut.Amount);

        Assert.False(rows[2].IsValid);
        Assert.Contains("PHP row skipped", rows[2].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USD", rows[2].Error, StringComparison.Ordinal);

        // Neutral transfer still imports via signed_amount; status filter drops pending.
        Assert.True(rows[3].IsValid, rows[3].Error);
        Assert.Equal(100.00m, rows[3].Amount);

        Assert.False(rows[4].IsValid);
        Assert.Contains("pending row skipped", rows[4].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DayFirstClear_DetectedWithoutUserFormat()
    {
        var content = ReadCorpus("day-first-clear.csv");
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2 };
        var resolved = StatementImportResolver.Resolve(content, map);
        Assert.Equal(StatementColumnDetector.DateKind.DayFirstNumeric, resolved.Map.DateDetection!.Kind);
        Assert.False(resolved.Summary.NeedsDateFormatChoice);

        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(new DateTime(2024, 1, 13), rows[0].PostedDate);
        Assert.Equal(new DateTime(2024, 12, 31), rows[2].PostedDate);
    }

    [Fact]
    public void AmbiguousDates_RequireChoice_NeverGuess()
    {
        var content = ReadCorpus("ambiguous-dates.csv");
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2 };
        var resolved = StatementImportResolver.Resolve(content, map);
        Assert.True(resolved.Summary.NeedsDateFormatChoice);
        Assert.True(resolved.Map.NeedsDateFormatChoice);

        var withoutChoice = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.All(withoutChoice, r => Assert.False(r.IsValid));
        Assert.All(withoutChoice, r => Assert.Contains("needs a date format", r.Error, StringComparison.OrdinalIgnoreCase));

        map.DateFormat = "dd/MM/yyyy";
        var dayFirst = StatementImportResolver.Resolve(content, map);
        Assert.False(dayFirst.Summary.NeedsDateFormatChoice);
        var rows = BankStatementParser.ParseCsv(content, dayFirst.Map, dayFirst.Records);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(new DateTime(2024, 4, 3), rows[0].PostedDate);
    }

    [Theory]
    [InlineData("(1,234.56)", -1234.56, null)]
    [InlineData("1234.56-", -1234.56, null)]
    [InlineData("1,234.56 CR", 1234.56, null)]
    [InlineData("1,234.56 DR", -1234.56, null)]
    [InlineData("$1,234.56", 1234.56, null)]
    [InlineData("1.234,56 €", 1234.56, ",")]
    [InlineData("1,466.01 PHP", 1466.01, ".")]
    public void AmountVariants_SharedParser(string raw, double expected, string? decimalSeparator)
    {
        char? sep = string.IsNullOrEmpty(decimalSeparator) ? null : decimalSeparator[0];
        Assert.Equal((decimal)expected, BankStatementParser.ParseAmount(raw, "Amount", sep));
    }

    [Fact]
    public void AmountVariantsFile_ParsesAllRows()
    {
        var content = ReadCorpus("amount-variants.csv");
        // File mixes US and European clear signals → user must pick; pin US for this corpus.
        var map = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            PayeeColumn = 2,
            DecimalSeparator = "."
        };
        var rows = BankStatementParser.ParseCsv(content, map);
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(-1234.56m, rows[0].Amount);
        Assert.Equal(-1234.56m, rows[1].Amount);
        Assert.Equal(1234.56m, rows[2].Amount);
        Assert.Equal(-1234.56m, rows[3].Amount);
        Assert.Equal(1234.56m, rows[4].Amount);
        Assert.Equal(1466.01m, rows[5].Amount);
    }

    [Fact]
    public void EuropeanAmounts_ColumnDecimalSeparator()
    {
        var content = ReadCorpus("european-amounts.csv");
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2 };
        var resolved = StatementImportResolver.Resolve(content, map);
        Assert.False(resolved.Summary.NeedsAmountFormatChoice);
        Assert.Equal(',', resolved.Map.AmountDecimalSeparator);

        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(1500.00m, rows[0].Amount); // 1.500 thousands grouping
        Assert.Equal(12.50m, rows[1].Amount);
        Assert.Equal(2000.00m, rows[2].Amount);
    }

    [Fact]
    public void EmbeddedCurrency_MustMatchAccount()
    {
        const string csv = "Date,Amount,Payee\n2024-01-01,\"1,466.01 PHP\",Vendor\n";
        var map = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            PayeeColumn = 2,
            DecimalSeparator = "."
        };
        var resolved = StatementImportResolver.Resolve(csv, map, accountCurrencyCode: "USD");
        var row = Assert.Single(BankStatementParser.ParseCsv(csv, resolved.Map, resolved.Records));
        Assert.False(row.IsValid);
        Assert.Contains("PHP row skipped", row.Error, StringComparison.OrdinalIgnoreCase);

        var matched = StatementImportResolver.Resolve(csv, map, accountCurrencyCode: "PHP");
        var ok = Assert.Single(BankStatementParser.ParseCsv(csv, matched.Map, matched.Records));
        Assert.True(ok.IsValid, ok.Error);
        Assert.Equal(1466.01m, ok.Amount);
    }

    [Fact]
    public void WiseRealHeaders_PresetBindsByName()
    {
        var content = ReadCorpus("wise-activity-real-headers.csv");
        var headers = content.Split('\n')[0].Trim().Split(',');
        var preset = StatementImportPresets.FindMatching(headers);
        Assert.NotNull(preset);
        Assert.Equal("Wise activity", preset!.Name);
        var map = preset.Bind(headers);
        var resolved = StatementImportResolver.Resolve(content, map, accountCurrencyCode: "USD");
        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal("Received money from Client Co", rows[0].Payee);
        Assert.Equal("Invoice payment", rows[0].Memo);
        Assert.Equal(-40.00m, rows[1].Amount);
        Assert.Equal("Sent money to Vendor LLC", rows[1].Payee);
    }

    [Fact]
    public void AmbiguousAmountColumn_RequiresChoice()
    {
        const string csv = "Date,Amount\n2024-01-01,1.500\n2024-01-02,2.000\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };
        var resolved = StatementImportResolver.Resolve(csv, map);
        Assert.True(resolved.Summary.NeedsAmountFormatChoice);
        var rows = BankStatementParser.ParseCsv(csv, resolved.Map, resolved.Records);
        Assert.All(rows, r => Assert.False(r.IsValid));
        Assert.All(rows, r => Assert.Contains("decimal separator", r.Error, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreambleHeader_DetectedAndParsed()
    {
        var content = ReadCorpus("preamble-header.csv");
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2, HasHeaderRow = true };
        var resolved = StatementImportResolver.Resolve(content, map);
        Assert.Equal(3, resolved.Map.HeaderRowIndex);
        Assert.Equal("row 4", resolved.Summary.Header);

        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(10.00m, rows[0].Amount);
        Assert.Equal(-3.50m, rows[1].Amount);
    }

    [Fact]
    public void Encoding_Utf16AndWindows1252RoundTripThroughDecodeThenParse()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string csv = "Date,Amount,Payee\n2024-01-01,5.00,Café ñ\n";

        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(csv)).ToArray();
        var fromUtf16 = StatementFileEncoding.Decode(utf16);
        var rowUtf16 = Assert.Single(BankStatementParser.ParseCsv(fromUtf16,
            new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2 }));
        Assert.True(rowUtf16.IsValid, rowUtf16.Error);
        Assert.Equal("Café ñ", rowUtf16.Payee);

        var win1252 = Encoding.GetEncoding(1252).GetBytes("Date,Amount,Payee\n2024-01-01,5.00,José\n");
        var from1252 = StatementFileEncoding.Decode(win1252);
        var row1252 = Assert.Single(BankStatementParser.ParseCsv(from1252,
            new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, PayeeColumn = 2 }));
        Assert.True(row1252.IsValid, row1252.Error);
        Assert.Equal("José", row1252.Payee);
    }
}
