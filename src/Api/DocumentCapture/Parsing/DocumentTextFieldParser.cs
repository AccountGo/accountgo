// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

public sealed record ParsedFields(string FieldsJson, IReadOnlyList<string> Warnings);

/// <summary>
/// Pure deterministic C# field parser converting ordered <see cref="TextLine"/> records into schema-valid
/// capture JSON and warnings for every supported document capture kind. No AI or ML.
/// Every regex uses <see cref="RegexOptions.NonBacktracking"/> and the full parse is bounded by an overall deadline.
/// </summary>
public static class DocumentTextFieldParser
{
    public const string RulesVersion = "rules-3";

    private const int MaxTotalInputChars = 8_000;
    private static readonly TimeSpan DefaultParseDeadline = TimeSpan.FromMilliseconds(500);
    private const RegexOptions Linear = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private const RegexOptions LinearI = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    public const string WarningAmbiguousCurrency = "AMBIGUOUS_CURRENCY";
    public const string WarningTotalUnlabeled = "TOTAL_UNLABELED";
    public const string WarningTotalsInconsistent = "TOTALS_INCONSISTENT";
    public const string WarningParseTruncated = "PARSE_TRUNCATED";

    /// <summary>
    /// Every warning code this parser can emit. Kept in sync with <c>DocumentCaptureWarningCodes</c> and UI review text.
    /// </summary>
    public static readonly IReadOnlyList<string> WarningCodes =
    [
        WarningAmbiguousCurrency,
        WarningTotalUnlabeled,
        WarningTotalsInconsistent,
        WarningParseTruncated,
    ];

    private static readonly Regex TotalKeywordRegex = new(
        @"(?:grand\s*total|total\s*amount|amount\s*due|balance\s*due|total\s*\(?rm\)?|\btotal\b|ยอดรวมทั้งสิ้น|ยอดสุทธิ|รวมทั้งสิ้น|ทั้งสิ้น|จำนวนเงินรวม)",
        LinearI);

    // K6 + K7: exclude subtotals, counts, tender/paid/change/previous balance/savings, and standalone tax lines.
    // Inclusive-tax total lines (e.g. "Total incl. tax: 110.00") are explicitly permitted by IsGrandTotalLine.
    private static readonly Regex NotTotalKeywordRegex = new(
        @"(?:sub\s*-?\s*total|total\s*qty|total\s*item|total\s*discount|rounding|change|cash|\bpaid\b|\btender(?:ed)?\b|\breceived\b|balance\s*(?:brought|forward)|previous\s*balance|you\s*saved|\bsavings\b|รวมเป็นเงิน)",
        LinearI);

    private static readonly Regex StandaloneTaxWordRegex = new(
        @"(?:\btax\b|\bgst\b|\bvat\b|ภาษี)",
        LinearI);

    private static readonly Regex InclusiveTaxPhraseRegex = new(
        @"(?:(?:tax|vat|gst)\s*(?:incl\.?|included|inclusive)|incl\.?\s*(?:tax|vat|gst)|including\s+(?:tax|vat|gst)|รวมภาษีแล้ว)",
        LinearI);

    // L2: any inclusive cue on a tax line (e.g., "VAT 19% included 6.79")
    private static readonly Regex InclusiveWordRegex = new(
        @"(?:\bincl\.?|\bincluded\b|\binclusive\b|รวมภาษีแล้ว)",
        LinearI);

    private static readonly Regex SubtotalLabelRegex = new(
        @"(?:sub\s*-?\s*total|net\s*total|รวมเป็นเงิน|ยอดก่อนภาษี)",
        LinearI);

    // E5: a tax total comes only from a label line (tax keyword at start followed by rate/:/=/inclusive/amount/EOL,
    // or anywhere followed directly by a rate, ':' or '=').
    private static readonly Regex TaxLabelRegex = new(
        @"(?:^(?:(?:sales|value\s*added|service|output|input|total)\s+)?(?:\btax\b|\bvat\b|\bgst\b|\bhst\b|\bpst\b|ภาษีมูลค่าเพิ่ม|ภาษี)(?:\s*(?:\(?\d+(?:\.\d+)?%\)?|[:=\-]|incl\.?|included|inclusive|รวมภาษีแล้ว)|\s+(?:(?:S\$|A\$|C\$|HK\$|NZ\$|RM|Rp|[A-Z]{3})\s*)?[$€£฿]?\s*\d|\s*$)|(?:\btax\b|\bvat\b|\bgst\b|\bhst\b|\bpst\b|ภาษีมูลค่าเพิ่ม|ภาษี)\s*(?:\(?\d+(?:\.\d+)?%\)?|[:=]))",
        LinearI);

    // K4: exclude tax invoice headers, tax dates, and tax registration/ID numbers.
    private static readonly Regex TaxHeaderExclusionRegex = new(
        @"(?:tax\s*invoice|tax\s*date|ใบกำกับภาษี|\b(?:reg\.?|registration|no\.?|number|id|tin)\b|#)",
        LinearI);

    private static readonly Regex DateLabelRegex = new(
        @"(?:\binvoice\s*date\b|\btax\s*date\b|\bbill\s*date\b|\breceipt\s*date\b|\bissue\s*date\b|\bdate\b|วันที่)",
        LinearI);

    // K3: explicit due-date labels only (never bare "due" from "Amount Due").
    private static readonly Regex DueDateLabelRegex = new(
        @"(?:\bdue\s*date\b|\bpayment\s*due\b|\bdate\s*due\b|\bdue\s*on\b|\bdue\s*by\b|ครบกำหนด)",
        LinearI);

    private static readonly Regex InvoiceNumberRegex = new(
        @"(?:invoice\s*(?:no\.?|number|#)|inv\.?\s*(?:no\.?|#)|receipt\s*(?:no\.?|number|#)|bill\s*(?:no\.?|number|#)|เลขที่)\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9\-_/\.]{0,60})",
        LinearI);

    private static readonly Regex InvoiceNumberLabelOnlyRegex = new(
        @"^(?:invoice\s*(?:no\.?|number|#)|inv\.?\s*(?:no\.?|#)|receipt\s*(?:no\.?|number|#)|bill\s*(?:no\.?|number|#)|เลขที่)\s*[:#]?\s*$",
        LinearI);

    // E8: bare top-block "#7" fallback when no labelled invoice number is present.
    private static readonly Regex BareHashTokenRegex = new(
        @"#\s?([A-Za-z0-9][A-Za-z0-9\-/]{0,30})",
        LinearI);

    private static readonly Regex AddressHashPrefixRegex = new(
        @"\b(?:unit|suite|ste\.?|apt\.?|room|rm\.?|floor|fl\.?|no\.?|lot|blk\.?|block)\s*$",
        LinearI);

    private static readonly Regex TableHeaderRowRegex = new(
        @"(?:^#\s+(?:description|item|service|product|qty|quantity)\b|\b(?:description|item)\b.*\b(?:qty|quantity|rate|price|amount)\b)",
        LinearI);

    // E7: standalone document-title tokens stripped before vendor scoring when columns merge (e.g. "Jane Example INVOICE").
    private static readonly Regex DocumentTitleTokenRegex = new(
        @"(?:\b(?:tax\s+invoice|official\s+receipt|invoice|receipt|bill|statement|quotation)\b|ใบกำกับภาษี|ใบเสร็จรับเงิน)",
        LinearI);

    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", Linear);

    // K12: anchored sales customer labels with separator/EOL; exclude "Customer Service" / "Customer ID" etc.
    private static readonly Regex BillToExclusionRegex = new(
        @"^(?:customer|client)\s+(?:service|support|care|no\.?|id|number|#)\b",
        LinearI);

    private static readonly Regex BillToLabelRegex = new(
        @"^(?:bill(?:ed)?\s*to|sold\s*to|customer\s*name|client\s*name|customer|client)(?:\s*[:#\-]\s*(.*)|\s*)$",
        LinearI);

    // E9: BILLED TO / BILL TO / CUSTOMER followed directly by a merged right-column label phrase (not a customer name).
    private static readonly Regex BillToWithTrailingLabelRegex = new(
        @"^(?:bill(?:ed)?\s*to|sold\s*to|customer\s*name|client\s*name|customer|client)\s+(?:issue\s*date|invoice\s*date|due\s*date|payment\s*due|invoice\s*(?:no\.?|number|#)|terms|date)\b",
        LinearI);

    // E9: strip trailing merged right-column label phrase from customer candidate lines.
    private static readonly Regex TrailingColumnLabelPhraseRegex = new(
        @"\b(?:issue\s*date|invoice\s*date|due\s*date|payment\s*due|invoice\s*(?:no\.?|number|#)|terms|date)\b.*$",
        LinearI);

    // E6: stop vendor scanning once recipient/customer section begins.
    private static readonly Regex VendorBlockStopRegex = new(
        @"^(?:bill(?:ed)?\s*to|ship\s*to|customer)\b",
        LinearI);

    private static readonly Regex VendorPenaltyRegex = new(
        @"\b(?:receipt|tax\s*invoice|invoice|copy|cashier|tel|telephone|fax|phone|mobile|email|e-mail|www|http|date|subtotal|total|qty|description|item)\b|@|ใบกำกับภาษี|ใบเสร็จรับเงิน|เลขที่|วันที่",
        LinearI);

    private static readonly Regex AddressPatternRegex = new(
        @"\b\d{1,5}\s+[A-Za-z0-9.\s]{1,30}\b(?:road|rd\.?|street|st\.?|avenue|ave\.?|blvd\.?|boulevard|lane|ln\.?|drive|dr\.?|way|unit|suite|ste\.?|floor|fl\.?|jalan|jln\.?|soi)\b|ถนน|ซอย",
        LinearI);

    // E2: address lines starting with Unit/Suite/Floor/Tower etc. followed by a number.
    private static readonly Regex AddressUnitPrefixRegex = new(
        @"^(?:unit|suite|ste\.?|floor|fl\.?|level|lvl\.?|room|rm\.?|bldg\.?|building|tower)\s*#?\s*\d+",
        LinearI);

    // E3: strip leading row-number column prefix (e.g., "1 Consulting services", "2. Paper cups").
    private static readonly Regex LeadingRowIndexRegex = new(
        @"^\d{1,3}[.)]?\s+",
        Linear);

    // E1b: reject spliced header/label rows from table line descriptions.
    private static readonly Regex LineItemDescriptionLabelKeywordRegex = new(
        @"(?:\b(?:date|invoice|receipt|bill|number|due|total|subtotal|tax|vat|gst|balance|amount|paid|page|tel|phone|email|terms|reference|po|customer|bill\s*to|ship\s*to)\b|\bno\.|#)",
        LinearI);

    private static readonly Regex LegalSuffixRegex = new(
        @"\b(?:ltd\.?|limited|llc|l\.l\.c\.?|inc\.?|incorporated|corp\.?|corporation|llp|pte\.?\s*ltd\.?|pte\.?|sdn\.?\s*bhd\.?|gmbh|co\.?,?\s*ltd\.?|co\.)\b|บริษัท.*จำกัด",
        LinearI);

    // K2: strong paid cues + explicit negations
    private static readonly Regex PaidNegationRegex = new(
        @"\b(?:not\s+paid|unpaid|if\s+paid|paid\s+within|unless\s+paid|when\s+paid|subject\s+to\s+change)\b",
        LinearI);

    private static readonly Regex StrongPaidLineRegex = new(
        @"^(?:paid(?:\s+in\s+full|\s+by\s+(?:cash|card|bank|transfer|check|cheque))?|payment\s+received)\s*[!.]?$",
        LinearI);

    private static readonly Regex TenderOrChangeCueRegex = new(
        @"\b(?:change|cash|tendered)\b",
        LinearI);

    private static readonly Regex BalanceZeroCueRegex = new(
        @"(?:balance|amount)\s*due\s*[:#\-]?\s*(?:[A-Z]{3}\s*)?[$€£฿]?\s*0(?:[.,]00)?\s*$",
        LinearI);

    private static readonly Regex AmountDueLabelRegex = new(
        @"(?:amount\s*due|balance\s*due)",
        LinearI);

    private static readonly Regex AmountPaidLabelRegex = new(
        @"(?:amount\s*paid|total\s*paid|paid\s*amount)\s*[:#\-]?\s*(.*)$",
        LinearI);

    // K11: NonBacktracking line-item table row parser
    private static readonly Regex LineItemRowRegex = new(
        @"^(.+?)\s+(\d+(?:\.\d{1,4})?)\s+(?:(?:S\$|A\$|C\$|HK\$|NZ\$|RM|Rp|[A-Z]{3})\s*)?[$€£฿]?\s*(\d{1,3}(?:[,\s]\d{3})+|\d+)[.,](\d{2})\s+(?:(?:S\$|A\$|C\$|HK\$|NZ\$|RM|Rp|[A-Z]{3})\s*)?[$€£฿]?\s*(\d{1,3}(?:[,\s]\d{3})+|\d+)[.,](\d{2})\s*$",
        Linear);

    private static readonly HashSet<string> UrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ftp", "ftps", "mailto", "tel", "file", "data", "ws", "wss", "javascript"
    };

    public static ParsedFields Parse(string kind, IReadOnlyList<TextLine> lines) =>
        Parse(kind, lines, TimeProvider.System, DefaultParseDeadline);

    internal static ParsedFields Parse(
        string kind,
        IReadOnlyList<TextLine> lines,
        TimeProvider timeProvider,
        TimeSpan? deadline = null)
    {
        if (!DocumentCaptureFieldSchemas.IsSupportedKind(kind))
            return new ParsedFields("{}", Array.Empty<string>());

        var effectiveDeadline = deadline ?? DefaultParseDeadline;
        var startTs = timeProvider.GetTimestamp();
        var truncated = false;
        bool CheckTimedOut()
        {
            if (truncated) return true;
            if (timeProvider.GetElapsedTime(startTs) >= effectiveDeadline)
            {
                truncated = true;
                return true;
            }
            return false;
        }

        var normalized = NormalizeBoundedLines(lines);
        var warnings = new List<string>();
        Dictionary<string, object?> payload;

        switch (kind)
        {
            case "expense-receipt":
            case "purchase-invoice":
                payload = ParseFinancial(normalized, isSales: false, warnings, CheckTimedOut);
                break;
            case "sales-invoice":
                payload = ParseFinancial(normalized, isSales: true, warnings, CheckTimedOut);
                break;
            case "bank":
                payload = BankFieldParser.Parse(normalized.Select(l => l.Text).ToList(), CheckTimedOut);
                break;
            case "assistant-attachment":
                payload = ParseAssistantAttachment(normalized, CheckTimedOut);
                break;
            default:
                payload = new Dictionary<string, object?>();
                break;
        }

        if (truncated)
            warnings.Add(WarningParseTruncated);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        if (DocumentCaptureFieldSchemas.TryValidate(kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion, doc.RootElement, out var canonical, out _))
        {
            return new ParsedFields(canonical, warnings);
        }

        var fallback = kind == "assistant-attachment" ? "{\"fields\":[]}" : "{}";
        return new ParsedFields(fallback, warnings);
    }

    private static List<NormalizedLine> NormalizeBoundedLines(IReadOnlyList<TextLine>? lines)
    {
        var result = new List<NormalizedLine>();
        if (lines is null || lines.Count == 0)
            return result;

        var remaining = MaxTotalInputChars;
        foreach (var line in lines)
        {
            if (remaining <= 0)
                break;
            if (line is null)
                continue;

            var clean = DateAndAmountParser.SanitizeLineText(line.Text);
            if (string.IsNullOrEmpty(clean))
                continue;

            if (clean.Length > remaining)
                clean = clean[..remaining].TrimEnd();
            if (string.IsNullOrEmpty(clean))
                continue;

            remaining -= clean.Length;
            var height = float.IsFinite(line.Y1 - line.Y0) ? Math.Max(0f, line.Y1 - line.Y0) : 0f;
            result.Add(new NormalizedLine(line.Page, clean, height));
        }

        return result;
    }

    private static Dictionary<string, object?> ParseFinancial(
        List<NormalizedLine> lines,
        bool isSales,
        List<string> warnings,
        Func<bool> isTimedOut)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (lines.Count == 0)
        {
            fields["lines"] = Array.Empty<object>();
            return fields;
        }

        var texts = lines.Select(l => l.Text).ToList();

        // Party name
        if (!isTimedOut())
        {
            var party = isSales ? ExtractSalesCustomer(texts) : ExtractVendorName(lines);
            if (!string.IsNullOrWhiteSpace(party))
                fields[isSales ? "customerName" : "vendorName"] = Truncate(party, DocumentCaptureFieldSchemas.MaxTextLength);
        }

        // Invoice / receipt number
        if (!isTimedOut())
        {
            var docNo = ExtractInvoiceNumber(texts);
            if (!string.IsNullOrWhiteSpace(docNo))
                fields[isSales ? "sourceInvoiceNo" : "vendorInvoiceNo"] = Truncate(docNo, 200);
        }

        // Dates
        string? dueDate = null;
        if (!isTimedOut())
        {
            var (docDate, parsedDueDate) = ExtractDates(texts);
            dueDate = parsedDueDate;
            if (docDate is not null)
                fields["date"] = docDate;
            if (dueDate is not null)
                fields["dueDate"] = dueDate;
        }

        // Currency
        if (!isTimedOut())
        {
            var (currency, ambiguousDollar) = DateAndAmountParser.DetectCurrency(texts);
            if (currency is not null)
                fields["currency"] = currency;
            else if (ambiguousDollar)
                warnings.Add(WarningAmbiguousCurrency);
        }

        // Totals
        decimal? parsedGrandTotal = null;
        decimal? parsedSubtotal = null;
        decimal? parsedTaxTotal = null;
        var parsedIsInclusive = false;
        if (!isTimedOut())
        {
            var (grandTotal, subtotal, taxTotal, isInclusive, usedUnlabeledFallback) = ExtractTotals(texts);
            parsedGrandTotal = grandTotal;
            parsedSubtotal = subtotal;
            parsedTaxTotal = taxTotal;
            if (usedUnlabeledFallback)
                warnings.Add(WarningTotalUnlabeled);

            if (grandTotal.HasValue || subtotal.HasValue || taxTotal.HasValue)
            {
                // K5 + L2:
                // - if subtotal + tax == total (within 0.01), tax is on top -> never mark inclusive.
                // - if subtotal == total (within 0.01) and tax > 0, treat as inclusive and do not warn.
                // - warn TOTALS_INCONSISTENT only when neither relation holds.
                var taxOnTopMatches = subtotal.HasValue && taxTotal.HasValue && grandTotal.HasValue
                    && Math.Abs(subtotal.Value + taxTotal.Value - grandTotal.Value) <= 0.01m;
                var inclusiveShapeMatches = subtotal.HasValue && taxTotal.HasValue && grandTotal.HasValue
                    && taxTotal.Value > 0m && Math.Abs(subtotal.Value - grandTotal.Value) <= 0.01m;

                if (taxOnTopMatches)
                    isInclusive = false;
                else if (inclusiveShapeMatches)
                    isInclusive = true;

                parsedIsInclusive = isInclusive;

                var totals = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (subtotal.HasValue) totals["subtotal"] = DateAndAmountParser.FormatDecimal2(subtotal.Value);
                if (taxTotal.HasValue) totals["taxTotal"] = DateAndAmountParser.FormatDecimal2(taxTotal.Value);
                if (grandTotal.HasValue) totals["grandTotal"] = DateAndAmountParser.FormatDecimal2(grandTotal.Value);
                if (isInclusive) totals["priceBasis"] = "inclusive";
                fields["sourceTotals"] = totals;

                if (subtotal.HasValue && taxTotal.HasValue && grandTotal.HasValue
                    && !taxOnTopMatches
                    && !inclusiveShapeMatches)
                {
                    warnings.Add(WarningTotalsInconsistent);
                }
            }
        }

        // E1: emit lines only when their sum reconciles to subtotal (or grandTotal with inclusive/no tax); never emit a partial table.
        if (isTimedOut())
        {
            fields["lines"] = new List<Dictionary<string, object?>>();
        }
        else
        {
            var (extractedLines, lineSum) = ExtractTableLines(texts, isTimedOut);
            var linesReconciled = false;
            if (extractedLines.Count > 0)
            {
                if (parsedSubtotal.HasValue)
                {
                    linesReconciled = Math.Abs(lineSum - parsedSubtotal.Value) <= 0.01m;
                }
                else if (parsedGrandTotal.HasValue && (parsedIsInclusive || !parsedTaxTotal.HasValue || parsedTaxTotal.Value == 0m))
                {
                    linesReconciled = Math.Abs(lineSum - parsedGrandTotal.Value) <= 0.01m;
                }
            }

            fields["lines"] = linesReconciled ? extractedLines : new List<Dictionary<string, object?>>();
        }

        // Payment hints (bill kinds only)
        if (!isSales && !isTimedOut())
        {
            fields["paymentHints"] = ExtractPaymentHints(texts, dueDate is not null, parsedGrandTotal);
        }

        return fields;
    }

    private static (string? DocDate, string? DueDate) ExtractDates(List<string> lines)
    {
        string? labeledDocDate = null;
        string? fallbackDocDate = null;
        string? dueDate = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var next = i + 1 < lines.Count ? lines[i + 1] : null;
            var isDueLine = DueDateLabelRegex.IsMatch(line);

            if (isDueLine)
            {
                // K3: use next line only when the due-date label line holds no digits.
                dueDate ??= DateAndAmountParser.TryExtractDate(line)
                    ?? (!DateAndAmountParser.ContainsDigit(line) && next is not null
                        ? DateAndAmountParser.TryExtractDate(next)
                        : null);
                continue;
            }

            if (labeledDocDate is null && DateLabelRegex.IsMatch(line))
            {
                labeledDocDate = DateAndAmountParser.TryExtractDate(line)
                    ?? (!DateAndAmountParser.ContainsDigit(line) && next is not null
                        ? DateAndAmountParser.TryExtractDate(next)
                        : null);
            }

            if (fallbackDocDate is null)
            {
                fallbackDocDate = DateAndAmountParser.TryExtractDate(line);
            }
        }

        return (labeledDocDate ?? fallbackDocDate, dueDate);
    }

    /// <summary>True for a line the parser reads as a total, subtotal or tax line, using the same label patterns as the totals rules.</summary>
    internal static bool IsTotalsLabelLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (TotalKeywordRegex.IsMatch(line) || SubtotalLabelRegex.IsMatch(line) || TaxLabelRegex.IsMatch(line));

    private static bool IsGrandTotalLine(string line)
    {
        if (!TotalKeywordRegex.IsMatch(line))
            return false;
        if (NotTotalKeywordRegex.IsMatch(line))
            return false;

        // K6: allow total lines containing an inclusive-tax phrase ("Total incl. tax: 110.00"),
        // while still excluding standalone tax lines ("Total Tax: 10.00").
        if (StandaloneTaxWordRegex.IsMatch(line))
        {
            var withoutInclusive = InclusiveTaxPhraseRegex.Replace(line, " ");
            if (StandaloneTaxWordRegex.IsMatch(withoutInclusive))
                return false;
        }

        return true;
    }

    private static (decimal? GrandTotal, decimal? Subtotal, decimal? TaxTotal, bool IsInclusive, bool UsedUnlabeledFallback) ExtractTotals(List<string> lines)
    {
        decimal? grandTotal = null;
        decimal? subtotal = null;
        decimal? taxTotal = null;
        var isInclusive = false;
        var usedUnlabeledFallback = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var next = i + 1 < lines.Count ? lines[i + 1] : null;

            // E5: a line matching the table-row pattern is never a tax, subtotal, or total line.
            if (line.Length <= 300 && LineItemRowRegex.IsMatch(line))
                continue;

            var isTotalLine = IsGrandTotalLine(line);

            // K5: only set inclusive on tax-tied cues on non-total lines
            if (!isTotalLine && InclusiveTaxPhraseRegex.IsMatch(line))
                isInclusive = true;

            if (isTotalLine)
            {
                var found = FirstMaxAmountInWindow(line, next);
                if (found.HasValue)
                    grandTotal = grandTotal.HasValue ? Math.Max(grandTotal.Value, found.Value) : found.Value;
            }

            if (!subtotal.HasValue && SubtotalLabelRegex.IsMatch(line))
            {
                subtotal = FirstMaxAmountInWindow(line, next);
            }

            // K4 + L2: exclude registration/ID labels from tax lines; detect inclusive cues on tax lines
            var isTaxLine = !isTotalLine && TaxLabelRegex.IsMatch(line) && !TaxHeaderExclusionRegex.IsMatch(line);
            if (isTaxLine && InclusiveWordRegex.IsMatch(line))
                isInclusive = true;

            if (!taxTotal.HasValue && isTaxLine)
            {
                taxTotal = FirstMaxAmountInWindow(line, next);
            }
        }

        // K6: fallback to largest amount anywhere only with TOTAL_UNLABELED warning
        if (!grandTotal.HasValue)
        {
            decimal? maxAny = null;
            foreach (var line in lines)
            {
                foreach (var v in DateAndAmountParser.ExtractAmounts(line))
                    maxAny = maxAny.HasValue ? Math.Max(maxAny.Value, v) : v;
            }
            if (maxAny.HasValue)
            {
                grandTotal = maxAny;
                usedUnlabeledFallback = true;
            }
        }

        return (grandTotal, subtotal, taxTotal, isInclusive, usedUnlabeledFallback);
    }

    // K4: look at nextLine ONLY when the label line contains no digits at all.
    private static decimal? FirstMaxAmountInWindow(string line, string? nextLine)
    {
        var sameLine = DateAndAmountParser.ExtractAmounts(line);
        if (sameLine.Count > 0)
            return sameLine.Max();

        if (!DateAndAmountParser.ContainsDigit(line) && nextLine is not null)
        {
            var nextAmounts = DateAndAmountParser.ExtractAmounts(nextLine);
            if (nextAmounts.Count > 0)
                return nextAmounts.Max();
        }

        return null;
    }

    // E2 + E6 + E7 + E10:
    // - Vendor candidates come only from the top block, stopping at recipient headers, document-number/date label lines,
    //   table headers, or any line containing an amount.
    // - Lines with amounts or table-row shape are never candidates.
    // - Single-word lines after an address line (city/region/country) are address continuations, never candidates.
    // - The issuer is the first qualifying top-block line; a later top-block line replaces it only for a legal suffix
    //   or as a height tie-breaker (>= 1.3x height) among candidates with the same legal-suffix status.
    private static string? ExtractVendorName(List<NormalizedLine> lines)
    {
        var page1 = lines.Where(l => l.Page <= 1).Take(12).ToList();
        var candidates = page1.Count > 0 ? page1 : lines.Take(12).ToList();
        if (candidates.Count == 0)
            return null;

        string? bestText = null;
        var bestHasLegalSuffix = false;
        var bestHeight = 0f;
        var seenAddressLine = false;

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var text = c.Text;

            if (VendorBlockStopRegex.IsMatch(text)
                || TableHeaderRowRegex.IsMatch(text)
                || InvoiceNumberRegex.IsMatch(text)
                || InvoiceNumberLabelOnlyRegex.IsMatch(text)
                || DateLabelRegex.IsMatch(text)
                || DueDateLabelRegex.IsMatch(text)
                || DateAndAmountParser.ExtractAmounts(text).Count > 0)
            {
                break;
            }

            if (text.Length <= 300 && LineItemRowRegex.IsMatch(text))
                continue;

            var isAddress = IsAddressLine(text);
            if (isAddress)
            {
                seenAddressLine = true;
                continue;
            }

            // E7: strip standalone document-title tokens before scoring (e.g., "Jane Example INVOICE" -> "Jane Example").
            // A line that is only a title becomes empty and stays disqualified.
            var candidateText = DocumentTitleTokenRegex.Replace(text, " ");
            candidateText = MultiSpaceRegex.Replace(candidateText, " ").Trim(' ', '-', ':', '|', '/');

            if (!IsValidPartyCandidateShape(candidateText))
                continue;

            var letters = CountLetters(candidateText);
            var digits = CountDigits(candidateText);
            if (VendorPenaltyRegex.IsMatch(candidateText) || (digits >= letters && digits > 0))
                continue;

            var hasLegalSuffix = LegalSuffixRegex.IsMatch(candidateText);
            var isSingleWord = !candidateText.Contains(' ', StringComparison.Ordinal);

            // E6: single-word lines after an address line are city/region/country continuations, never vendor names.
            if (seenAddressLine && isSingleWord && !hasLegalSuffix)
                continue;

            if (bestText is null)
            {
                bestText = candidateText;
                bestHasLegalSuffix = hasLegalSuffix;
                bestHeight = c.Height;
                continue;
            }

            // E10: legal suffix beats a plain line; height only breaks ties (>= 1.3x) among top-block candidates.
            var heightTieBreaker = hasLegalSuffix == bestHasLegalSuffix
                && bestHeight > 0.5f
                && c.Height >= bestHeight * 1.3f
                && (c.Height - bestHeight) >= 3.0f;
            if ((hasLegalSuffix && !bestHasLegalSuffix) || heightTieBreaker)
            {
                bestText = candidateText;
                bestHasLegalSuffix = hasLegalSuffix;
                bestHeight = c.Height;
            }
        }

        return bestText;
    }

    // E2: catch standard street addresses, Unit/Suite/Floor/Tower prefix + number, and comma-separated multi-part lines with numbers.
    private static bool IsAddressLine(string text)
    {
        if (AddressPatternRegex.IsMatch(text) || AddressUnitPrefixRegex.IsMatch(text))
            return true;

        if (text.Contains(',', StringComparison.Ordinal)
            && DateAndAmountParser.ContainsDigit(text)
            && !LegalSuffixRegex.IsMatch(text))
        {
            return true;
        }

        return false;
    }

    // K12 + E9: anchored labels, skip Customer Service/ID, ignore merged right-column label phrases, reject digit-heavy / phone-like candidates.
    private static string? ExtractSalesCustomer(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (BillToExclusionRegex.IsMatch(line))
                continue;

            var m = BillToLabelRegex.Match(line);
            var matchedWithTrailingLabel = !m.Success && BillToWithTrailingLabelRegex.IsMatch(line);
            if (!m.Success && !matchedWithTrailingLabel)
                continue;

            if (m.Success)
            {
                var inline = StripTrailingColumnLabelPhrase(m.Groups[1].Value);
                if (IsValidCustomerCandidate(inline))
                    return inline;
            }

            for (var j = i + 1; j < Math.Min(lines.Count, i + 4); j++)
            {
                var cand = StripTrailingColumnLabelPhrase(lines[j]);
                if (IsValidCustomerCandidate(cand))
                    return cand;
            }
        }

        return null;
    }

    private static string StripTrailingColumnLabelPhrase(string raw) =>
        TrailingColumnLabelPhraseRegex.Replace(raw, string.Empty).Trim(' ', ':', '-', '|');

    private static bool IsValidCustomerCandidate(string cand)
    {
        if (!IsValidPartyCandidateShape(cand))
            return false;
        var letters = CountLetters(cand);
        var digits = CountDigits(cand);
        if (digits >= letters)
            return false;
        if (IsAddressLine(cand) || VendorPenaltyRegex.IsMatch(cand))
            return false;
        return true;
    }

    // L3: vendor and customer candidates must be <= 120 chars and at least 40% letters among non-space chars.
    private static bool IsValidPartyCandidateShape(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2 || text.Length > 120)
            return false;

        var letters = 0;
        var nonSpace = 0;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            nonSpace++;
            if (char.IsLetter(ch))
                letters++;
        }

        return letters >= 2 && nonSpace > 0 && letters * 5 >= nonSpace * 2;
    }

    private static string? ExtractInvoiceNumber(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var m = InvoiceNumberRegex.Match(lines[i]);
            if (m.Success)
            {
                var val = m.Groups[1].Value.Trim();
                if (val.Length >= 2 || DateAndAmountParser.ContainsDigit(val))
                    return val;
            }

            if (InvoiceNumberLabelOnlyRegex.IsMatch(lines[i]) && i + 1 < lines.Count)
            {
                var next = lines[i + 1].Trim();
                if (next.Length is >= 1 and <= 64
                    && !next.Contains(' ', StringComparison.Ordinal)
                    && (next.Length >= 2 || DateAndAmountParser.ContainsDigit(next)))
                {
                    return next;
                }
            }
        }

        // E8: fallback to standalone "#<token>" (with at least one digit) in top 10 lines before recipient/table header.
        var topLimit = Math.Min(lines.Count, 10);
        for (var i = 0; i < topLimit; i++)
        {
            var line = lines[i];
            if (VendorBlockStopRegex.IsMatch(line) || TableHeaderRowRegex.IsMatch(line))
                break;

            if (line.Length <= 300 && LineItemRowRegex.IsMatch(line))
                continue;

            foreach (Match m in BareHashTokenRegex.Matches(line))
            {
                // Equivalent to (?<!\S) without lookbehind (keeps NonBacktracking).
                if (m.Index > 0 && !char.IsWhiteSpace(line[m.Index - 1]))
                    continue;

                var end = m.Index + m.Length;
                if (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not (',' or ';' or ')'))
                    continue;

                var val = m.Groups[1].Value;
                if (!DateAndAmountParser.ContainsDigit(val))
                    continue;

                var prefix = line[..m.Index].TrimEnd();
                if (AddressHashPrefixRegex.IsMatch(prefix))
                    continue;

                return val;
            }
        }

        return null;
    }

    private static (List<Dictionary<string, object?>> Lines, decimal Sum) ExtractTableLines(List<string> lines, Func<bool> isTimedOut)
    {
        var result = new List<Dictionary<string, object?>>();
        var sum = 0m;
        foreach (var line in lines)
        {
            if (result.Count >= DocumentCaptureFieldSchemas.MaxFinancialLines || isTimedOut())
                break;

            if (line.Length > 300)
                continue;

            if (TotalKeywordRegex.IsMatch(line) || SubtotalLabelRegex.IsMatch(line) || TaxLabelRegex.IsMatch(line))
                continue;

            var m = LineItemRowRegex.Match(line);
            if (!m.Success)
                continue;

            var desc = m.Groups[1].Value.Trim();
            // E3: strip leading row-number column prefix (e.g., "1 Consulting services") when >= 2 letters remain.
            var strippedDesc = LeadingRowIndexRegex.Replace(desc, string.Empty).Trim();
            if (CountLetters(strippedDesc) >= 2)
                desc = strippedDesc;
            if (desc.Length == 0 || CountLetters(desc) < 2)
                continue;

            // E1b: reject spliced header/label rows whose description has a label keyword, a date, or a "Label: value" form.
            if (IsDisqualifiedLineDescription(desc))
                continue;

            if (!decimal.TryParse(m.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var qty)
                || qty <= 0m)
                continue;

            var unitStr = StripSeps(m.Groups[3].Value) + "." + m.Groups[4].Value;
            var totalStr = StripSeps(m.Groups[5].Value) + "." + m.Groups[6].Value;
            if (!decimal.TryParse(unitStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var unit)
                || !decimal.TryParse(totalStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var lineTotal))
                continue;

            if (Math.Abs(Math.Round(qty * unit, 2, MidpointRounding.AwayFromZero) - lineTotal) > 0.01m)
                continue;

            sum += lineTotal;
            result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["description"] = Truncate(desc, DocumentCaptureFieldSchemas.MaxLineDescriptionLength),
                ["quantity"] = DateAndAmountParser.FormatQuantity(qty),
                ["unitAmount"] = DateAndAmountParser.FormatDecimal2(unit),
                ["sourceLineTotal"] = DateAndAmountParser.FormatDecimal2(lineTotal),
            });
        }

        return (result, sum);
    }

    private static bool IsDisqualifiedLineDescription(string desc)
    {
        if (LineItemDescriptionLabelKeywordRegex.IsMatch(desc))
            return true;

        var colonIdx = desc.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx >= 0 && colonIdx < desc.Length - 1 && !string.IsNullOrWhiteSpace(desc[(colonIdx + 1)..]))
            return true;

        if (DateAndAmountParser.TryExtractDate(desc) is not null)
            return true;

        return false;
    }

    // K2 + L1: strong paid cues only, negations never count, positive amount due always wins;
    // tendered amounts and amountPaid > grandTotal + 0.01 never fill amountPaid (keep paid status).
    private static Dictionary<string, object?> ExtractPaymentHints(List<string> lines, bool hasDueDate, decimal? grandTotal)
    {
        var hints = new Dictionary<string, object?>(StringComparer.Ordinal);
        var isPaid = false;
        var hasPositiveAmountDue = false;
        decimal? amountPaid = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var next = i + 1 < lines.Count ? lines[i + 1] : null;
            var negated = PaidNegationRegex.IsMatch(line);

            if (!negated)
            {
                if (StrongPaidLineRegex.IsMatch(line) || BalanceZeroCueRegex.IsMatch(line))
                {
                    isPaid = true;
                }
                else if (TenderOrChangeCueRegex.IsMatch(line) && DateAndAmountParser.ExtractAmounts(line).Count > 0)
                {
                    isPaid = true;
                }
            }

            if (AmountDueLabelRegex.IsMatch(line))
            {
                var amt = FirstMaxAmountInWindow(line, next);
                if (amt.HasValue && amt.Value > 0m)
                    hasPositiveAmountDue = true;
            }

            if (!amountPaid.HasValue && !line.Contains("tender", StringComparison.OrdinalIgnoreCase))
            {
                var paidMatch = AmountPaidLabelRegex.Match(line);
                if (paidMatch.Success)
                {
                    var valPart = paidMatch.Groups[1].Value;
                    var amounts = DateAndAmountParser.ExtractAmounts(valPart);
                    if (amounts.Count > 0)
                        amountPaid = amounts.Max();
                    else if (!DateAndAmountParser.ContainsDigit(valPart) && next is not null)
                    {
                        var nextAmounts = DateAndAmountParser.ExtractAmounts(next);
                        if (nextAmounts.Count > 0)
                            amountPaid = nextAmounts.Max();
                    }
                }
            }
        }

        // L1: if labelled amountPaid exceeds grandTotal by > 0.01, it is cash tendered, not document payment.
        if (amountPaid.HasValue && grandTotal.HasValue && amountPaid.Value > grandTotal.Value + 0.01m)
        {
            isPaid = true;
            amountPaid = null;
        }

        string status;
        if (hasPositiveAmountDue)
        {
            status = amountPaid is > 0m ? "partial" : "unpaid";
        }
        else if (isPaid)
        {
            status = "paid";
        }
        else if (amountPaid is > 0m && grandTotal is > 0m)
        {
            // M1: explicit amountPaid >= grandTotal (within 0.01) => paid; 0 < amountPaid < grandTotal => partial.
            status = amountPaid.Value + 0.01m >= grandTotal.Value ? "paid" : "partial";
        }
        else if (hasDueDate)
        {
            status = "unpaid";
        }
        else
        {
            status = "unknown";
        }

        hints["paymentStatus"] = status;
        if (amountPaid.HasValue)
            hints["amountPaid"] = DateAndAmountParser.FormatDecimal2(amountPaid.Value);

        return hints;
    }

    // K13: label must have >= 2 letters, not be a URL scheme, and next char after ':' must not be '/'.
    private static Dictionary<string, object?> ParseAssistantAttachment(List<NormalizedLine> lines, Func<bool> isTimedOut)
    {
        var items = new List<Dictionary<string, object?>>();
        foreach (var line in lines)
        {
            if (items.Count >= DocumentCaptureFieldSchemas.MaxAssistantFields || isTimedOut())
                break;

            var colon = line.Text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon >= line.Text.Length - 1)
                continue;

            if (line.Text[colon + 1] == '/')
                continue;

            var label = line.Text[..colon].Trim();
            var value = line.Text[(colon + 1)..].Trim();
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value))
                continue;

            if (CountLetters(label) < 2 || UrlSchemes.Contains(label))
                continue;

            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = Truncate(label, 200),
                ["value"] = Truncate(value, DocumentCaptureFieldSchemas.MaxTextLength),
            });
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fields"] = items,
        };
    }

    private static int CountLetters(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
                count++;
        }
        return count;
    }

    private static int CountDigits(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
                count++;
        }
        return count;
    }

    private static string StripSeps(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength].TrimEnd();

    private sealed record NormalizedLine(int Page, string Text, float Height);
}
