// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Dto.Banking;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankStatementParserTests
{
    private static BankStatementColumnMap StandardMap() => new()
    {
        DateColumn = 0,
        AmountColumn = 1,
        PayeeColumn = 2,
        MemoColumn = 3,
        ReferenceColumn = 4
    };

    [Fact]
    public void Csv_ParsesHeaderQuotedFieldsAndSignedAmounts()
    {
        const string csv =
            "Date,Amount,Payee,Memo,Ref\r\n" +
            "2026-10-01,\"1,250.50\",\"Acme, Inc\",\"Invoice \"\"A\"\"\",1001\r\n" +
            "2026-10-02,(40.00),Coffee Shop,Latte,\r\n" +
            "\r\n" +
            "2026-10-03,12.30-,Fee,,\r\n";

        var rows = BankStatementParser.ParseCsv(csv, StandardMap());

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsValid));
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal("Acme, Inc", rows[0].Payee);
        Assert.Equal("Invoice \"A\"", rows[0].Memo);
        Assert.Equal("1001", rows[0].Reference);
        Assert.Equal(new DateTime(2026, 10, 1), rows[0].PostedDate);
        Assert.Equal(-40.00m, rows[1].Amount);
        Assert.Equal(-12.30m, rows[2].Amount);
        Assert.StartsWith("csv:", rows[0].ExternalId);
    }

    [Fact]
    public void Csv_SupportsSemicolonDelimiterAndCustomDateFormat()
    {
        const string csv = "Datum;Betrag;Name\n31/10/2026;-5.00;Bakery\n";
        var map = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            PayeeColumn = 2,
            DateFormat = "dd/MM/yyyy"
        };

        var rows = BankStatementParser.ParseCsv(csv, map);

        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2026, 10, 31), row.PostedDate);
        Assert.Equal(-5.00m, row.Amount);
        Assert.Equal("Bakery", row.Payee);
    }

    [Theory]
    [InlineData("03/04/2026")]
    [InlineData("13/04/2026")]
    [InlineData("3/4/26")]
    public void Csv_NumericSlashDatesAreRefusedWithoutADateFormat(string rawDate)
    {
        var csv = $"d,a\n{rawDate},5.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.False(row.IsValid);
        Assert.Contains("needs a date format", row.Error);
    }

    [Fact]
    public void Csv_AmbiguousDateIsReadTheWayTheColumnMapSays()
    {
        const string csv = "d,a\n03/04/2026,5.00\n";

        var monthFirst = Assert.Single(BankStatementParser.ParseCsv(
            csv, new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, DateFormat = "MM/dd/yyyy" }));
        var dayFirst = Assert.Single(BankStatementParser.ParseCsv(
            csv, new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, DateFormat = "dd/MM/yyyy" }));

        Assert.Equal(new DateTime(2026, 3, 4), monthFirst.PostedDate);
        Assert.Equal(new DateTime(2026, 4, 3), dayFirst.PostedDate);
    }

    [Fact]
    public void Csv_DateThatDoesNotMatchTheGivenFormatIsAnError()
    {
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, DateFormat = "dd/MM/yyyy" };

        var row = Assert.Single(BankStatementParser.ParseCsv("d,a\n2026-10-01,5.00\n", map));

        Assert.False(row.IsValid);
        Assert.Contains("dd/MM/yyyy", row.Error);
    }

    [Fact]
    public void Csv_SkipsLeadingBlankLinesBeforeTheHeader()
    {
        const string csv = "\n\nDate,Amount\n2026-10-01,5.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, HasHeaderRow = true };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal(new DateTime(2026, 10, 1), row.PostedDate);
        Assert.Equal(5.00m, row.Amount);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\\t")]
    public void Csv_AcceptsTabDelimiter(string delimiter)
    {
        const string csv = "Date\tAmount\n2026-10-01\t5.00\n";
        var map = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            Delimiter = delimiter,
            HasHeaderRow = true
        };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal(5.00m, row.Amount);
    }

    [Theory]
    [InlineData("\n\nDate\tAmount\n2026-10-01\t5.00\n")]
    [InlineData("\r\n\r\nDate;Amount\r\n2026-10-01;5.00\r\n")]
    public void Csv_AutoDetectsDelimiterFromFirstNonBlankLine(string csv)
    {
        // Delimiter null → auto-detect. A leading blank must not make tab/semicolon files fall back to comma
        // (which would leave Amount in column 0 and fail this parse).
        var map = new BankStatementColumnMap
        {
            DateColumn = 0,
            AmountColumn = 1,
            Delimiter = null,
            HasHeaderRow = true
        };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal(5.00m, row.Amount);
    }

    [Theory]
    [InlineData("2026-10-01")]
    [InlineData("2026/10/01")]
    [InlineData("20261001")]
    [InlineData("1-Oct-2026")]
    [InlineData("2026-10-01 08:30:00")]
    public void Csv_UnambiguousDatesNeedNoFormat(string rawDate)
    {
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var row = Assert.Single(BankStatementParser.ParseCsv($"d,a\n{rawDate},5.00\n", map));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal(new DateTime(2026, 10, 1), row.PostedDate);
    }

    [Fact]
    public void Csv_DepositAndWithdrawalColumns_AndInvertSign()
    {
        const string csv = "d,dep,wd\n2026-10-01,100.00,\n2026-10-02,,25.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, DepositColumn = 1, WithdrawalColumn = 2 };

        var rows = BankStatementParser.ParseCsv(csv, map);
        Assert.Equal(100.00m, rows[0].Amount);
        Assert.Equal(-25.00m, rows[1].Amount);

        map.InvertSign = true;
        var inverted = BankStatementParser.ParseCsv(csv, map);
        Assert.Equal(-100.00m, inverted[0].Amount);
        Assert.Equal(25.00m, inverted[1].Amount);
    }

    [Fact]
    public void Csv_BadRowsAreFlagged_NotThrown()
    {
        const string csv = "d,a\nnot-a-date,5\n2026-10-02,abc\n2026-10-03,0\n2026-10-04,7\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var rows = BankStatementParser.ParseCsv(csv, map);

        Assert.Equal(4, rows.Count);
        Assert.Equal(3, rows.Count(r => !r.IsValid));
        Assert.Contains("date format", rows[0].Error);
        Assert.Contains("not a number", rows[1].Error);
        Assert.Contains("zero", rows[2].Error);
        Assert.True(rows[3].IsValid);
    }

    [Fact]
    public void Csv_IdenticalRowsGetDistinctSyntheticIds_AndExternalIdColumnWins()
    {
        const string csv = "d,a,id\n2026-10-01,5,\n2026-10-01,5,\n2026-10-02,6,TX9\n";
        var withoutId = BankStatementParser.ParseCsv(csv, new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 });
        Assert.NotEqual(withoutId[0].ExternalId, withoutId[1].ExternalId);
        Assert.EndsWith("#2", withoutId[1].ExternalId);

        var withId = BankStatementParser.ParseCsv(csv, new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, ExternalIdColumn = 2 });
        Assert.Equal("TX9", withId[2].ExternalId);
    }

    [Fact]
    public void Csv_RequiresAmountMappingAndRows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BankStatementParser.ParseCsv("d\n2026-10-01\n", new BankStatementColumnMap { DateColumn = 0 }));
        Assert.Throws<InvalidOperationException>(() =>
            BankStatementParser.ParseCsv("d,a\n", new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 }));
        Assert.Throws<InvalidOperationException>(() =>
            BankStatementParser.Parse(Core.Domain.BankStatementSources.Csv, "  ", StandardMap()));
    }

    [Fact]
    public void Ofx_ParsesSgmlTransactions()
    {
        const string ofx =
            "OFXHEADER:100\nDATA:OFXSGML\n\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>\n" +
            "<STMTTRN>\n<TRNTYPE>CREDIT\n<DTPOSTED>20261001120000[0:GMT]\n<TRNAMT>1250.50\n<FITID>F1\n<NAME>Acme &amp; Co\n<MEMO>Invoice 7\n<CHECKNUM>1001\n</STMTTRN>\n" +
            "<STMTTRN>\n<TRNTYPE>DEBIT\n<DTPOSTED>20261002\n<TRNAMT>-40.00\n<FITID>F2\n<NAME>Coffee\n" +
            "</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

        var rows = BankStatementParser.ParseOfx(ofx);

        Assert.Equal(2, rows.Count);
        Assert.Equal("F1", rows[0].ExternalId);
        Assert.Equal(new DateTime(2026, 10, 1), rows[0].PostedDate);
        Assert.Equal(1250.50m, rows[0].Amount);
        Assert.Equal("Acme & Co", rows[0].Payee);
        Assert.Equal("Invoice 7", rows[0].Memo);
        Assert.Equal("1001", rows[0].Reference);
        Assert.Equal(-40.00m, rows[1].Amount);
        Assert.Equal("F2", rows[1].ExternalId);
    }

    [Fact]
    public void Ofx_ParsesXmlTransactions_AndRejectsFilesWithoutTransactions()
    {
        const string xml =
            "<?xml version=\"1.0\"?><OFX><BANKTRANLIST><STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20261005</DTPOSTED>" +
            "<TRNAMT>-9.99</TRNAMT><FITID>X1</FITID><NAME>Shop</NAME></STMTTRN></BANKTRANLIST></OFX>";

        var row = Assert.Single(BankStatementParser.ParseOfx(xml));
        Assert.Equal(-9.99m, row.Amount);
        Assert.Equal("X1", row.ExternalId);

        Assert.Throws<InvalidOperationException>(() => BankStatementParser.ParseOfx("<OFX></OFX>"));
    }

    [Theory]
    [InlineData("-0,5", -0.5)]
    [InlineData("-100.5", -100.5)]
    [InlineData("-45.0000", -45.0000)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("1,234", 1234)]
    [InlineData("12,50", 12.50)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-0.50", -0.50)]
    [InlineData("-1,234.56", -1234.56)]
    [InlineData("1234.56", 1234.56)]
    public void Ofx_AmountNormalizesUsAndEuropeanGrouping(string raw, double expected)
    {
        var ofx = $"<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20261001<TRNAMT>{raw}<FITID>T1<NAME>X</STMTTRN></BANKTRANLIST></OFX>";

        var row = Assert.Single(BankStatementParser.ParseOfx(ofx));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal((decimal)expected, row.Amount);
    }
}
