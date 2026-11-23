// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

internal static class DateAndAmountParser
{
    private const RegexOptions Linear = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private const RegexOptions LinearI = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

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

    // All patterns use NonBacktracking; token boundaries are verified explicitly in C# to stay strictly linear.
    private static readonly Regex IsoDateRegex = new(@"(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})", Linear);
    private static readonly Regex ThaiMonthDateRegex = new(
        @"(\d{1,2})\s+(มกราคม|กุมภาพันธ์|มีนาคม|เมษายน|พฤษภาคม|มิถุนายน|กรกฎาคม|สิงหาคม|กันยายน|ตุลาคม|พฤศจิกายน|ธันวาคม)\s+(\d{4})",
        Linear);
    private static readonly Regex DayMonthYearEnRegex = new(@"(\d{1,2})\s+([A-Za-z]{3})[A-Za-z]*\.?,?\s+(\d{2,4})", Linear);
    private static readonly Regex MonthDayYearEnRegex = new(@"([A-Za-z]{3})[A-Za-z]*\.?\s+(\d{1,2}),?\s+(\d{4})", Linear);
    private static readonly Regex NumericDateRegex = new(@"(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})", Linear);

    private static readonly Regex AmountTokenRegex = new(
        @"(\d{1,3}(?:[,\s]\d{3})+|\d{1,3}(?:\.\d{3})+|\d+)([.,])(\d{2})",
        Linear);

    private static readonly Regex PhoneContextRegex = new(
        @"\b(?:tel|telephone|phone|fax|mobile|cell|call|hotline|โทร)\b",
        LinearI);

    // Currency codes/symbols must sit adjacent to an amount on the same line (K8).
    private static readonly Regex IsoCurrencyBeforeAmountRegex = new(
        @"\b(USD|EUR|GBP|THB|SGD|MYR|IDR|AUD|CAD|JPY|CHF|NZD|HKD|PHP|VND)\b\s*[$€£฿]?\s*\d",
        Linear);

    private static readonly Regex IsoCurrencyAfterAmountRegex = new(
        @"\d[.,]\d{2}\s*(USD|EUR|GBP|THB|SGD|MYR|IDR|AUD|CAD|JPY|CHF|NZD|HKD|PHP|VND)\b",
        Linear);

    private static readonly Regex SymbolBeforeAmountRegex = new(
        @"(S\$|A\$|C\$|HK\$|NZ\$|\bRM\b|\bRp\b|[€£฿])\s*\d",
        Linear);

    private static readonly Regex SymbolAfterAmountRegex = new(
        @"\d[.,]\d{2}\s*(S\$|A\$|C\$|HK\$|NZ\$|\bRM\b|\bRp\b|[€£฿])",
        Linear);

    private static readonly Regex BareDollarBeforeAmountRegex = new(@"\$\s*\d", Linear);
    private static readonly Regex BareDollarAfterAmountRegex = new(@"\d[.,]\d{2}\s*\$", Linear);

    public static bool ContainsDigit(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
                return true;
        }
        return false;
    }

    public static string SanitizeLineText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        var prevSpace = true;
        foreach (var ch in raw)
        {
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

        foreach (Match m in IsoDateRegex.Matches(text))
        {
            if (!HasDateTokenBoundaries(text, m.Index, m.Length)) continue;
            var iso = TryMakeIsoDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            if (iso is not null) return iso;
        }

        foreach (Match m in ThaiMonthDateRegex.Matches(text))
        {
            if (!HasDigitBoundaries(text, m.Index, m.Length)) continue;
            if (ThaiMonths.TryGetValue(m.Groups[2].Value, out var month))
            {
                var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[1].Value);
                if (iso is not null) return iso;
            }
        }

        foreach (Match m in DayMonthYearEnRegex.Matches(text))
        {
            if (!HasDigitBoundaries(text, m.Index, m.Length)) continue;
            if (EnglishMonths.TryGetValue(m.Groups[2].Value, out var month))
            {
                var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[1].Value);
                if (iso is not null) return iso;
            }
        }

        foreach (Match m in MonthDayYearEnRegex.Matches(text))
        {
            if (m.Index > 0 && char.IsAsciiLetter(text[m.Index - 1])) continue;
            if (m.Index + m.Length < text.Length && char.IsAsciiDigit(text[m.Index + m.Length])) continue;
            if (EnglishMonths.TryGetValue(m.Groups[1].Value, out var month))
            {
                var iso = TryMakeIsoDate(m.Groups[3].Value, month, m.Groups[2].Value);
                if (iso is not null) return iso;
            }
        }

        foreach (Match m in NumericDateRegex.Matches(text))
        {
            if (!HasDateTokenBoundaries(text, m.Index, m.Length)) continue;
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

        return null;
    }

    public static IReadOnlyList<decimal> ExtractAmounts(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || PhoneContextRegex.IsMatch(line))
            return Array.Empty<decimal>();

        var results = new List<decimal>();
        foreach (Match m in AmountTokenRegex.Matches(line))
        {
            if (!HasAmountBoundaries(line, m.Index, m.Length))
                continue;

            var wholeRaw = m.Groups[1].Value;
            var sep = m.Groups[2].Value[0];
            var frac = m.Groups[3].Value;

            if (sep == ',')
            {
                if (wholeRaw.Contains(',', StringComparison.Ordinal))
                    continue;
            }
            else
            {
                if (wholeRaw.Contains('.', StringComparison.Ordinal))
                    continue;
            }

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

        return results;
    }

    public static (string? Currency, bool AmbiguousDollar) DetectCurrency(IReadOnlyList<string> lines)
    {
        // Pass 1: explicit ISO code adjacent to an amount on the same line
        foreach (var line in lines)
        {
            var m = IsoCurrencyBeforeAmountRegex.Match(line);
            if (m.Success) return (m.Groups[1].Value, false);
            var m2 = IsoCurrencyAfterAmountRegex.Match(line);
            if (m2.Success) return (m2.Groups[1].Value, false);
        }

        // Pass 2: unambiguous symbol adjacent to an amount on the same line
        foreach (var line in lines)
        {
            var m = SymbolBeforeAmountRegex.Match(line);
            if (!m.Success) m = SymbolAfterAmountRegex.Match(line);
            if (m.Success)
            {
                var mapped = m.Groups[1].Value switch
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
        }

        // Pass 3: bare '$' adjacent to an amount on the same line -> omit currency and warn AMBIGUOUS_CURRENCY
        foreach (var line in lines)
        {
            if (BareDollarBeforeAmountRegex.IsMatch(line) || BareDollarAfterAmountRegex.IsMatch(line))
                return (null, true);
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

    private static bool HasDigitBoundaries(string text, int index, int length)
    {
        if (index > 0 && char.IsAsciiDigit(text[index - 1])) return false;
        if (index + length < text.Length && char.IsAsciiDigit(text[index + length])) return false;
        return true;
    }

    private static bool HasDateTokenBoundaries(string text, int index, int length)
    {
        if (index > 0 && IsDateConnectorOrDigit(text[index - 1])) return false;
        if (index + length < text.Length && IsDateConnectorOrDigit(text[index + length])) return false;
        return true;
    }

    private static bool HasAmountBoundaries(string text, int index, int length)
    {
        if (index > 0 && IsAmountConnectorOrDigit(text[index - 1])) return false;
        if (index + length < text.Length && IsAmountConnectorOrDigit(text[index + length])) return false;
        return true;
    }

    private static bool IsDateConnectorOrDigit(char ch) =>
        char.IsAsciiDigit(ch) || ch is '-' or '/' or '.';

    private static bool IsAmountConnectorOrDigit(char ch) =>
        char.IsAsciiDigit(ch) || ch is '.' or ',' or '-' or '/';

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
