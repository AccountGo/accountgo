// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Parsing;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentTextFieldParserTests
{
    private static TextLine Line(string text, int page = 1, float y0 = 0, float y1 = 16) =>
        new(page, text, 0, y0, 400, y1, TextLineSource.Ocr);

    private static JsonElement ParseAndValidate(string kind, IReadOnlyList<TextLine> lines, out IReadOnlyList<string> warnings)
    {
        var result = DocumentTextFieldParser.Parse(kind, lines);
        warnings = result.Warnings;
        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.True(
            DocumentCaptureFieldSchemas.TryValidate(kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion, doc.RootElement, out _, out var err),
            $"Schema validation failed for kind '{kind}': {err}. JSON={result.FieldsJson}");
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("Date: 2026-11-20", "2026-11-20")]
    [InlineData("Date: 2026/09/30", "2026-09-30")]
    [InlineData("Date: 21/11/2026", "2026-11-21")]
    [InlineData("Date: 03.10.2026", "2026-10-03")]
    [InlineData("Date: 11/19/2026", "2026-11-19")]
    [InlineData("Date: 15 Oct 2026", "2026-10-15")]
    [InlineData("Date: 19 November 2026", "2026-11-19")]
    [InlineData("Date: Nov 3, 2026", "2026-11-03")]
    [InlineData("Date: October 1, 2026", "2026-10-01")]
    [InlineData("Date: Nov 21 2026", "2026-11-21")]
    [InlineData("Date: 15 Oct 26", "2026-10-15")]
    [InlineData("วันที่: 20/11/2569", "2026-11-20")]
    [InlineData("วันที่: 5 พฤศจิกายน 2569", "2026-11-05")]
    public void Parse_Dates_HandlesSupportedFormats(string line, string expectedIso)
    {
        var root = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line(line), Line("Total: EUR 10.00")], out _);
        Assert.Equal(expectedIso, root.GetProperty("date").GetString());
    }

    [Fact]
    public void Parse_Date_PrefersLabelledDateOverEarlierDateAndParsesDueDateSeparately()
    {
        var lines = new[]
        {
            Line("Acme Ltd"),
            Line("Printed 01/01/2026"),
            Line("Invoice Date: 15/11/2026"),
            Line("Payment Due: 15/12/2026"),
            Line("Total: EUR 100.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Equal("2026-11-15", root.GetProperty("date").GetString());
        Assert.Equal("2026-12-15", root.GetProperty("dueDate").GetString());
    }

    [Fact]
    public void Parse_Date_IgnoresDateLikeAccountNumbers()
    {
        var lines = new[]
        {
            Line("Acme Ltd"),
            Line("Ref: 12-03-2026-8899"),
            Line("Total: EUR 25.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.False(root.TryGetProperty("date", out _));
    }

    [Theory]
    [InlineData("Grand Total: $1,234.56", "1234.56")]
    [InlineData("Total Amount: €1 234.56", "1234.56")]
    [InlineData("TOTAL: €1.234,56", "1234.56")]
    [InlineData("Amount Due: RM 89.50", "89.50")]
    [InlineData("ยอดรวมทั้งสิ้น: ฿3,188.60", "3188.60")]
    [InlineData("Total: S$2,354.40", "2354.40")]
    [InlineData("Total: Rp 150,000.00", "150000.00")]
    public void Parse_Amounts_HandlesFormatsAndCurrencyPrefixes(string totalLine, string expectedTotal)
    {
        var root = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line(totalLine)], out _);
        Assert.Equal(expectedTotal, root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
    }

    [Fact]
    public void Parse_Totals_ExcludesSubtotalTaxCashChangeAndPhoneNumbers()
    {
        var lines = new[]
        {
            Line("Corner Cafe"),
            Line("Tel: 02-123.45"),
            Line("Subtotal: EUR 100.00"),
            Line("Tax 10%: EUR 10.00"),
            Line("TOTAL:"),
            Line("EUR 110.00"),
            Line("Cash: EUR 200.00"),
            Line("Change: EUR 90.00"),
            Line("Total Qty: 5"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("100.00", totals.GetProperty("subtotal").GetString());
        Assert.Equal("10.00", totals.GetProperty("taxTotal").GetString());
        Assert.Equal("110.00", totals.GetProperty("grandTotal").GetString());
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
    }

    [Fact]
    public void Parse_Totals_WarnsWhenSubtotalPlusTaxDiffersFromGrandTotal()
    {
        var lines = new[]
        {
            Line("Acme Ltd"),
            Line("Subtotal: EUR 100.00"),
            Line("Tax: EUR 10.00"),
            Line("Total: EUR 125.00"),
        };

        _ = ParseAndValidate("purchase-invoice", lines, out var warnings);
        Assert.Contains("TOTALS_INCONSISTENT", warnings);
    }

    [Fact]
    public void Parse_Currency_PrefersIsoCodeAndWarnsAmbiguousOnBareDollar()
    {
        var isoRoot = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line("Total: $50.00 CAD")], out var isoWarnings);
        Assert.Equal("CAD", isoRoot.GetProperty("currency").GetString());
        Assert.DoesNotContain("CURRENCY_AMBIGUOUS", isoWarnings);

        var bareDollar = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line("Total: $50.00")], out var dollarWarnings);
        Assert.False(bareDollar.TryGetProperty("currency", out _));
        Assert.Contains("CURRENCY_AMBIGUOUS", dollarWarnings);
    }

    [Fact]
    public void Parse_Vendor_PenalizesHeadersAndAddressesAndBoostsLegalSuffixAndHeight()
    {
        var lines = new[]
        {
            Line("TAX INVOICE", y0: 0, y1: 14),
            Line("COPY", y0: 16, y1: 28),
            Line("123 Example Road, Unit 4", y0: 30, y1: 42),
            Line("Tel: 555-0199", y0: 44, y1: 56),
            Line("Northwind Office Ltd", y0: 60, y1: 92),
            Line("Total: EUR 40.00", y0: 100, y1: 114),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Equal("Northwind Office Ltd", root.GetProperty("vendorName").GetString());
    }

    [Fact]
    public void Parse_SalesInvoice_ExtractsCustomerFromBillToNotHeader()
    {
        var lines = new[]
        {
            Line("Our Seller Company Inc.", y0: 0, y1: 30),
            Line("TAX INVOICE", y0: 32, y1: 48),
            Line("Invoice No.: INV-2026-88", y0: 50, y1: 64),
            Line("Bill To:", y0: 70, y1: 84),
            Line("Blue Harbor Retail LLC", y0: 86, y1: 100),
            Line("45 Market Street", y0: 102, y1: 116),
            Line("Date: 2026-11-20", y0: 120, y1: 134),
            Line("Total: USD 500.00", y0: 140, y1: 154),
        };

        var root = ParseAndValidate("sales-invoice", lines, out _);
        Assert.Equal("Blue Harbor Retail LLC", root.GetProperty("customerName").GetString());
        Assert.Equal("INV-2026-88", root.GetProperty("sourceInvoiceNo").GetString());
        Assert.False(root.TryGetProperty("paymentHints", out _));
    }

    [Fact]
    public void Parse_Lines_ExtractsClearTableRowsAndOmitsUnverifiedRows()
    {
        var lines = new[]
        {
            Line("Blue Bean Coffee Supply"),
            Line("Date: 2026-11-20"),
            Line("Espresso beans 2 $25.00 $50.00"),
            Line("Paper cups 1 $50.00 $50.00"),
            Line("Bad math row 3 $10.00 $99.00"),
            Line("Total: USD 100.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        var parsedLines = root.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, parsedLines.Count);
        Assert.Equal("Espresso beans", parsedLines[0].GetProperty("description").GetString());
        Assert.Equal("2", parsedLines[0].GetProperty("quantity").GetString());
        Assert.Equal("25.00", parsedLines[0].GetProperty("unitAmount").GetString());
        Assert.Equal("50.00", parsedLines[0].GetProperty("sourceLineTotal").GetString());
    }

    [Fact]
    public void Parse_PaymentHints_DetectsPaidUnpaidUnknownAndExplicitAmountPaid()
    {
        var paid = ParseAndValidate("expense-receipt", [
            Line("Cafe"),
            Line("Total: EUR 20.00"),
            Line("PAID BY CASH"),
            Line("Amount Paid: EUR 20.00"),
        ], out _);
        Assert.Equal("paid", paid.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
        Assert.Equal("20.00", paid.GetProperty("paymentHints").GetProperty("amountPaid").GetString());

        var unpaid = ParseAndValidate("purchase-invoice", [
            Line("Supplier Ltd"),
            Line("Amount Due: EUR 150.00"),
        ], out _);
        Assert.Equal("unpaid", unpaid.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
        Assert.False(unpaid.GetProperty("paymentHints").TryGetProperty("amountPaid", out _));

        var unknown = ParseAndValidate("purchase-invoice", [
            Line("Supplier Ltd"),
            Line("Total: EUR 150.00"),
        ], out _);
        Assert.Equal("unknown", unknown.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public void Parse_Bank_ValidatesChecksumsAndRequiresLabels()
    {
        var validLines = new[]
        {
            Line("Bank Name: First Test Bank"),
            Line("Account Holder: Acme Operations Ltd"),
            Line("Account Number: 000123456789"),
            Line("Routing Number: 021000021"),
            Line("IBAN: DE89 3704 0044 0532 0130 00"),
            Line("SWIFT / BIC: COBADEFFXXX"),
        };

        var root = ParseAndValidate("bank", validLines, out _);
        Assert.Equal("First Test Bank", root.GetProperty("bankName").GetString());
        Assert.Equal("Acme Operations Ltd", root.GetProperty("name").GetString());
        Assert.Equal("000123456789", root.GetProperty("accountNumber").GetString());
        Assert.Equal("021000021", root.GetProperty("routingNumber").GetString());
        Assert.Equal("DE89370400440532013000", root.GetProperty("iban").GetString());
        Assert.Equal("COBADEFFXXX", root.GetProperty("swift").GetString());
    }

    [Fact]
    public void Parse_Bank_RejectsInvalidChecksumsAndUnlabelledNumbers()
    {
        var badLines = new[]
        {
            Line("DE89 3704 0044 0532 0130 00"), // unlabelled IBAN
            Line("021000021"), // unlabelled routing
            Line("000123456789"), // unlabelled account
            Line("IBAN: DE00 3704 0044 0532 0130 00"), // bad mod-97
            Line("Routing Number: 021000022"), // bad ABA checksum
            Line("SWIFT: NOT-A-SWIFT"), // bad format
        };

        var root = ParseAndValidate("bank", badLines, out _);
        Assert.False(root.TryGetProperty("iban", out _));
        Assert.False(root.TryGetProperty("routingNumber", out _));
        Assert.False(root.TryGetProperty("accountNumber", out _));
        Assert.False(root.TryGetProperty("swift", out _));
    }

    [Fact]
    public void Parse_AssistantAttachment_ExtractsCappedLabelValuePairs()
    {
        var lines = Enumerable.Range(1, 70)
            .Select(i => Line($"Field {i}: Value {i}"))
            .ToList();

        var root = ParseAndValidate("assistant-attachment", lines, out _);
        var fields = root.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(DocumentCaptureFieldSchemas.MaxAssistantFields, fields.Count);
        Assert.Equal("Field 1", fields[0].GetProperty("label").GetString());
        Assert.Equal("Value 1", fields[0].GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("expense-receipt")]
    [InlineData("purchase-invoice")]
    [InlineData("sales-invoice")]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    public void Parse_HostileInputs_ReturnSchemaValidJsonWithin50Ms(string kind)
    {
        var hostileSets = new TextLine[][]
        {
            Array.Empty<TextLine>(),
            [Line("\u202E\u200B\uFEFF\u200F   \u0000")],
            [Line(new string('A', 8_000))],
            [Line(new string('9', 10_000))],
            [Line(new string('[', 2_000) + "Total: 10.00" + new string(']', 2_000))],
            [Line("a: " + new string('x', 5_000))],
        };

        foreach (var set in hostileSets)
        {
            var sw = Stopwatch.StartNew();
            _ = ParseAndValidate(kind, set, out _);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 50, $"Kind '{kind}' took {sw.ElapsedMilliseconds}ms on hostile input.");
        }
    }

    [Fact]
    public void Parse_SyntheticTruthCleanText_Scores18Of18OnVendorDateAndTotal()
    {
        var truthPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "test", "AccountGo.BackendTests", "Fixtures", "DocumentCapture", "Synthetic", "truth.json"));
        Assert.True(File.Exists(truthPath), $"Expected synthetic truth.json at {truthPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(truthPath));
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(18, entries.Count);

        var vendorHits = 0;
        var dateHits = 0;
        var totalHits = 0;

        foreach (var entry in entries)
        {
            var vendor = entry.GetProperty("vendor").GetString()!;
            var invoiceNo = entry.GetProperty("invoiceNo").GetString()!;
            var isoDate = entry.GetProperty("date").GetString()!;
            var subtotal = entry.GetProperty("subtotal").GetString()!;
            var tax = entry.GetProperty("tax").GetString()!;
            var total = entry.GetProperty("total").GetString()!;
            var currency = entry.GetProperty("currency").GetString()!;
            var lang = entry.GetProperty("lang").GetString()!;
            var layout = entry.GetProperty("layout").GetString()!;

            var isThai = lang == "th";
            var sym = currency switch { "EUR" => "€", "SGD" => "S$", "THB" => "฿", _ => "$" };
            var kind = layout == "invoice" ? "purchase-invoice" : "expense-receipt";

            var lines = new[]
            {
                Line(vendor, y0: 20, y1: 46),
                Line("123 Example Road, Unit 4", y0: 48, y1: 64),
                Line(isThai ? "ใบกำกับภาษี / ใบเสร็จรับเงิน" : (layout == "invoice" ? "TAX INVOICE" : "RECEIPT"), y0: 70, y1: 88),
                Line($"{(isThai ? "เลขที่" : (layout == "invoice" ? "Invoice No." : "Receipt #"))}: {invoiceNo}", y0: 92, y1: 108),
                Line($"{(isThai ? "วันที่" : "Date")}: {isoDate}", y0: 110, y1: 126),
                Line($"{(isThai ? "รวมเป็นเงิน" : "Subtotal")}: {sym}{subtotal}", y0: 180, y1: 196),
                Line($"{(isThai ? "ภาษีมูลค่าเพิ่ม 7%" : "Tax")}: {sym}{tax}", y0: 198, y1: 214),
                Line($"{(isThai ? "ยอดรวมทั้งสิ้น" : "TOTAL")}: {sym}{total}", y0: 220, y1: 242),
            };

            var parsed = ParseAndValidate(kind, lines, out _);
            if (parsed.TryGetProperty("vendorName", out var v) && v.GetString() == vendor) vendorHits++;
            if (parsed.TryGetProperty("date", out var d) && d.GetString() == isoDate) dateHits++;
            if (parsed.TryGetProperty("sourceTotals", out var st)
                && st.TryGetProperty("grandTotal", out var gt)
                && gt.GetString() == total) totalHits++;
        }

        Assert.Equal(18, vendorHits);
        Assert.Equal(18, dateHits);
        Assert.Equal(18, totalHits);
    }
}
