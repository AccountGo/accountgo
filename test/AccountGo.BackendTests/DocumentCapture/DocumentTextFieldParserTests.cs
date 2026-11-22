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
    public void Parse_HostileInputs_ReturnSchemaValidJsonWithin250MsWarmed(string kind)
    {
        // Warm regexes and JIT once before timing (K1).
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
            _ = ParseAndValidate(kind, set, out _);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds <= 250, $"Kind '{kind}' took {sw.ElapsedMilliseconds}ms across hostile inputs (ceiling 250ms).");
    }

    // --- K2: paymentStatus strong cues, negations, and positive amount due precedence ---

    [Fact]
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
    public void Parse_PaymentHints_StrongPaidCuesSetPaid(string cueLine)
    {
        var root = ParseAndValidate("expense-receipt", [Line("Corner Cafe"), Line("Total: EUR 42.50"), Line(cueLine)], out _);
        Assert.Equal("paid", root.GetProperty("paymentHints").GetProperty("paymentStatus").GetString());
    }

    // --- K3: dueDate label strictness and next-line digit guard ---

    [Fact]
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
    public void Parse_Bank_K9_RoutingNumberVariants(string line)
    {
        var root = ParseAndValidate("bank", [Line(line)], out _);
        Assert.Equal("021000021", root.GetProperty("routingNumber").GetString());
    }

    [Theory]
    [InlineData("SWIFT Code: DEUTDEFF", "DEUTDEFF")]
    [InlineData("BIC Code: DEUTDEFFXXX", "DEUTDEFFXXX")]
    [InlineData("SWIFT / BIC: COBADEFFXXX", "COBADEFFXXX")]
    public void Parse_Bank_K9_SwiftAndBicCodeVariants(string line, string expected)
    {
        var root = ParseAndValidate("bank", [Line(line)], out _);
        Assert.Equal(expected, root.GetProperty("swift").GetString());
    }

    [Fact]
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

    // --- L1: tendered amounts and amountPaid > grandTotal + 0.01 omit amountPaid ---

    [Fact]
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

    // --- L2: real tax-inclusive receipts recognised without TOTALS_INCONSISTENT ---

    [Fact]
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

        Assert.Equal(18, vendorHits);
        Assert.Equal(18, dateHits);
        Assert.Equal(18, totalHits);
    }
}
