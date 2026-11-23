// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Api.DocumentCaptures.Parsing;

namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// The fields to store after a mapper ran, the warnings to show, and for every field where the mapper disagreed with the rules,
/// the value the rules found (so the review step can say "rules found ...").
/// </summary>
public sealed record CaptureMappingGuardResult(
    string FieldsJson,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> RuleValueHints)
{
    public void Deconstruct(out string fieldsJson, out IReadOnlyList<string> warnings)
    {
        fieldsJson = FieldsJson;
        warnings = Warnings;
    }
}

/// <summary>
/// Pure guards between a field mapper and storage (rules G-AI-01 to G-AI-05). The mapper can only suggest: nothing it returns is
/// kept unless it appears in the document's own text, the arithmetic holds, and it stays inside the kind's schema.
/// </summary>
public static partial class CaptureMappingGuards
{
    public const string WarningNotInSource = "AI_VALUE_NOT_IN_SOURCE";
    public const string WarningTotalsInconsistent = "AI_TOTALS_INCONSISTENT";
    public const string WarningDisagreesWithRules = "AI_DISAGREES_WITH_RULES";
    public const string WarningUnavailable = "AI_UNAVAILABLE";

    private const decimal Cent = 0.01m;
    private static readonly string[] TotalAmountNames = ["subtotal", "taxTotal", "grandTotal", "discountTotal", "additionalCharges"];
    private static readonly string[] AmountLineFields = ["unitAmount", "sourceLineTotal"];
    private static readonly string[] NumberLineFields = ["quantity", "discount"];
    private static readonly string[] OtherLineFields = ["description", "taxHint", "priceBasis", "missingQuantityDefaulted"];

    public static CaptureMappingGuardResult Apply(string kind, string ruleFieldsJson, string mappedFieldsJson, IReadOnlyList<string> sourceLines)
    {
        // Only financial documents are ever mapped. Bank and assistant-attachment captures keep the rules' fields (G-AI-04).
        if (!CaptureMappingRequestFactory.IsMappableKind(kind) || string.Equals(ruleFieldsJson, mappedFieldsJson, StringComparison.Ordinal))
            return Same(ruleFieldsJson);

        JsonObject? rule;
        JsonObject? mapped;
        try
        {
            rule = JsonNode.Parse(ruleFieldsJson) as JsonObject;
            // G-AI-04: nothing outside the kind's schema survives, so no bank.* fields and no stray properties.
            mapped = JsonNode.Parse(DocumentCaptureFieldSchemas.SanitizeProviderFields(kind, mappedFieldsJson)) as JsonObject;
        }
        catch (JsonException)
        {
            return Unavailable(ruleFieldsJson);
        }
        if (rule is null || mapped is null)
            return Unavailable(ruleFieldsJson);

        // The mapper never names its own sources: they are computed here from what actually changed.
        mapped.Remove("fieldSources");
        mapped.Remove("paymentHints");

        var source = new SourceIndex(sourceLines);
        var result = (JsonObject)rule.DeepClone();
        var warnings = new List<string>();
        var aiPaths = new SortedSet<string>(StringComparer.Ordinal);
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = false;

        var isSales = kind == "sales-invoice";
        foreach (var name in new[] { isSales ? "customerName" : "vendorName", isSales ? "sourceInvoiceNo" : "vendorInvoiceNo", "referenceNo", "date", "dueDate", "currency" })
        {
            var suggested = Text(mapped[name]);
            if (suggested is null)
                continue;
            var found = Text(rule[name]);
            if (string.Equals(found, suggested, StringComparison.Ordinal))
                continue;
            // G-AI-01: a date, document number, reference or currency the mapper returns must be written in the document.
            if (name is "date" or "dueDate" ? !source.HasDate(suggested)
                : name is "referenceNo" or "sourceInvoiceNo" or "vendorInvoiceNo" or "currency" ? !source.HasToken(suggested)
                : false)
            {
                Warn(warnings, WarningNotInSource);
                continue;
            }
            result[name] = suggested;
            aiPaths.Add(name);
            Disagree(name, found, hints, warnings);
            changed = true;
        }

        if (mapped["sourceTotals"] is JsonObject mappedTotals)
        {
            var candidate = (result["sourceTotals"] as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject();
            var pending = new List<(string Path, string? Found)>();
            foreach (var name in TotalAmountNames)
            {
                var suggested = Text(mappedTotals[name]);
                if (suggested is null)
                    continue;
                var found = Text(candidate[name]);
                if (AmountsEqual(found, suggested))
                    continue;
                if (!source.HasAmount(suggested))
                {
                    Warn(warnings, WarningNotInSource);
                    continue;
                }
                candidate[name] = suggested;
                pending.Add(("sourceTotals." + name, found));
            }
            if (Text(candidate["priceBasis"]) is null && Text(mappedTotals["priceBasis"]) is "inclusive" or "exclusive")
            {
                candidate["priceBasis"] = Text(mappedTotals["priceBasis"]);
                pending.Add(("sourceTotals.priceBasis", null));
            }

            if (pending.Count > 0)
            {
                // G-AI-02: the totals must add up. If they don't, the rules' totals win.
                if (!TotalsAddUp(candidate))
                {
                    Warn(warnings, WarningTotalsInconsistent);
                }
                else
                {
                    result["sourceTotals"] = candidate;
                    foreach (var (path, found) in pending)
                    {
                        aiPaths.Add(path);
                        Disagree(path, found, hints, warnings);
                    }
                    changed = true;
                }
            }
        }

        if (mapped["lines"] is JsonArray mappedLines && mappedLines.Count > 0 && !JsonNode.DeepEquals(mappedLines, rule["lines"]))
        {
            var ruleLines = rule["lines"] as JsonArray;
            var accepted = LinesAreSourced(mappedLines, ruleLines, source, warnings)
                && LinesAddUp(mappedLines, result["sourceTotals"] as JsonObject, warnings);
            if (accepted)
            {
                result["lines"] = mappedLines.DeepClone();
                for (var i = 0; i < mappedLines.Count; i++)
                {
                    var line = (JsonObject)mappedLines[i]!;
                    var before = ruleLines is not null && i < ruleLines.Count ? ruleLines[i] as JsonObject : null;
                    foreach (var property in line)
                    {
                        var found = before is null ? null : Text(before[property.Key]);
                        var suggested = Text(property.Value);
                        if (string.Equals(found, suggested, StringComparison.Ordinal))
                            continue;
                        var path = $"lines[{i}].{property.Key}";
                        aiPaths.Add(path);
                        Disagree(path, found, hints, warnings);
                    }
                }
                changed = true;
            }
        }

        if (!changed && warnings.Count == 0)
            return Same(ruleFieldsJson);

        if (aiPaths.Count > 0)
        {
            var sources = new JsonObject();
            foreach (var path in aiPaths)
                sources[path] = "ai";
            result["fieldSources"] = sources;
        }
        return new CaptureMappingGuardResult(result.ToJsonString(), warnings, hints);
    }

    private static CaptureMappingGuardResult Same(string ruleFieldsJson) =>
        new(ruleFieldsJson, Array.Empty<string>(), new Dictionary<string, string>());

    private static CaptureMappingGuardResult Unavailable(string ruleFieldsJson) =>
        new(ruleFieldsJson, [WarningUnavailable], new Dictionary<string, string>());

    private static void Warn(List<string> warnings, string code)
    {
        if (!warnings.Contains(code))
            warnings.Add(code);
    }

    // G-AI-03: where the rules found a different value, the mapper's value is only a suggestion and the rules' value is kept as a hint.
    private static void Disagree(string path, string? found, Dictionary<string, string> hints, List<string> warnings)
    {
        if (found is null)
            return;
        hints[path] = found.Length > DocumentCaptureFieldSchemas.MaxLineDescriptionLength ? found[..DocumentCaptureFieldSchemas.MaxLineDescriptionLength] : found;
        Warn(warnings, WarningDisagreesWithRules);
    }

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) => text.Trim(),
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        _ => null
    };

    private static decimal? Amount(string? text) =>
        text is not null && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool AmountsEqual(string? a, string? b) =>
        Amount(a) is { } x && Amount(b) is { } y ? Math.Round(x, 2) == Math.Round(y, 2) : string.Equals(a, b, StringComparison.Ordinal);

    private static bool TotalsAddUp(JsonObject totals)
    {
        var subtotal = Amount(Text(totals["subtotal"]));
        var tax = Amount(Text(totals["taxTotal"]));
        var grand = Amount(Text(totals["grandTotal"]));
        if (subtotal is null || tax is null || grand is null)
            return true;
        var discount = Amount(Text(totals["discountTotal"])) ?? 0m;
        var charges = Amount(Text(totals["additionalCharges"])) ?? 0m;
        var inclusive = Text(totals["priceBasis"]) == "inclusive";
        return Math.Abs(subtotal.Value + tax.Value - grand.Value) <= Cent
            || (inclusive && Math.Abs(subtotal.Value - grand.Value) <= Cent)
            || Math.Abs(subtotal.Value - discount + charges + tax.Value - grand.Value) <= Cent;
    }

    private static bool LinesAreSourced(JsonArray mappedLines, JsonArray? ruleLines, SourceIndex source, List<string> warnings)
    {
        for (var i = 0; i < mappedLines.Count; i++)
        {
            if (mappedLines[i] is not JsonObject line)
                return false;
            var before = ruleLines is not null && i < ruleLines.Count ? ruleLines[i] as JsonObject : null;
            foreach (var name in AmountLineFields)
            {
                var suggested = Text(line[name]);
                if (suggested is not null && !AmountsEqual(before is null ? null : Text(before[name]), suggested) && !source.HasAmount(suggested))
                {
                    Warn(warnings, WarningNotInSource);
                    return false;
                }
            }
            foreach (var name in NumberLineFields)
            {
                var suggested = Text(line[name]);
                if (suggested is null || AmountsEqual(before is null ? null : Text(before[name]), suggested))
                    continue;
                // A missing quantity is 1; any other quantity or discount has to be a number the document prints.
                if (name == "quantity" && Amount(suggested) == 1m)
                    continue;
                if (!source.HasNumber(suggested))
                {
                    Warn(warnings, WarningNotInSource);
                    return false;
                }
            }
            if (line.Any(property => !AmountLineFields.Contains(property.Key) && !NumberLineFields.Contains(property.Key) && !OtherLineFields.Contains(property.Key)))
                return false;
        }
        return true;
    }

    private static bool LinesAddUp(JsonArray mappedLines, JsonObject? totals, List<string> warnings)
    {
        var sum = 0m;
        foreach (var node in mappedLines)
        {
            if (node is not JsonObject line)
                return false;
            var lineTotal = Amount(Text(line["sourceLineTotal"]));
            if (lineTotal is null)
            {
                var unit = Amount(Text(line["unitAmount"])) ?? 0m;
                var quantity = Amount(Text(line["quantity"])) ?? 1m;
                var discount = Amount(Text(line["discount"])) ?? 0m;
                lineTotal = Math.Round(quantity * unit * (1m - discount / 100m), 2, MidpointRounding.AwayFromZero);
            }
            sum += lineTotal.Value;
        }

        var subtotal = Amount(Text(totals?["subtotal"]));
        var grand = Amount(Text(totals?["grandTotal"]));
        var tax = Amount(Text(totals?["taxTotal"]));
        var inclusive = Text(totals?["priceBasis"]) == "inclusive";
        var expected = subtotal ?? (grand is not null && (inclusive || tax is null or 0m) ? grand : null);
        if (expected is not null && Math.Abs(sum - expected.Value) > Cent)
        {
            Warn(warnings, WarningTotalsInconsistent);
            return false;
        }
        return true;
    }

    /// <summary>What the document's own text says, normalised, so a mapper's values can be looked up without trusting them.</summary>
    private sealed partial class SourceIndex
    {
        private readonly HashSet<string> _dates = new(StringComparer.Ordinal);
        private readonly HashSet<decimal> _amounts = [];
        private readonly HashSet<decimal> _numbers = [];
        private readonly HashSet<string> _tokenRuns = new(StringComparer.Ordinal);

        public SourceIndex(IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                foreach (var date in DateAndAmountParser.ExtractAllDates(line))
                    _dates.Add(date);
                foreach (var amount in DateAndAmountParser.ExtractAmounts(line))
                    _amounts.Add(Math.Round(amount, 2));
                foreach (Match match in NumberTokenRegex().Matches(line))
                    if (NormalizeNumber(match.Value) is { } number)
                        _numbers.Add(number);

                var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (var start = 0; start < tokens.Length; start++)
                    for (var length = 1; length <= 3 && start + length <= tokens.Length; length++)
                        if (NormalizeToken(string.Concat(tokens.Skip(start).Take(length))) is { Length: > 0 } run)
                            _tokenRuns.Add(run);
            }
        }

        public bool HasDate(string iso) => _dates.Contains(iso);

        public bool HasToken(string value) => NormalizeToken(value) is { Length: > 0 } token && _tokenRuns.Contains(token);

        public bool HasAmount(string value) =>
            Amount(value) is { } amount
            && (_amounts.Contains(Math.Round(amount, 2)) || (amount == Math.Truncate(amount) && _numbers.Contains(amount)));

        public bool HasNumber(string value) => Amount(value) is { } number && _numbers.Contains(number);

        private static string NormalizeToken(string value) =>
            new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        private static decimal? NormalizeNumber(string token)
        {
            // "1,234" and "1.234" with exactly three trailing digits are thousands; one or two trailing digits are decimals.
            var thousands = ThousandsRegex().IsMatch(token);
            var cleaned = thousands ? token.Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal) : token.Replace(',', '.');
            return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        [GeneratedRegex(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
        private static partial Regex NumberTokenRegex();

        [GeneratedRegex(@"^\d{1,3}(?:[.,]\d{3})+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
        private static partial Regex ThousandsRegex();
    }
}
