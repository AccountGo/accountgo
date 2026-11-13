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
    [InlineData("3/4/26")]
    public void Csv_AmbiguousNumericSlashDatesAreRefusedWithoutADateFormat(string rawDate)
    {
        var csv = $"d,a\n{rawDate},5.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.False(row.IsValid);
        Assert.Contains("needs a date format", row.Error);
    }

    [Fact]
    public void Csv_DayFirstColumnIsDetectedWithoutUserFormat()
    {
        const string csv = "d,a\n13/04/2026,5.00\n02/03/2026,6.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var rows = BankStatementParser.ParseCsv(csv, map);

        Assert.All(rows, r => Assert.True(r.IsValid, r.Error));
        Assert.Equal(new DateTime(2026, 4, 13), rows[0].PostedDate);
        Assert.Equal(new DateTime(2026, 3, 2), rows[1].PostedDate);
    }

    [Fact]
    public void Csv_Iso8601WithFractionAndZ_NeedsNoFormat()
    {
        const string csv = "d,a\n2024-03-07T04:59:09.447Z,5.00\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1 };

        var row = Assert.Single(BankStatementParser.ParseCsv(csv, map));

        Assert.True(row.IsValid, row.Error);
        Assert.Equal(new DateTime(2024, 3, 7), row.PostedDate);
    }

    [Fact]
    public void Csv_DirectionColumnSignsUnsignedAmount()
    {
        const string csv = "d,a,dir\n2024-01-01,40.00,out\n2024-01-02,10.00,in\n";
        var map = new BankStatementColumnMap { DateColumn = 0, AmountColumn = 1, DirectionColumn = 2 };

        var rows = BankStatementParser.ParseCsv(csv, map);

        Assert.Equal(-40.00m, rows[0].Amount);
        Assert.Equal(10.00m, rows[1].Amount);
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

    // ---------------------------------------------------------------- closing balance (#522)

    private static readonly StatementAccountContext Eur = new("EUR");

    private static string OfxStmt(string acct, string curdef, string balances, string fitid = "F1") =>
        "<STMTRS>" + (curdef.Length > 0 ? $"<CURDEF>{curdef}</CURDEF>" : "") +
        (acct.Length > 0 ? $"<BANKACCTFROM><BANKID>1</BANKID><ACCTID>{acct}</ACCTID></BANKACCTFROM>" : "") +
        $"<BANKTRANLIST><STMTTRN><DTPOSTED>20261002</DTPOSTED><TRNAMT>-4.00</TRNAMT><FITID>{fitid}</FITID><NAME>X</NAME></STMTTRN></BANKTRANLIST>" +
        balances + "</STMTRS>";

    private static string Ledger(string amt, string? dt = "20261002120000[0:GMT]") =>
        $"<LEDGERBAL><BALAMT>{amt}</BALAMT>" + (dt == null ? "" : $"<DTASOF>{dt}</DTASOF>") + "</LEDGERBAL>";

    private static string Avail(string amt, string dt = "20261005") =>
        $"<AVAILBAL><BALAMT>{amt}</BALAMT><DTASOF>{dt}</DTASOF></AVAILBAL>";

    private static string OfxDoc(params string[] statements) =>
        "<OFX><BANKMSGSRSV1>" + string.Concat(statements) + "</BANKMSGSRSV1></OFX>";

    [Fact]
    public void Ofx_ExtractsClosingBalance_FromLedgerBal()
    {
        const string ofx =
            "OFXHEADER:100\nDATA:OFXSGML\n\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>" +
            "<CURDEF>USD\n" +
            "<BANKTRANLIST>\n" +
            "<STMTTRN>\n<TRNTYPE>DEBIT\n<DTPOSTED>20261002\n<TRNAMT>-40.00\n<FITID>F2\n<NAME>Coffee\n</STMTTRN>\n" +
            "</BANKTRANLIST>\n" +
            "<LEDGERBAL>\n<BALAMT>5432.10\n<DTASOF>20261002120000[0:GMT]\n</LEDGERBAL>\n" +
            "</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

        var parsed = BankStatementParser.ParseOfxFile(ofx, "USD");

        Assert.Single(parsed.Rows);
        Assert.NotNull(parsed.ClosingBalance);
        Assert.Equal(5432.10m, parsed.ClosingBalance!.Amount);
        Assert.Equal(new DateTime(2026, 10, 2), parsed.ClosingBalance.AsOfDate);
        Assert.Equal("USD", parsed.ClosingBalance.CurrencyCode);
        Assert.Null(parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenOnlyAvailBalExists()
    {
        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "EUR", Avail("1200.50"))), Eur);

        Assert.Single(parsed.Rows);
        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("not_booked", parsed.ClosingBalanceUnavailableReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ofx_LedgerBalWins_OverAvailBal_RegardlessOfOrder(bool ledgerFirst)
    {
        var balances = ledgerFirst ? Ledger("100.00") + Avail("999.00") : Avail("999.00") + Ledger("100.00");

        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "EUR", balances)), Eur);

        Assert.Equal(100.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Ofx_UsesTheMatchingAccount_WhenFileHasTwoStatements()
    {
        var ofx = OfxDoc(
            OfxStmt("DE001", "EUR", Ledger("111.00"), "A1"),
            OfxStmt("GB002", "USD", Ledger("222.00"), "B1"));

        var parsed = BankStatementParser.ParseOfxFile(ofx, new StatementAccountContext("USD", null, "gb-002"));

        Assert.Equal(222.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal("USD", parsed.ClosingBalance.CurrencyCode);
        Assert.Equal("GB002", parsed.ClosingBalance.AccountIdentifier);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenTwoStatementsAndBankHasNoIdentifiers()
    {
        var ofx = OfxDoc(
            OfxStmt("DE001", "EUR", Ledger("111.00"), "A1"),
            OfxStmt("GB002", "EUR", Ledger("222.00"), "B1"));

        var parsed = BankStatementParser.ParseOfxFile(ofx, Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("multiple_accounts", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenTwoStatementsAndNoneMatchTheBank()
    {
        var ofx = OfxDoc(
            OfxStmt("DE001", "EUR", Ledger("111.00"), "A1"),
            OfxStmt("GB002", "EUR", Ledger("222.00"), "B1"));

        var parsed = BankStatementParser.ParseOfxFile(ofx, new StatementAccountContext("EUR", null, "999"));

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("multiple_accounts", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenSingleStatementIsForAnotherAccount()
    {
        var parsed = BankStatementParser.ParseOfxFile(
            OfxDoc(OfxStmt("DE001", "EUR", Ledger("111.00"))), new StatementAccountContext("EUR", null, "555"));

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("other_account", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_MatchesPartialAccountNumberAtTheEnd()
    {
        var ofx = OfxDoc(OfxStmt("000123456789", "USD", Ledger("4500.00")));

        var matched = BankStatementParser.ParseOfxFile(
            ofx, new StatementAccountContext("USD", null, "6789"));
        Assert.NotNull(matched.ClosingBalance);
        Assert.Equal(4500.00m, matched.ClosingBalance!.Amount);
        Assert.Null(matched.ClosingBalanceUnavailableReason);

        var tooShort = BankStatementParser.ParseOfxFile(
            ofx, new StatementAccountContext("USD", null, "678"));
        Assert.Null(tooShort.ClosingBalance);
        Assert.Equal("other_account", tooShort.ClosingBalanceUnavailableReason);

        var notSuffix = BankStatementParser.ParseOfxFile(
            ofx, new StatementAccountContext("USD", null, "1234"));
        Assert.Null(notSuffix.ClosingBalance);
        Assert.Equal("other_account", notSuffix.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenSeveralStatementsHaveNoAccountId()
    {
        var ofx = OfxDoc(
            OfxStmt("", "USD", Ledger("100.00", "20261001"), fitid: "F1"),
            OfxStmt("", "USD", Ledger("999.00", "20261002"), fitid: "F2"));

        var parsed = BankStatementParser.ParseOfxFile(
            ofx, new StatementAccountContext("USD", null, "123456"));

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("multiple_accounts", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_TrustsTheSelectedBank_WhenSingleStatementHasNoAccountId()
    {
        var parsed = BankStatementParser.ParseOfxFile(
            OfxDoc(OfxStmt("", "EUR", Ledger("111.00"))), new StatementAccountContext("EUR", null, "555"));

        Assert.Equal(111.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenCurrencyIsMissing()
    {
        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "", Ledger("111.00"))), Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("currency_missing", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_OffersNoBalance_WhenCurrencyMismatchesAccount()
    {
        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "USD", Ledger("1200.50"))), Eur);

        Assert.Single(parsed.Rows);
        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("currency_mismatch", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_KeepsNegativeSign()
    {
        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "EUR", Ledger("-75.25"))), Eur);

        Assert.Equal(-75.25m, parsed.ClosingBalance!.Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("20261340")]
    [InlineData("garbage")]
    public void Ofx_OffersAmountWithoutDate_WhenDateIsMissingOrInvalid(string? dt)
    {
        var parsed = BankStatementParser.ParseOfxFile(OfxDoc(OfxStmt("", "EUR", Ledger("50.00", dt))), Eur);

        Assert.Equal(50.00m, parsed.ClosingBalance!.Amount);
        Assert.Null(parsed.ClosingBalance.AsOfDate);
        Assert.Equal("date_missing", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Ofx_ReturnsNullBalance_WhenBalanceTagsMissing()
    {
        const string xml =
            "<?xml version=\"1.0\"?><OFX><BANKTRANLIST><STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20261005</DTPOSTED>" +
            "<TRNAMT>-9.99</TRNAMT><FITID>X1</FITID><NAME>Shop</NAME></STMTTRN></BANKTRANLIST></OFX>";

        var parsed = BankStatementParser.ParseOfxFile(xml, "USD");

        Assert.Single(parsed.Rows);
        Assert.Null(parsed.ClosingBalance);
        Assert.Null(parsed.ClosingBalanceUnavailableReason);
    }

    private static string CamtBal(string? type, string amt, string ccy = "EUR", string ind = "CRDT", string? date = "2026-10-15", string tpPath = "CdOrPrtry") =>
        "<Bal>" + (type == null ? "" : $"<Tp><{tpPath}><Cd>{type}</Cd></{tpPath}></Tp>") +
        $"<Amt Ccy=\"{ccy}\">{amt}</Amt><CdtDbtInd>{ind}</CdtDbtInd>" +
        (date == null ? "" : $"<Dt><{(date.Contains('T') ? "DtTm" : "Dt")}>{date}</{(date.Contains('T') ? "DtTm" : "Dt")}></Dt>") + "</Bal>";

    private static string CamtStmt(string? iban, string balances, string? other = null) =>
        "<Stmt>" + (iban != null || other != null
            ? "<Acct><Id>" + (iban != null ? $"<IBAN>{iban}</IBAN>" : $"<Othr><Id>{other}</Id></Othr>") + "</Id></Acct>"
            : "") +
        balances +
        "<Ntry><Sts>BOOK</Sts><Amt Ccy=\"EUR\">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><BookgDt><Dt>2026-10-14</Dt></BookgDt></Ntry></Stmt>";

    private static string CamtDoc(params string[] stmts) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\"><BkToCstmrStmt>" +
        string.Concat(stmts) + "</BkToCstmrStmt></Document>";

    [Fact]
    public void Camt_ExtractsClosingBalance_FromClbd()
    {
        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, CamtBal("CLBD", "3500.75"))), "EUR");

        Assert.Single(parsed.Rows);
        Assert.Equal(3500.75m, parsed.ClosingBalance!.Amount);
        Assert.Equal(new DateTime(2026, 10, 15), parsed.ClosingBalance.AsOfDate);
        Assert.Equal("EUR", parsed.ClosingBalance.CurrencyCode);
    }

    [Fact]
    public void Camt_ClbdWins_OverClav()
    {
        var parsed = CamtStatementParser.ParseFile(
            CamtDoc(CamtStmt(null, CamtBal("CLAV", "999.00") + CamtBal("CLBD", "10.00"))), Eur);

        Assert.Equal(10.00m, parsed.ClosingBalance!.Amount);
    }

    [Theory]
    [InlineData("CLAV")]
    [InlineData("ITBD")]
    [InlineData("ITAV")]
    [InlineData("OPBD")]
    [InlineData("PRCD")]
    [InlineData("FWAV")]
    public void Camt_OffersNoBalance_ForNonBookedTypes(string type)
    {
        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, CamtBal(type, "10.00"))), Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("not_booked", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_IgnoresClbdOutsideTheBalanceType()
    {
        // CLBD only appears in a proprietary code under a different element, never as Tp/CdOrPrtry/Cd.
        var bal = "<Bal><Tp><CdOrPrtry><Prtry>X</Prtry></CdOrPrtry></Tp><Amt Ccy=\"EUR\">10.00</Amt><CdtDbtInd>CRDT</CdtDbtInd>" +
                  "<Dt><Dt>2026-10-15</Dt></Dt><Avlbty><Cd>CLBD</Cd></Avlbty></Bal>";

        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, bal)), Eur);

        Assert.Null(parsed.ClosingBalance);
    }

    [Fact]
    public void Camt_AssociatesBalanceWithTheRightStatement()
    {
        var doc = CamtDoc(
            CamtStmt("DE00 1111", CamtBal("CLBD", "111.00")),
            CamtStmt("DE00 2222", CamtBal("CLBD", "222.00")));

        var parsed = CamtStatementParser.ParseFile(doc, new StatementAccountContext("EUR", "de002222"));

        Assert.Equal(222.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal("DE00 2222", parsed.ClosingBalance.AccountIdentifier);
    }

    [Fact]
    public void Camt_MatchesOtherIdAgainstBankNumber()
    {
        var doc = CamtDoc(
            CamtStmt(null, CamtBal("CLBD", "111.00"), other: "123-456"),
            CamtStmt(null, CamtBal("CLBD", "222.00"), other: "789"));

        var parsed = CamtStatementParser.ParseFile(doc, new StatementAccountContext("EUR", null, "123456"));

        Assert.Equal(111.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Camt_UsesTheLatestStatement_ForTheSameIban()
    {
        var doc = CamtDoc(
            CamtStmt("DE1", CamtBal("CLBD", "200.00", date: "2026-10-16")),
            CamtStmt("DE1", CamtBal("CLBD", "100.00", date: "2026-10-15")));

        var parsed = CamtStatementParser.ParseFile(doc, new StatementAccountContext("EUR", "DE1"));

        Assert.Equal(200.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal(new DateTime(2026, 10, 16), parsed.ClosingBalance.AsOfDate);
    }

    [Fact]
    public void Camt_OffersNoBalance_WhenLatestDateIsTiedWithDifferentAmounts()
    {
        var doc = CamtDoc(
            CamtStmt("DE1", CamtBal("CLBD", "200.00")),
            CamtStmt("DE1", CamtBal("CLBD", "100.00")));

        var parsed = CamtStatementParser.ParseFile(doc, new StatementAccountContext("EUR", "DE1"));

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("ambiguous_latest", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_DebitClosingBalance_IsNegative()
    {
        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, CamtBal("CLBD", "80.00", ind: "DBIT"))), Eur);

        Assert.Equal(-80.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Camt_OffersNoBalance_WhenCurrencyDiffersFromBank()
    {
        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, CamtBal("CLBD", "80.00", ccy: "USD"))), Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("currency_mismatch", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_PicksTheBankCurrency_WhenOneIbanHasStatementsPerCurrency()
    {
        var doc = CamtDoc(
            CamtStmt("GB29NWBK60161331926819", CamtBal("CLBD", "900.00", ccy: "EUR", date: "2026-10-15")),
            CamtStmt("GB29NWBK60161331926819", CamtBal("CLBD", "1500.00", ccy: "USD", date: "2026-10-15")));

        var usdBank = CamtStatementParser.ParseFile(
            doc, new StatementAccountContext("USD", "GB29NWBK60161331926819"));
        Assert.NotNull(usdBank.ClosingBalance);
        Assert.Equal(1500.00m, usdBank.ClosingBalance!.Amount);
        Assert.Equal("USD", usdBank.ClosingBalance.CurrencyCode);
        Assert.Null(usdBank.ClosingBalanceUnavailableReason);

        var gbpBank = CamtStatementParser.ParseFile(
            doc, new StatementAccountContext("GBP", "GB29NWBK60161331926819"));
        Assert.Null(gbpBank.ClosingBalance);
        Assert.Equal("currency_mismatch", gbpBank.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_MatchesDomesticNumberAtTheEndOfTheIban()
    {
        var doc = CamtDoc(
            CamtStmt("GB29NWBK60161331926819", CamtBal("CLBD", "1500.00", ccy: "EUR")));

        var parsed = CamtStatementParser.ParseFile(
            doc, new StatementAccountContext("EUR", null, "31926819"));

        Assert.NotNull(parsed.ClosingBalance);
        Assert.Equal(1500.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal("EUR", parsed.ClosingBalance.CurrencyCode);
        Assert.Null(parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_OffersNoBalance_WhenOneStatementHasTwoDifferentClosingBalances()
    {
        var doc = CamtDoc(
            CamtStmt(null, CamtBal("CLBD", "100.00", date: "2026-10-15") + CamtBal("CLBD", "200.00", date: "2026-10-15")));

        var parsed = CamtStatementParser.ParseFile(doc, Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("ambiguous_latest", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Camt_KeepsTheCalendarDateAsWritten_ForDtTmWithOffset()
    {
        var parsed = CamtStatementParser.ParseFile(
            CamtDoc(CamtStmt(null, CamtBal("CLBD", "80.00", date: "2026-10-15T00:30:00+07:00"))), Eur);

        Assert.Equal(new DateTime(2026, 10, 15), parsed.ClosingBalance!.AsOfDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-13-40")]
    public void Camt_OffersAmountWithoutDate_WhenDateIsMissingOrInvalid(string? date)
    {
        var parsed = CamtStatementParser.ParseFile(CamtDoc(CamtStmt(null, CamtBal("CLBD", "80.00", date: date))), Eur);

        Assert.Equal(80.00m, parsed.ClosingBalance!.Amount);
        Assert.Null(parsed.ClosingBalance.AsOfDate);
        Assert.Equal("date_missing", parsed.ClosingBalanceUnavailableReason);
    }

    private static string Mt940Stmt(string account, string balanceLines, string reference = "R1") =>
        $":20:{reference}\n:25:{account}\n:28C:1\n:60F:C261001EUR1000,00\n" +
        $":61:2610021002CR250,00NTRF{reference}//456\n:86:Payment\n" + balanceLines + "\n-\n";

    [Fact]
    public void Mt940_ExtractsClosingBalance_FromTag62F()
    {
        var parsed = Mt940StatementParser.ParseFile(Mt940Stmt("123456789", ":62F:C261002EUR1250,00"), "EUR");

        Assert.Single(parsed.Rows);
        Assert.Equal(1250.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal(new DateTime(2026, 10, 2), parsed.ClosingBalance.AsOfDate);
        Assert.Equal("EUR", parsed.ClosingBalance.CurrencyCode);
        Assert.Equal("123456789", parsed.ClosingBalance.AccountIdentifier);
    }

    [Fact]
    public void Mt940_UsesTheStatementOfTheMatchingAccount()
    {
        var file = Mt940Stmt("11111111", ":62F:C261002EUR100,00", "A") + Mt940Stmt("22222222", ":62F:C261002EUR200,00", "B");

        var parsed = Mt940StatementParser.ParseFile(file, new StatementAccountContext("EUR", null, "22222222"));

        Assert.Equal(200.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Mt940_MatchesEachSlashSegmentOfTag25()
    {
        var file = Mt940Stmt("50010517/5424578901", ":62F:C261002EUR100,00", "A")
                   + Mt940Stmt("50010517/9999999999", ":62F:C261002EUR200,00", "B");

        var parsed = Mt940StatementParser.ParseFile(file, new StatementAccountContext("EUR", null, "5424578901"));

        Assert.Equal(100.00m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Mt940_OffersNoBalance_WhenSeveralAccountsAndBankHasNoIdentifiers()
    {
        var file = Mt940Stmt("11111111", ":62F:C261002EUR100,00", "A") + Mt940Stmt("22222222", ":62F:C261002EUR200,00", "B");

        var parsed = Mt940StatementParser.ParseFile(file, Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("multiple_accounts", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Mt940_UsesTheLaterClosingBalance_ForTheSameAccount()
    {
        var file = Mt940Stmt("11111111", ":62F:C261002EUR100,00", "A") + Mt940Stmt("11111111", ":62F:C261003EUR150,00", "B");

        var parsed = Mt940StatementParser.ParseFile(file, new StatementAccountContext("EUR", null, "11111111"));

        Assert.Equal(150.00m, parsed.ClosingBalance!.Amount);
        Assert.Equal(new DateTime(2026, 10, 3), parsed.ClosingBalance.AsOfDate);
    }

    [Fact]
    public void Mt940_OffersNoBalance_ForIntermediateBalanceOnly()
    {
        var parsed = Mt940StatementParser.ParseFile(Mt940Stmt("123", ":62M:C261002EUR100,00\n:64:C261002EUR90,00"), Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("not_booked", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Mt940_DebitClosingBalance_IsNegative()
    {
        var parsed = Mt940StatementParser.ParseFile(Mt940Stmt("123", ":62F:D261002EUR100,50"), Eur);

        Assert.Equal(-100.50m, parsed.ClosingBalance!.Amount);
    }

    [Fact]
    public void Mt940_OffersNoBalance_WhenCurrencyDiffersFromBank()
    {
        var parsed = Mt940StatementParser.ParseFile(Mt940Stmt("123", ":62F:C261002USD100,00"), Eur);

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("currency_mismatch", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Mt940_OffersNoBalance_WhenSingleStatementIsForAnotherAccount()
    {
        var parsed = Mt940StatementParser.ParseFile(
            Mt940Stmt("11111111", ":62F:C261002EUR100,00"), new StatementAccountContext("EUR", null, "99999999"));

        Assert.Null(parsed.ClosingBalance);
        Assert.Equal("other_account", parsed.ClosingBalanceUnavailableReason);
    }

    [Fact]
    public void Mt940_OffersAmountWithoutDate_WhenDateIsImpossible()
    {
        var parsed = Mt940StatementParser.ParseFile(Mt940Stmt("123", ":62F:C261340EUR100,00"), Eur);

        Assert.Equal(100.00m, parsed.ClosingBalance!.Amount);
        Assert.Null(parsed.ClosingBalance.AsOfDate);
        Assert.Equal("date_missing", parsed.ClosingBalanceUnavailableReason);
    }
}
