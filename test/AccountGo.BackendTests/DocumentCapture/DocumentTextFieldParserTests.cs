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
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentTextFieldParserTests(ITestOutputHelper output)
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
    [Trait("Rule", "D-01")]
    [Trait("Rule", "D-02")]
    public void Parse_Dates_HandlesSupportedFormats(string line, string expectedIso)
    {
        var root = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line(line), Line("Total: EUR 10.00")], out _);
        Assert.Equal(expectedIso, root.GetProperty("date").GetString());
    }

    [Fact]
    [Trait("Rule", "D-03")]
    [Trait("Rule", "D-04")]
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
    [Trait("Rule", "D-02")]
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
    [Trait("Rule", "T-01")]
    public void Parse_Amounts_HandlesFormatsAndCurrencyPrefixes(string totalLine, string expectedTotal)
    {
        var root = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line(totalLine)], out _);
        Assert.Equal(expectedTotal, root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
    }

    [Fact]
    [Trait("Rule", "T-01")]
    [Trait("Rule", "T-03")]
    [Trait("Rule", "T-04")]
    [Trait("Rule", "T-05")]
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
    [Trait("Rule", "T-08")]
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
    [Trait("Rule", "C-01")]
    public void Parse_Currency_PrefersIsoCodeAndWarnsAmbiguousOnBareDollar()
    {
        var isoRoot = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line("Total: $50.00 CAD")], out var isoWarnings);
        Assert.Equal("CAD", isoRoot.GetProperty("currency").GetString());
        Assert.DoesNotContain("AMBIGUOUS_CURRENCY", isoWarnings);

        var bareDollar = ParseAndValidate("purchase-invoice", [Line("Acme Ltd"), Line("Total: $50.00")], out var dollarWarnings);
        Assert.False(bareDollar.TryGetProperty("currency", out _));
        Assert.Contains("AMBIGUOUS_CURRENCY", dollarWarnings);
    }

    [Fact]
    [Trait("Rule", "V-03")]
    [Trait("Rule", "V-04")]
    [Trait("Rule", "V-05")]
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
    [Trait("Rule", "V-06")]
    [Trait("Rule", "N-01")]
    [Trait("Rule", "P-01")]
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
    [Trait("Rule", "L-01")]
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
    [Trait("Rule", "P-01")]
    [Trait("Rule", "P-02")]
    [Trait("Rule", "P-03")]
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
    [Trait("Rule", "B-01")]
    [Trait("Rule", "B-02")]
    [Trait("Rule", "B-03")]
    [Trait("Rule", "B-04")]
    [Trait("Rule", "B-05")]
    [Trait("Rule", "B-06")]
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
    [Trait("Rule", "B-01")]
    [Trait("Rule", "B-02")]
    [Trait("Rule", "B-03")]
    [Trait("Rule", "B-04")]
    [Trait("Rule", "B-05")]
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
    [Trait("Rule", "A-01")]
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
    [Trait("Rule", "G-02")]
    [Trait("Rule", "G-03")]
    public void Parse_HostileInputs_ReturnSchemaValidJsonWithin2000MsWarmed(string kind)
    {
        // Warm regexes and JIT once before timing (K1/R2).
        _ = ParseAndValidate(kind, [Line("Warmup Co"), Line("Date: 2026-11-20"), Line("Total: EUR 10.00")], out _);

        static string RepeatToLength(string chunk, int targetLength)
        {
            var count = (targetLength / chunk.Length) + 1;
            return string.Concat(Enumerable.Repeat(chunk, count))[..targetLength];
        }

        var hostileSets = new TextLine[][]
        {
            Array.Empty<TextLine>(),
            [Line("\u202E\u200B\uFEFF\u200F   \u0000")],
            [Line(new string('A', 8_000))],
            [Line(new string('9', 10_000))],
            [Line(new string('[', 2_000) + "Total: 10.00" + new string(']', 2_000))],
            [Line("a: " + new string('x', 5_000))],
            [Line(RepeatToLength("a 1 1.00 ", 8_000))],
            [Line(RepeatToLength("a 1 $1.00 ", 8_000))],
        };

        var sw = Stopwatch.StartNew();
        foreach (var set in hostileSets)
        {
            _ = ParseAndValidate(kind, set, out var warnings);
            Assert.DoesNotContain("PARSE_TRUNCATED", warnings);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds <= 2_000, $"Kind '{kind}' took {sw.ElapsedMilliseconds}ms across hostile inputs (ceiling 2000ms).");
    }

    // --- K2: paymentStatus strong cues, negations, and positive amount due precedence ---

    [Fact]
    [Trait("Rule", "P-01")]
    [Trait("Rule", "P-03")]
    public void Parse_PaymentHints_P2_SubjectToChangeAndAmountDueRemainsUnpaid()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Amount Due: EUR 500.00"),
            Line("Prices subject to change without notice"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        var hints = root.GetProperty("paymentHints");
        Assert.Equal("unpaid", hints.GetProperty("paymentStatus").GetString());
        Assert.False(hints.TryGetProperty("amountPaid", out _));
    }

    [Fact]
    [Trait("Rule", "P-01")]
    public void Parse_PaymentHints_P3_NotPaidWithin30DaysDoesNotTriggerPaid()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Total: EUR 200.00"),
            Line("Late fee applies to invoices not paid within 30 days"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Equal("unknown", root.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
    }

    [Fact]
    [Trait("Rule", "P-03")]
    public void Parse_PaymentHints_PositiveAmountDueWithExplicitAmountPaidIsPartial()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Total: EUR 200.00"),
            Line("Amount Paid: EUR 50.00"),
            Line("Amount Due: EUR 150.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        var hints = root.GetProperty("paymentHints");
        Assert.Equal("partial", hints.GetProperty("paymentStatus").GetString());
        Assert.Equal("50.00", hints.GetProperty("amountPaid").GetString());
    }

    [Theory]
    [InlineData("PAID")]
    [InlineData("PAID IN FULL")]
    [InlineData("PAYMENT RECEIVED")]
    [InlineData("CASH 50.00")]
    [InlineData("CHANGE 7.50")]
    [InlineData("Balance Due: EUR 0.00")]
    [Trait("Rule", "P-01")]
    public void Parse_PaymentHints_StrongPaidCuesSetPaid(string cueLine)
    {
        var root = ParseAndValidate("expense-receipt", [Line("Corner Cafe"), Line("Total: EUR 42.50"), Line(cueLine)], out _);
        Assert.Equal("paid", root.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
    }

    // --- K3: dueDate label strictness and next-line digit guard ---

    [Fact]
    [Trait("Rule", "D-03")]
    [Trait("Rule", "D-04")]
    public void Parse_Date_P1_AmountDueDoesNotTriggerDueDateOnNextLine()
    {
        var lines = new[]
        {
            Line("Acme Ltd"),
            Line("Amount Due: 50.00"),
            Line("Date: 2026-11-20"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Equal("2026-11-20", root.GetProperty("date").GetString());
        Assert.False(root.TryGetProperty("dueDate", out _));
    }

    [Fact]
    [Trait("Rule", "D-04")]
    public void Parse_Date_DueDateReadsNextLineOnlyWhenLabelLineHasNoDigits()
    {
        var withNextLine = ParseAndValidate("purchase-invoice", [
            Line("Acme Ltd"),
            Line("Due Date:"),
            Line("2026-12-20"),
            Line("Total: EUR 50.00"),
        ], out _);
        Assert.Equal("2026-12-20", withNextLine.GetProperty("dueDate").GetString());

        var thaiDue = ParseAndValidate("purchase-invoice", [
            Line("Acme Ltd"),
            Line("ครบกำหนด: 20/12/2569"),
            Line("Total: THB 50.00"),
        ], out _);
        Assert.Equal("2026-12-20", thaiDue.GetProperty("dueDate").GetString());
    }

    // --- K4: tax registration exclusion and next-line digit guard ---

    [Fact]
    [Trait("Rule", "T-06")]
    public void Parse_Totals_P5_VatRegNoDoesNotLeakSubtotalIntoTaxTotal()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("VAT Reg No: 12345678"),
            Line("Subtotal: EUR 100.00"),
            Line("Total: EUR 100.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("100.00", totals.GetProperty("subtotal").GetString());
        Assert.Equal("100.00", totals.GetProperty("grandTotal").GetString());
        Assert.False(totals.TryGetProperty("taxTotal", out _));
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
    }

    [Fact]
    [Trait("Rule", "T-04")]
    public void Parse_Totals_LabelLineWithDigitsDoesNotFallbackToNextLine()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Tax 10%"),
            Line("Subtotal: EUR 100.00"),
            Line("Total: EUR 100.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.False(totals.TryGetProperty("taxTotal", out _));
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
    }

    // --- K5 & K6: priceBasis inclusive cues, inclusive-tax total line, and TOTAL_UNLABELED ---

    [Fact]
    [Trait("Rule", "T-08")]
    public void Parse_Totals_P8_ServiceChargeIncludedDoesNotSetPriceBasisInclusive()
    {
        var lines = new[]
        {
            Line("Corner Bistro"),
            Line("Service charge included"),
            Line("Total: EUR 100.00"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out _);
        var totals = root.GetProperty("sourceTotals");
        Assert.False(totals.TryGetProperty("priceBasis", out _));
    }

    [Fact]
    [Trait("Rule", "T-03")]
    [Trait("Rule", "T-08")]
    public void Parse_Totals_P4_TotalInclTaxWithAdditiveSubtotalAndTaxIsNotInclusiveOrUnlabeled()
    {
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Subtotal: EUR 100.00"),
            Line("Tax: EUR 10.00"),
            Line("Total incl. tax: EUR 110.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("100.00", totals.GetProperty("subtotal").GetString());
        Assert.Equal("10.00", totals.GetProperty("taxTotal").GetString());
        Assert.Equal("110.00", totals.GetProperty("grandTotal").GetString());
        Assert.False(totals.TryGetProperty("priceBasis", out _));
        Assert.DoesNotContain("TOTAL_UNLABELED", warnings);
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
    }

    [Fact]
    [Trait("Rule", "T-08")]
    public void Parse_Totals_TrueTaxInclusiveReceiptSetsPriceBasisInclusiveWithoutInconsistentWarning()
    {
        var lines = new[]
        {
            Line("Corner Cafe"),
            Line("Subtotal: EUR 49.80"),
            Line("VAT 19% (tax included): EUR 7.95"),
            Line("TOTAL: EUR 49.80"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("49.80", totals.GetProperty("subtotal").GetString());
        Assert.Equal("7.95", totals.GetProperty("taxTotal").GetString());
        Assert.Equal("49.80", totals.GetProperty("grandTotal").GetString());
        Assert.Equal("inclusive", totals.GetProperty("priceBasis").GetString());
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
        Assert.DoesNotContain("TOTAL_UNLABELED", warnings);
    }

    [Fact]
    [Trait("Rule", "T-07")]
    public void Parse_Totals_P9_UnlabeledLargestAmountFallbackEmitsTotalUnlabeledWarning()
    {
        var lines = new[]
        {
            Line("Corner Cafe"),
            Line("Balance on card 250.00"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out var warnings);
        Assert.Equal("250.00", root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
        Assert.Contains("TOTAL_UNLABELED", warnings);
    }

    // --- K7: tendered / paid / brought-forward lines excluded from grandTotal ---

    [Fact]
    [Trait("Rule", "T-03")]
    public void Parse_Totals_ExcludesTotalPaidAndTenderedFromGrandTotal()
    {
        var lines = new[]
        {
            Line("Corner Cafe"),
            Line("TOTAL 42.50"),
            Line("TOTAL PAID 100.00"),
            Line("AMOUNT TENDERED 100.00"),
            Line("CHANGE 57.50"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out var warnings);
        Assert.Equal("42.50", root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
        Assert.Equal("paid", root.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
        Assert.DoesNotContain("TOTAL_UNLABELED", warnings);
    }

    // --- K8: currency word boundaries and adjacency to amounts ---

    [Fact]
    [Trait("Rule", "C-01")]
    public void Parse_Currency_P7_OrdinaryWordsEndingInRmDoNotTriggerMyr()
    {
        var lines = new[]
        {
            Line("NORTHWIND PLATFORM FIRM"),
            Line("ORDER FORM STORM"),
            Line("Total: 50.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.False(root.TryGetProperty("currency", out _));

        var myr = ParseAndValidate("purchase-invoice", [Line("Store"), Line("Total: RM 45.00")], out _);
        Assert.Equal("MYR", myr.GetProperty("currency").GetString());

        var idr = ParseAndValidate("purchase-invoice", [Line("Store"), Line("Total: Rp 10,000.00")], out _);
        Assert.Equal("IDR", idr.GetProperty("currency").GetString());
    }

    // --- K9 & K10: bank label variants and bounded value extraction ---

    [Theory]
    [InlineData("Account No.: 12345678", "12345678")]
    [InlineData("Account No: 12345678", "12345678")]
    [InlineData("Account #: 12345678", "12345678")]
    [InlineData("Account Number: 1234 5678 Branch Sukhumvit", "1234 5678")]
    [Trait("Rule", "B-05")]
    public void Parse_Bank_K9_K10_AccountNumberVariantsAndBoundedCapture(string line, string expected)
    {
        var root = ParseAndValidate("bank", [Line(line)], out _);
        Assert.Equal(expected, root.GetProperty("accountNumber").GetString());
    }

    [Theory]
    [InlineData("Routing No.: 021000021")]
    [InlineData("Routing Number: 021000021")]
    [InlineData("Routing Transit Number: 021000021")]
    [InlineData("ABA: 021000021")]
    [Trait("Rule", "B-04")]
    public void Parse_Bank_K9_RoutingNumberVariants(string line)
    {
        var root = ParseAndValidate("bank", [Line(line)], out _);
        Assert.Equal("021000021", root.GetProperty("routingNumber").GetString());
    }

    [Theory]
    [InlineData("SWIFT Code: DEUTDEFF", "DEUTDEFF")]
    [InlineData("BIC Code: DEUTDEFFXXX", "DEUTDEFFXXX")]
    [InlineData("SWIFT / BIC: COBADEFFXXX", "COBADEFFXXX")]
    [Trait("Rule", "B-03")]
    public void Parse_Bank_K9_SwiftAndBicCodeVariants(string line, string expected)
    {
        var root = ParseAndValidate("bank", [Line(line)], out _);
        Assert.Equal(expected, root.GetProperty("swift").GetString());
    }

    [Fact]
    [Trait("Rule", "B-02")]
    [Trait("Rule", "B-03")]
    public void Parse_Bank_B5_IbanAndSwiftOnSameLineBothExtractCleanly()
    {
        var root = ParseAndValidate("bank", [Line("IBAN: GB82 WEST 1234 5698 7654 32 SWIFT: NWBKGB2L")], out _);
        Assert.Equal("GB82WEST12345698765432", root.GetProperty("iban").GetString());
        Assert.Equal("NWBKGB2L", root.GetProperty("swift").GetString());
    }

    // --- K11: overall parse deadline emits PARSE_TRUNCATED ---

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _ticks;
        private readonly long _stepTicks;

        public AdvancingTimeProvider(TimeSpan step)
        {
            _stepTicks = step.Ticks;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            _ticks += _stepTicks;
            return _ticks;
        }
    }

    [Theory]
    [InlineData("purchase-invoice")]
    [InlineData("sales-invoice")]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    [Trait("Rule", "G-03")]
    public void Parse_K11_DeadlineExceededEmitsParseTruncatedAndReturnsSchemaValidJson(string kind)
    {
        var fakeTime = new AdvancingTimeProvider(TimeSpan.FromMilliseconds(300));
        var lines = new[]
        {
            Line("Acme Supplies Ltd"),
            Line("Date: 2026-11-20"),
            Line("Reference: INV-42"),
            Line("IBAN: DE89 3704 0044 0532 0130 00"),
            Line("Total: EUR 100.00"),
        };

        var result = DocumentTextFieldParser.Parse(kind, lines, fakeTime, TimeSpan.FromMilliseconds(500));
        Assert.Contains("PARSE_TRUNCATED", result.Warnings);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.True(
            DocumentCaptureFieldSchemas.TryValidate(kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion, doc.RootElement, out _, out var err),
            $"Schema validation failed after deadline truncation for '{kind}': {err}");
    }

    // --- K12: sales customer label anchoring and phone/ID rejection ---

    [Fact]
    [Trait("Rule", "V-06")]
    public void Parse_SalesInvoice_S1_SkipsCustomerServiceAndCustomerIdLabels()
    {
        var lines = new[]
        {
            Line("Our Seller Company Inc."),
            Line("Customer Service: 1-800-555-0100"),
            Line("Customer ID: 987654"),
            Line("Client #: 4421"),
            Line("Total: USD 500.00"),
        };

        var root = ParseAndValidate("sales-invoice", lines, out _);
        Assert.False(root.TryGetProperty("customerName", out _));

        var valid = ParseAndValidate("sales-invoice", [
            Line("Our Seller Company Inc."),
            Line("Customer Service: 1-800-555-0100"),
            Line("Customer Name: Blue Harbor Retail LLC"),
            Line("Total: USD 500.00"),
        ], out _);
        Assert.Equal("Blue Harbor Retail LLC", valid.GetProperty("customerName").GetString());
    }

    // --- K13: assistant attachment skips times and URLs ---

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_A1_SkipsTimesAndUrls()
    {
        var lines = new[]
        {
            Line("12:30"),
            Line("https://example.com/inv"),
            Line("http://example.com/receipt"),
            Line("mailto:billing@example.com"),
            Line("Reference: INV-42"),
        };

        var root = ParseAndValidate("assistant-attachment", lines, out _);
        var fields = root.GetProperty("fields").EnumerateArray().ToList();
        Assert.Single(fields);
        Assert.Equal("Reference", fields[0].GetProperty("label").GetString());
        Assert.Equal("INV-42", fields[0].GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_SpaceSeparatedAndNextLineLabels()
    {
        var lines = new[]
        {
            Line("Invoice Number INV-3337"),
            Line("From:"),
            Line("DEMO - Sliced Invoices"),
            Line("Order Number 12345"),
            Line("Invoice Date January 25, 2016"),
            Line("Due Date January 31, 2016"),
            Line("Total Due $93.50"),
            Line("To:"),
            Line("Test Business"),
            Line("Sub Total $85.00"),
            Line("Tax $8.50"),
            Line("Total $93.50"),
        };

        var root = ParseAndValidate("assistant-attachment", lines, out _);
        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("INV-3337", map["Invoice Number"]);
        Assert.Equal("DEMO - Sliced Invoices", map["From"]);
        Assert.Equal("12345", map["Order Number"]);
        Assert.Equal("January 25, 2016", map["Invoice Date"]);
        Assert.Equal("January 31, 2016", map["Due Date"]);
        Assert.Equal("$93.50", map["Total Due"]);
        Assert.Equal("Test Business", map["To"]);
        Assert.Equal("$85.00", map["Sub Total"]);
        Assert.Equal("$8.50", map["Tax"]);
        Assert.Equal("$93.50", map["Total"]);
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_EmbeddedLabelsWhenColumnsMerge()
    {
        var root = ParseAndValidate("assistant-attachment",
        [
            Line("DEMO - Sliced Invoices Order Number 12345"),
            Line("Suite 5A-1204 Invoice Date January 25, 2016"),
        ], out _);
        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("12345", map["Order Number"]);
        Assert.Equal("January 25, 2016", map["Invoice Date"]);
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_FinancialFallbackWhenNoEnglishLabeledPairs()
    {
        // Thai invoice cues are known to the financial parser but not to the English A-01 space-label list.
        var root = ParseAndValidate("assistant-attachment",
        [
            Line("ตัวอย่าง ซัพพลาย จำกัด"),
            Line("เลขที่ INV-20457"),
            Line("วันที่ 25/01/2016"),
            Line("ยอดรวมทั้งสิ้น 93.50"),
        ], out _);

        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("INV-20457", map["Invoice number"]);
        Assert.Equal("2016-01-25", map["Date"]);
        Assert.Equal("93.50", map["Total"]);
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_RejectsTaxInvoiceHeaderAndTotalQty()
    {
        var root = ParseAndValidate("assistant-attachment",
        [
            Line("Tax Invoice"),
            Line("Total Qty 5"),
            Line("Total 93.50"),
            Line("Due Date 25 January 2016"),
        ], out _);

        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.False(map.TryGetValue("Tax", out var tax) && tax!.Equals("Invoice", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("93.50", map["Total"]);
        Assert.Contains("2016", map["Due Date"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_FinancialHintsFillGapsAfterPartialLabels()
    {
        // A lone From: pair must not block financial date/total recovery.
        var root = ParseAndValidate("assistant-attachment",
        [
            Line("From:"),
            Line("Acme Supplies Ltd"),
            Line("Invoice Number INV-9"),
            Line("Date 25 January 2016"),
            Line("Total 40.00"),
        ], out _);

        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Acme Supplies Ltd", map["From"]);
        Assert.Equal("INV-9", map["Invoice Number"]);
        Assert.True(map.ContainsKey("Date") || map.ContainsKey("Total") || map.ContainsKey("Invoice number"));
    }

    [Fact]
    [Trait("Rule", "A-01")]
    public void Parse_AssistantAttachment_ProdStyleWordPressInvoiceExtractsFields()
    {
        // Invented layout matching the production Sliced Invoices / WordPress sample shape (merged columns, no colons).
        var root = ParseAndValidate("assistant-attachment",
        [
            Line("Invoice"),
            Line("Invoice Number INV-3337"),
            Line("From:"),
            Line("DEMO - Sliced Invoices Order Number 12345"),
            Line("Suite 5A-1204 Invoice Date January 25, 2016"),
            Line("123 Somewhere Street"),
            Line("Due Date January 31, 2016"),
            Line("Your City AZ 12345"),
            Line("Total Due $93.50"),
            Line("To:"),
            Line("Test Business"),
            Line("Sub Total $85.00d"),
            Line("Tax $8.50i"),
            Line("Total $93.50a"),
        ], out _);

        var map = root.GetProperty("fields").EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("INV-3337", map["Invoice Number"]);
        Assert.Equal("12345", map["Order Number"]);
        Assert.Contains("DEMO - Sliced Invoices", map["From"], StringComparison.Ordinal);
        Assert.Equal("Test Business", map["To"]);
        Assert.Equal("$93.50", map["Total Due"]);
        Assert.True(map.ContainsKey("Date") || map.ContainsKey("Invoice Date") || map.ContainsKey("Due Date"));
    }

    // --- L1: tendered amounts and amountPaid > grandTotal + 0.01 omit amountPaid ---

    [Fact]
    [Trait("Rule", "P-02")]
    public void Parse_PaymentHints_L1_TenderedAmountExceedingGrandTotalOmitsAmountPaidAndKeepsPaid()
    {
        var lines = new[]
        {
            Line("Corner Cafe"),
            Line("TOTAL 42.50"),
            Line("TOTAL PAID 100.00"),
            Line("TENDERED 100.00"),
            Line("CHANGE 57.50"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out _);
        Assert.Equal("42.50", root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
        var hints = root.GetProperty("paymentHints");
        Assert.Equal("paid", hints.GetProperty("paymentStatus").GetString());
        Assert.False(hints.TryGetProperty("amountPaid", out _));

        var tenderedOnly = ParseAndValidate("expense-receipt", [
            Line("Corner Cafe"),
            Line("TOTAL 42.50"),
            Line("TENDERED 42.50"),
        ], out _);
        var tenderedHints = tenderedOnly.GetProperty("paymentHints");
        Assert.Equal("paid", tenderedHints.GetProperty("paymentStatus").GetString());
        Assert.False(tenderedHints.TryGetProperty("amountPaid", out _));
    }

    // --- M1: explicit amountPaid vs grandTotal determines paid vs partial ---

    [Fact]
    [Trait("Rule", "P-03")]
    public void Parse_PaymentHints_M1_ExplicitAmountPaidDeterminesPaidOrPartialAgainstGrandTotal()
    {
        var fullyPaid = ParseAndValidate("purchase-invoice", [
            Line("Acme Supplies Ltd"),
            Line("Total: EUR 500.00"),
            Line("Amount Paid: EUR 500.00"),
        ], out _);
        var paidHints = fullyPaid.GetProperty("paymentHints");
        Assert.Equal("paid", paidHints.GetProperty("paymentStatus").GetString());
        Assert.Equal("500.00", paidHints.GetProperty("amountPaid").GetString());

        var partiallyPaid = ParseAndValidate("purchase-invoice", [
            Line("Acme Supplies Ltd"),
            Line("Total: EUR 500.00"),
            Line("Amount Paid: EUR 200.00"),
        ], out _);
        var partialHints = partiallyPaid.GetProperty("paymentHints");
        Assert.Equal("partial", partialHints.GetProperty("paymentStatus").GetString());
        Assert.Equal("200.00", partialHints.GetProperty("amountPaid").GetString());
    }

    // --- E1: emit lines only when sum reconciles to subtotal (or grandTotal when inclusive/no tax) ---

    [Fact]
    [Trait("Rule", "L-04")]
    public void Parse_Lines_E1_OmitsSplicedOrPartialTablesAndEmitsCompleteReconciledTable()
    {
        // Spliced-row case (like skewed OCR producing one wrong row summing to 50.00 vs subtotal 100.00)
        var spliced = ParseAndValidate("expense-receipt", [
            Line("Blue Bean Coffee Supply"),
            Line("Paper cups 2 $25.00 $50.00"),
            Line("Subtotal: USD 100.00"),
            Line("Total: USD 100.00"),
        ], out _);
        Assert.Empty(spliced.GetProperty("lines").EnumerateArray().ToList());

        // Complete table reconciling to subtotal with tax on top
        var complete = ParseAndValidate("purchase-invoice", [
            Line("Blue Bean Coffee Supply"),
            Line("Espresso beans 2 $25.00 $50.00"),
            Line("Paper cups 1 $50.00 $50.00"),
            Line("Subtotal: USD 100.00"),
            Line("Tax: USD 10.00"),
            Line("Total: USD 110.00"),
        ], out _);
        Assert.Equal(2, complete.GetProperty("lines").EnumerateArray().Count());

        // Table with a missing row (rows sum to 100.00 vs subtotal 125.00)
        var missingRow = ParseAndValidate("purchase-invoice", [
            Line("Blue Bean Coffee Supply"),
            Line("Espresso beans 2 $25.00 $50.00"),
            Line("Paper cups 1 $50.00 $50.00"),
            Line("Subtotal: USD 125.00"),
            Line("Total: USD 125.00"),
        ], out _);
        Assert.Empty(missingRow.GetProperty("lines").EnumerateArray().ToList());

        // No subtotal, non-inclusive tax present -> cannot reconcile against grandTotal -> empty
        var noSubtotalWithTax = ParseAndValidate("purchase-invoice", [
            Line("Blue Bean Coffee Supply"),
            Line("Espresso beans 2 $25.00 $50.00"),
            Line("Paper cups 1 $50.00 $50.00"),
            Line("Tax: USD 10.00"),
            Line("Total: USD 110.00"),
        ], out _);
        Assert.Empty(noSubtotalWithTax.GetProperty("lines").EnumerateArray().ToList());
    }

    // --- E1b: spliced header row ("Date: 2026-11-20 2 $25.00 $50.00") rejected before sum check ---

    [Fact]
    [Trait("Rule", "L-03")]
    public void Parse_Lines_E1b_RejectsSplicedHeaderRowWithLabelDateOrColonBeforeSumCheck()
    {
        var lines = new[]
        {
            Line("Blue Bean Coffee Supply"),
            Line("TAX INVOICE"),
            Line("Invoice No.: S01-1283 $50.00"),
            Line("Date: 2026-11-20 2 $25.00 $50.00"),
            Line("Espresso beans 1 $50.00"),
            Line("Paper cups 2 $25.00 $50.00"),
            Line("Subtotal: $100.00"),
            Line("Tax 10%: $10.00"),
            Line("TOTAL: $110.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Empty(root.GetProperty("lines").EnumerateArray().ToList());
        Assert.Equal("2026-11-20", root.GetProperty("date").GetString());
        Assert.Equal("110.00", root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
    }

    // --- E5: item line containing "tax" must not become taxTotal or priceBasis inclusive ---

    [Fact]
    [Trait("Rule", "T-02")]
    [Trait("Rule", "T-06")]
    public void Parse_Totals_E5_ItemRowContainingTaxDoesNotSetTaxTotalOrInclusive()
    {
        var lines = new[]
        {
            Line("Acme Ltd"),
            Line("Phone case 2 $5.00 $10.00"),
            Line("Tax software licence 1 $90.00 $90.00"),
            Line("Subtotal: $100.00"),
            Line("Total: $100.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("100.00", totals.GetProperty("subtotal").GetString());
        Assert.Equal("100.00", totals.GetProperty("grandTotal").GetString());
        Assert.False(totals.TryGetProperty("taxTotal", out _));
        Assert.False(totals.TryGetProperty("priceBasis", out _));
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);
    }

    [Theory]
    [InlineData("Tax 10%: $10.00", "10.00")]
    [InlineData("VAT: 6.79", "6.79")]
    [InlineData("GST (9%) 1.20", "1.20")]
    [Trait("Rule", "T-06")]
    public void Parse_Totals_E5_ValidTaxLabelLinesStillExtractTaxTotal(string taxLine, string expectedTax)
    {
        var root = ParseAndValidate("purchase-invoice", [
            Line("Acme Ltd"),
            Line(taxLine),
            Line("Total: EUR 100.00"),
        ], out _);
        Assert.Equal(expectedTax, root.GetProperty("sourceTotals").GetProperty("taxTotal").GetString());
    }

    // --- E2: vendor address detection (Unit/Tower/comma+digit) and earliest clean letters-only line ---

    [Fact]
    [Trait("Rule", "V-02")]
    [Trait("Rule", "V-05")]
    public void Parse_Vendor_E2_SkipsUnitAndCommaNumberAddressLinesAndPrefersEarliestCleanIssuer()
    {
        var lines = new[]
        {
            Line("Jane Example", y0: 0, y1: 14),
            Line("INVOICE", y0: 16, y1: 30),
            Line("Unit 1200 North Tower, Example Residences Sample Avenue Townsville", y0: 32, y1: 56),
            Line("#7", y0: 58, y1: 72),
            Line("45 Sample St. Ext. Some Area, Example City", y0: 74, y1: 88),
            Line("Total: USD 500.00", y0: 100, y1: 114),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out _);
        Assert.Equal("Jane Example", root.GetProperty("vendorName").GetString());

        // Earliest clean letters-only line beats later equal-height non-legal-suffix line
        var earliestClean = ParseAndValidate("purchase-invoice", [
            Line("Jane Example", y0: 0, y1: 14),
            Line("Sample Services Division", y0: 20, y1: 34),
            Line("Total: USD 500.00", y0: 60, y1: 74),
        ], out _);
        Assert.Equal("Jane Example", earliestClean.GetProperty("vendorName").GetString());

        // Later line with a legal suffix still beats an earlier plain line
        var laterLegal = ParseAndValidate("purchase-invoice", [
            Line("Billing Department", y0: 0, y1: 14),
            Line("Northwind Consulting LLC", y0: 20, y1: 34),
            Line("Total: USD 500.00", y0: 60, y1: 74),
        ], out _);
        Assert.Equal("Northwind Consulting LLC", laterLegal.GetProperty("vendorName").GetString());
    }

    // --- E6: country line after address block and lines after BILLED TO never become vendor ---

    [Theory]
    [InlineData("purchase-invoice")]
    [InlineData("expense-receipt")]
    [Trait("Rule", "V-01")]
    [Trait("Rule", "V-02")]
    public void Parse_Vendor_E6_IgnoresCountryLineAfterAddressAndLinesAfterBilledTo(string kind)
    {
        var lines = new[]
        {
            Line("Jane Example", y0: 0, y1: 14),
            Line("INVOICE", y0: 16, y1: 30),
            Line("Unit 1200 North Tower, Example Residences Sample Avenue Townsville", y0: 32, y1: 46),
            Line("#7", y0: 48, y1: 62),
            Line("45 Sample St. Ext. Some Area, Example City", y0: 64, y1: 78),
            Line("Townsville, Metro Region 1600", y0: 80, y1: 94),
            Line("Examplestan", y0: 96, y1: 110),
            Line("jane.example@example.test", y0: 112, y1: 126),
            Line("BILLED TO", y0: 130, y1: 144),
            Line("Blue Harbor Retail LLC", y0: 146, y1: 170),
            Line("Total: USD 500.00", y0: 180, y1: 194),
        };

        var root = ParseAndValidate(kind, lines, out _);
        Assert.Equal("Jane Example", root.GetProperty("vendorName").GetString());
    }

    [Fact]
    [Trait("Rule", "V-06")]
    public void Parse_SalesInvoice_E6_ExtractsCustomerFromBilledToBlock()
    {
        var lines = new[]
        {
            Line("Jane Example", y0: 0, y1: 14),
            Line("INVOICE", y0: 16, y1: 30),
            Line("Unit 1200 North Tower, Example Residences Sample Avenue Townsville", y0: 32, y1: 46),
            Line("#7", y0: 48, y1: 62),
            Line("45 Sample St. Ext. Some Area, Example City", y0: 64, y1: 78),
            Line("Townsville, Metro Region 1600", y0: 80, y1: 94),
            Line("Examplestan", y0: 96, y1: 110),
            Line("jane.example@example.test", y0: 112, y1: 126),
            Line("BILLED TO", y0: 130, y1: 144),
            Line("Blue Harbor Retail LLC", y0: 146, y1: 170),
            Line("Total: USD 500.00", y0: 180, y1: 194),
        };

        var root = ParseAndValidate("sales-invoice", lines, out _);
        Assert.Equal("Blue Harbor Retail LLC", root.GetProperty("customerName").GetString());
    }

    // --- E7 & E8: merged title on vendor line ("Jane Example INVOICE") and bare "#7" invoice number ---

    [Fact]
    [Trait("Rule", "V-03")]
    [Trait("Rule", "V-07")]
    [Trait("Rule", "N-02")]
    [Trait("Rule", "C-01")]
    [Trait("Rule", "L-02")]
    public void Parse_E7_E8_TwoColumnInvoiceExtractsVendorStrippingTitleAndBareHashInvoiceNo()
    {
        var lines = new[]
        {
            Line("Jane Example INVOICE"),
            Line("Unit 1200 North Tower, Example Residences Sample Avenue Townsville #7"),
            Line("45 Sample St. Ext. Some Area, Example City"),
            Line("Townsville, Metro Region 1600"),
            Line("Examplestan"),
            Line("jane.example@example.test"),
            Line("BILLED TO Issue date Nov 15, 2026"),
            Line("Globex Example Corp Due date Nov 28, 2026"),
            Line("99 Demo Road, Sampleville"),
            Line("# Description Qty Rate Amount"),
            Line("1 Consulting services 1 $4,200.00 $4,200.00"),
            Line("Subtotal $4,200.00"),
            Line("Total $4,200.00"),
            Line("Amount due $4,200.00"),
        };

        var root = ParseAndValidate("purchase-invoice", lines, out var warnings);
        Assert.Equal("Jane Example", root.GetProperty("vendorName").GetString());
        Assert.Equal("7", root.GetProperty("vendorInvoiceNo").GetString());
        Assert.Equal("2026-11-15", root.GetProperty("date").GetString());
        Assert.Equal("2026-11-28", root.GetProperty("dueDate").GetString());
        Assert.Equal("4200.00", root.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
        Assert.Contains("AMBIGUOUS_CURRENCY", warnings);

        var parsedLines = root.GetProperty("lines").EnumerateArray().ToList();
        Assert.Single(parsedLines);
        Assert.Equal("Consulting services", parsedLines[0].GetProperty("description").GetString());

        // E9: same two-column layout parsed as sales-invoice extracts "Globex Example Corp" as customerName
        var salesRoot = ParseAndValidate("sales-invoice", lines, out _);
        Assert.Equal("Globex Example Corp", salesRoot.GetProperty("customerName").GetString());
        Assert.Equal("7", salesRoot.GetProperty("sourceInvoiceNo").GetString());
    }

    [Fact]
    [Trait("Rule", "N-01")]
    [Trait("Rule", "N-02")]
    public void Parse_E8_AddressUnitHashIgnoredAndLabelledInvoiceNumberWins()
    {
        // Address "Unit #4, 10 Example Road" must not produce an invoice number
        var unitHash = ParseAndValidate("purchase-invoice", [
            Line("Jane Example"),
            Line("Unit #4, 10 Example Road"),
            Line("Total: EUR 50.00"),
        ], out _);
        Assert.False(unitHash.TryGetProperty("vendorInvoiceNo", out _));

        // Inline forms "INVOICE #7" and "Invoice # INV-0042"
        var inlineSingle = ParseAndValidate("purchase-invoice", [
            Line("Jane Example"),
            Line("INVOICE #7"),
            Line("Total: EUR 50.00"),
        ], out _);
        Assert.Equal("7", inlineSingle.GetProperty("vendorInvoiceNo").GetString());

        var inlineSpaced = ParseAndValidate("purchase-invoice", [
            Line("Jane Example"),
            Line("Invoice # INV-0042"),
            Line("Total: EUR 50.00"),
        ], out _);
        Assert.Equal("INV-0042", inlineSpaced.GetProperty("vendorInvoiceNo").GetString());

        // Labelled invoice number wins over an earlier bare "#7"
        var labelledWins = ParseAndValidate("purchase-invoice", [
            Line("Jane Example #7"),
            Line("Invoice No.: INV-999"),
            Line("Total: EUR 50.00"),
        ], out _);
        Assert.Equal("INV-999", labelledWins.GetProperty("vendorInvoiceNo").GetString());
    }

    // --- E10: skewed item rows with inflated OCR box heights must never replace the top-block issuer ---

    [Fact]
    [Trait("Rule", "V-01")]
    [Trait("Rule", "V-02")]
    [Trait("Rule", "V-05")]
    public void Parse_Vendor_E10_SkewedTallItemRowsDoNotReplaceTopBlockIssuer()
    {
        var s04 = new[]
        {
            Line("Harbor Hardware", y0: 0, y1: 25),
            Line("123 Example Road, Unit 4", y0: 30, y1: 54),
            Line("RECEIPT", y0: 60, y1: 83),
            Line("Receipt #: S04-2738", y0: 90, y1: 112),
            Line("Date: 03.10.2026", y0: 120, y1: 141),
            Line("1 €34.90 €34.90", y0: 150, y1: 178),
            Line("Drill bits set €7.45 €14.90", y0: 185, y1: 220),
            Line("Screws 200pc 2", y0: 225, y1: 256),
        };
        Assert.Equal("Harbor Hardware", ParseAndValidate("expense-receipt", s04, out _).GetProperty("vendorName").GetString());

        var s06 = new[]
        {
            Line("Green Leaf Grocers", y0: 0, y1: 26),
            Line("123 Example Road, Unit 4", y0: 30, y1: 56),
            Line("RECEIPT", y0: 60, y1: 85),
            Line("Receipt #: S06-5197", y0: 90, y1: 113),
            Line("Date: 2026/09/30", y0: 120, y1: 143),
            Line("Organic apples 3 $4.99 $14.97", y0: 150, y1: 187),
            Line("Milk 2L 2 $3.49 $6.98", y0: 190, y1: 228),
            Line("Bread 1 $5.25 $5.25", y0: 230, y1: 268),
        };
        Assert.Equal("Green Leaf Grocers", ParseAndValidate("expense-receipt", s06, out _).GetProperty("vendorName").GetString());

        var s08 = new[]
        {
            Line("Sunrise Bakery", y0: 0, y1: 28),
            Line("123 Example Road, Unit 4", y0: 30, y1: 54),
            Line("RECEIPT", y0: 60, y1: 85),
            Line("Receipt #: S08-7103", y0: 90, y1: 112),
            Line("Date: 12/11/2026", y0: 120, y1: 142),
            Line("6 $2.75 $16.50", y0: 150, y1: 178),
            Line("Croissant $3.50 $14.00", y0: 185, y1: 221),
            Line("Coffee 4", y0: 225, y1: 253),
        };
        Assert.Equal("Sunrise Bakery", ParseAndValidate("expense-receipt", s08, out _).GetProperty("vendorName").GetString());
    }

    // --- E3: strip leading row-number column from line item descriptions ---

    [Fact]
    [Trait("Rule", "L-02")]
    public void Parse_Lines_E3_StripsLeadingRowIndexPrefixAndPreservesUnindexedDescriptions()
    {
        var indexed = ParseAndValidate("purchase-invoice", [
            Line("Jane Example"),
            Line("1 Consulting services 1 $500.00 $500.00"),
            Line("2. Design review 2 $100.00 $200.00"),
            Line("3) Support retainer 1 $300.00 $300.00"),
            Line("Total: USD 1,000.00"),
        ], out _);

        var indexedLines = indexed.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(3, indexedLines.Count);
        Assert.Equal("Consulting services", indexedLines[0].GetProperty("description").GetString());
        Assert.Equal("Design review", indexedLines[1].GetProperty("description").GetString());
        Assert.Equal("Support retainer", indexedLines[2].GetProperty("description").GetString());

        var unindexed = ParseAndValidate("purchase-invoice", [
            Line("Jane Example"),
            Line("Consulting services 1 $500.00 $500.00"),
            Line("Total: USD 500.00"),
        ], out _);

        var unindexedLines = unindexed.GetProperty("lines").EnumerateArray().ToList();
        Assert.Single(unindexedLines);
        Assert.Equal("Consulting services", unindexedLines[0].GetProperty("description").GetString());
    }

    // --- E4: DocumentTextFieldParser.WarningCodes exposes every parser warning code ---

    [Fact]
    [Trait("Rule", "W-01")]
    public void WarningCodes_E4_ListsEveryParserWarningCode()
    {
        Assert.Contains("AMBIGUOUS_CURRENCY", DocumentTextFieldParser.WarningCodes);
        Assert.Contains("TOTAL_UNLABELED", DocumentTextFieldParser.WarningCodes);
        Assert.Contains("TOTALS_INCONSISTENT", DocumentTextFieldParser.WarningCodes);
        Assert.Contains("PARSE_TRUNCATED", DocumentTextFieldParser.WarningCodes);
        Assert.Equal(4, DocumentTextFieldParser.WarningCodes.Distinct(StringComparer.Ordinal).Count());
    }

    // --- L2: real tax-inclusive receipts recognised without TOTALS_INCONSISTENT ---

    [Fact]
    [Trait("Rule", "T-08")]
    public void Parse_Totals_L2_VatRateIncludedLineRecognisedAsInclusiveWithoutWarning()
    {
        var lines = new[]
        {
            Line("Corner Bistro"),
            Line("Subtotal EUR 42.50"),
            Line("VAT 19% included 6.79"),
            Line("TOTAL EUR 42.50"),
        };

        var root = ParseAndValidate("expense-receipt", lines, out var warnings);
        var totals = root.GetProperty("sourceTotals");
        Assert.Equal("42.50", totals.GetProperty("subtotal").GetString());
        Assert.Equal("6.79", totals.GetProperty("taxTotal").GetString());
        Assert.Equal("42.50", totals.GetProperty("grandTotal").GetString());
        Assert.Equal("inclusive", totals.GetProperty("priceBasis").GetString());
        Assert.DoesNotContain("TOTALS_INCONSISTENT", warnings);

        var implicitInclusive = ParseAndValidate("expense-receipt", [
            Line("Corner Bistro"),
            Line("Subtotal EUR 42.50"),
            Line("VAT 19%: EUR 6.79"),
            Line("TOTAL EUR 42.50"),
        ], out var implicitWarnings);
        Assert.Equal("inclusive", implicitInclusive.GetProperty("sourceTotals").GetProperty("priceBasis").GetString());
        Assert.DoesNotContain("TOTALS_INCONSISTENT", implicitWarnings);
    }

    // --- L3: vendor and customer length (<= 120) and letter-ratio (>= 40%) bounds ---

    [Fact]
    [Trait("Rule", "V-04")]
    public void Parse_Party_L3_RejectsOverlongAndLowLetterRatioCandidates()
    {
        var repeated = string.Concat(Enumerable.Repeat("a 1 1.00 ", 900))[..8_000];
        var hostileRoot = ParseAndValidate("purchase-invoice", [Line(repeated)], out _);
        Assert.False(hostileRoot.TryGetProperty("vendorName", out _));

        var overlong = new string('A', 121);
        var overlongVendor = ParseAndValidate("purchase-invoice", [Line(overlong), Line("Total: EUR 10.00")], out _);
        Assert.False(overlongVendor.TryGetProperty("vendorName", out _));

        var overlongCustomer = ParseAndValidate("sales-invoice", [Line("Seller Co"), Line("Bill To:"), Line(overlong), Line("Total: EUR 10.00")], out _);
        Assert.False(overlongCustomer.TryGetProperty("customerName", out _));

        var lowLetterRatio = ParseAndValidate("purchase-invoice", [Line("AB 1234567890"), Line("Total: EUR 10.00")], out _);
        Assert.False(lowLetterRatio.TryGetProperty("vendorName", out _));
    }

    [Fact]
    [Trait("Rule", "N-01")]
    [Trait("Rule", "T-05")]
    public void Parse_SyntheticTruthCleanText_Scores18Of18OnVendorDateAndTotalWithNoTotalsInconsistent()
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
            var id = entry.GetProperty("id").GetString()!;
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

            var parsed = ParseAndValidate(kind, lines, out var warnings);
            Assert.True(!warnings.Contains("TOTALS_INCONSISTENT"), $"Unexpected TOTALS_INCONSISTENT on synthetic fixture {id}");
            if (parsed.TryGetProperty("vendorName", out var v) && v.GetString() == vendor) vendorHits++;
            if (parsed.TryGetProperty("date", out var d) && d.GetString() == isoDate) dateHits++;
            if (parsed.TryGetProperty("sourceTotals", out var st)
                && st.TryGetProperty("grandTotal", out var gt)
                && gt.GetString() == total) totalHits++;
        }

        output.WriteLine($"Clean text vendor: {vendorHits}/{entries.Count} match.");
        output.WriteLine($"Clean text date: {dateHits}/{entries.Count} match.");
        output.WriteLine($"Clean text total: {totalHits}/{entries.Count} match.");

        Assert.Equal(18, vendorHits);
        Assert.Equal(18, dateHits);
        Assert.Equal(18, totalHits);
    }

    [Fact]
    [Trait("Rule", "G-01")]
    public void Parse_UnsupportedKind_ReturnsEmptyJsonWithNoWarnings()
    {
        var result = DocumentTextFieldParser.Parse("unsupported-kind", [Line("Acme Ltd"), Line("Total: EUR 10.00")]);
        Assert.Equal("{}", result.FieldsJson);
        Assert.Empty(result.Warnings);
    }
}
