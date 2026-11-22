// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

internal static class DateAndAmountParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Dictionary<string, int> EnglishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4,
        ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8,
        ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
    };

    private static readonly Dictionary<string, int> ThaiMonths = new(StringComparer.Ordinal)
    {
        ["มกราคม"] = 1, ["กุมภาพันธ์"] = 2, ["มีนาคม"] = 3, ["เมษายน"] = 4,
        ["พฤษภาคม"] = 5, ["มิถุนายน"] = 6, ["กรกฎาคม"] = 7, ["สิงหาคม"] = 8,
        ["กันยายน"] = 9, ["ตุลาคม"] = 10, ["พฤศจิกายน"] = 11, ["ธันวาคม"] = 12,
    };

    private static readonly Regex IsoDateRegex = new(
        @"(?<![\d\-/\.])(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})(?![\d\-/\.])",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex ThaiMonthDateRegex = new(
        @"(?<!\d)(\d{1,2})\s+(มกราคม|กุมภาพันธ์|มีนาคม|เมษายน|พฤษภาคม|มิถุนายน|กรกฎาคม|สิงหาคม|กันยายน|ตุลาคม|พฤศจิกายน|ธันวาคม)\s+(\d{4})(?!\d)",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex DayMonthYearEnRegex = new(
        @"(?<!\d)(\d{1,2})\s+([A-Za-z]{3})[A-Za-z]*\.?,?\s+(\d{2,4})(?!\d)",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex MonthDayYearEnRegex = new(
        @"(?<![A-Za-z])([A-Za-z]{3})[A-Za-z]*\.?\s+(\d{1,2}),?\s+(\d{4})(?!\d)",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex NumericDateRegex = new(
        @"(?<![\d\-/\.])(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})(?![\d\-/\.])",
        RegexOptions.CultureInvariant, RegexTimeout);

    // Matches standard amounts like 1,234.56 / 1 234.56 / 1234.56 / 1.234,56 / 123,45
    private static readonly Regex AmountTokenRegex = new(
        @"(?<![\d.,\-/])(\d{1,3}(?:[,\s]\d{3})+|\d{1,3}(?:\.\d{3})+|\d+)([.,])(\d{2})(?![\d.,\-/])",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex PhoneContextRegex = new(
        @"\b(tel|telephone|phone|fax|mobile|cell|call|hotline|โทร)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex ExplicitIsoCurrencyRegex = new(
        @"\b(USD|EUR|GBP|THB|SGD|MYR|IDR|AUD|CAD|JPY|CHF|NZD|HKD|PHP|VND)\b",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex CurrencyPrefixSymbolRegex = new(
        @"(S\$|A\$|C\$|HK\$|NZ\$|RM\b|Rp\b|[€£฿])",
        RegexOptions.CultureInvariant, RegexTimeout);

    public static string SanitizeLineText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        var prevSpace = true;
        foreach (var ch in raw)
        {
            // Strip zero-width, bidi/RTL override marks, BOM, and control chars
            if (ch is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F'
                or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069'
                or '\uFEFF' or '\0')
            {
                continue;
            }

            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            sb.Append(ch);
            prevSpace = false;
        }

        return sb.ToString().Trim();
    }

    public static string? TryExtractDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            foreach (Match m in IsoDateRegex.Matches(text))
            {
                var iso = TryMakeIsoDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                if (iso is not null) return iso;
            }

            foreach (Match m in ThaiMonthDateRegex.Matches(text))
            {
                if (ThaiMonths.TryGetValue(m.Groups[2].Value, out var month))
                {
                    var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[1].Value);
                    if (iso is not null) return iso;
                }
            }

            foreach (Match m in DayMonthYearEnRegex.Matches(text))
            {
                if (EnglishMonths.TryGetValue(m.Groups[2].Value, out var month))
                {
                    var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[1].Value);
                    if (iso is not null) return iso;
                }
            }

            foreach (Match m in MonthDayYearEnRegex.Matches(text))
            {
                if (EnglishMonths.TryGetValue(m.Groups[1].Value, out var month))
                {
                    var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[2].Value);
                    if (iso is not null) return iso;
                }
            }

            foreach (Match m in NumericDateRegex.Matches(text))
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var first)
                    || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var second))
                    continue;

                // Day-first by default; switch to month-first only when day-first is impossible.
                var iso = second <= 12
                    ? TryMakeIsoDate(m.Groups[3].Value, second, first)
                    : first <= 12
                        ? TryMakeIsoDate(m.Groups[3].Value, first, second)
                        : null;
                if (iso is not null) return iso;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return null;
    }

    public static IReadOnlyList<decimal> ExtractAmounts(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || PhoneContextRegex.IsMatch(line))
            return Array.Empty<decimal>();

        var results = new List<decimal>();
        try
        {
            foreach (Match m in AmountTokenRegex.Matches(line))
            {
                var wholeRaw = m.Groups[1].Value;
                var sep = m.Groups[2].Value[0];
                var frac = m.Groups[3].Value;

                // Reject European-style ambiguous patterns where both dots and commas mix inconsistently
                if (sep == ',')
                {
                    // Comma decimal is valid when whole part has no commas (either plain digits or dot-thousands like 1.234,56)
                    if (wholeRaw.Contains(',', StringComparison.Ordinal))
                        continue;
                }
                else
                {
                    // Dot decimal: whole part cannot contain dots
                    if (wholeRaw.Contains('.', StringComparison.Ordinal))
                        continue;
                }

                // Avoid matching dates like 03.10.2026 (already excluded by lookaround, double-check)
                var digitsOnly = StripSeparators(wholeRaw);
                if (digitsOnly.Length == 0 || digitsOnly.Length > 14)
                    continue;

                var normalized = digitsOnly + "." + frac;
                if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                    && parsed >= 0m)
                {
                    results.Add(parsed);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return results;
        }

        return results;
    }

    public static (string? Currency, bool AmbiguousDollar) DetectCurrency(IReadOnlyList<string> lines)
    {
        var joined = string.Join("\n", lines);
        try
        {
            var isoMatch = ExplicitIsoCurrencyRegex.Match(joined);
            if (isoMatch.Success)
                return (isoMatch.Groups[1].Value, false);

            var symMatch = CurrencyPrefixSymbolRegex.Match(joined);
            if (symMatch.Success)
            {
                var mapped = symMatch.Groups[1].Value switch
                {
                    "€" => "EUR",
                    "£" => "GBP",
                    "฿" => "THB",
                    "S$" => "SGD",
                    "RM" => "MYR",
                    "Rp" => "IDR",
                    "A$" => "AUD",
                    "C$" => "CAD",
                    "HK$" => "HKD",
                    "NZ$" => "NZD",
                    _ => null
                };
                if (mapped is not null)
                    return (mapped, false);
            }

            if (joined.Contains('$', StringComparison.Ordinal))
                return (null, true);
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, false);
        }

        return (null, false);
    }

    public static string FormatDecimal2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatQuantity(decimal value)
    {
        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded == Math.Truncate(rounded)
            ? Math.Truncate(rounded).ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string StripSeparators(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string? TryMakeIsoDate(string yearRaw, string monthRaw, string dayRaw)
    {
        if (!int.TryParse(monthRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(dayRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            return null;
        return TryMakeIsoDate(yearRaw, month, day);
    }

    private static string? TryMakeIsoDate(string yearRaw, int month, string dayRaw)
    {
        if (!int.TryParse(dayRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            return null;
        return TryMakeIsoDate(yearRaw, month, day);
    }

    private static string? TryMakeIsoDate(string yearRaw, int month, int day)
    {
        if (!int.TryParse(yearRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return null;

        if (year < 100)
            year += 2000;
        if (year > 2400)
            year -= 543; // Thai Buddhist Era

        if (year is < 1990 or > 2100 || month is < 1 or > 12 || day is < 1 or > 31)
            return null;

        if (day > DateTime.DaysInMonth(year, month))
            return null;

        return new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
