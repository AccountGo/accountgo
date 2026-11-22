// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

internal static class BankFieldParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    // ISO 13616 country IBAN lengths
    private static readonly Dictionary<string, int> IbanLengths = new(StringComparer.Ordinal)
    {
        ["AD"] = 24, ["AE"] = 23, ["AL"] = 28, ["AT"] = 20, ["AZ"] = 28, ["BA"] = 20, ["BE"] = 16,
        ["BG"] = 22, ["BH"] = 22, ["BR"] = 29, ["BY"] = 28, ["CH"] = 21, ["CR"] = 22, ["CY"] = 28,
        ["CZ"] = 24, ["DE"] = 22, ["DK"] = 18, ["DO"] = 28, ["EE"] = 20, ["EG"] = 29, ["ES"] = 24,
        ["FI"] = 18, ["FO"] = 18, ["FR"] = 27, ["GB"] = 22, ["GE"] = 22, ["GI"] = 23, ["GL"] = 18,
        ["GR"] = 27, ["GT"] = 28, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IL"] = 23, ["IQ"] = 23,
        ["IS"] = 26, ["IT"] = 27, ["JO"] = 30, ["KW"] = 30, ["KZ"] = 20, ["LB"] = 28, ["LC"] = 32,
        ["LI"] = 21, ["LT"] = 20, ["LU"] = 20, ["LV"] = 21, ["LY"] = 25, ["MC"] = 27, ["MD"] = 24,
        ["ME"] = 22, ["MK"] = 19, ["MR"] = 27, ["MT"] = 31, ["MU"] = 30, ["NL"] = 18, ["NO"] = 15,
        ["PK"] = 24, ["PL"] = 28, ["PS"] = 29, ["PT"] = 25, ["QA"] = 29, ["RO"] = 24, ["RS"] = 22,
        ["SA"] = 24, ["SC"] = 31, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["ST"] = 25,
        ["SV"] = 28, ["TL"] = 23, ["TN"] = 24, ["TR"] = 26, ["UA"] = 29, ["VA"] = 22, ["VG"] = 24,
        ["XK"] = 20,
    };

    private static readonly Regex SwiftFormatRegex = new(
        @"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex IbanLabelRegex = new(
        @"\bIBAN\b\s*[:#-]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex SwiftLabelRegex = new(
        @"\b(?:SWIFT(?:\s*/\s*BIC)?|BIC(?:\s*/\s*SWIFT)?|SWIFT\s+CODE|BIC\s+CODE)\b\s*[:#-]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex RoutingLabelRegex = new(
        @"\b(?:ROUTING(?:\s+(?:NO\.?|NUMBER|#))?|ABA(?:\s+ROUTING)?(?:\s+(?:NO\.?|NUMBER|#))?|RTN)\b\s*[:#-]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AccountNumberLabelRegex = new(
        @"\b(?:ACCOUNT\s*(?:NO\.?|NUMBER|#)|ACCT\.?\s*(?:NO\.?|NUMBER|#))\b\s*[:#-]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex BankNameLabelRegex = new(
        @"\b(?:BANK\s+NAME|FINANCIAL\s+INSTITUTION|INSTITUTION|BANK)\b\s*[:#-]\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AccountHolderLabelRegex = new(
        @"\b(?:ACCOUNT\s+HOLDER|ACCOUNT\s+NAME|HOLDER\s+NAME|BENEFICIARY(?:\s+NAME)?)\b\s*[:#-]\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    public static Dictionary<string, object?> Parse(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (lines.Count == 0)
            return result;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var next = i + 1 < lines.Count ? lines[i + 1] : null;

            if (!result.ContainsKey("iban"))
            {
                var raw = MatchLabeledValue(IbanLabelRegex, line, next);
                if (raw is not null && TryNormalizeValidIban(raw, out var iban))
                    result["iban"] = iban;
            }

            if (!result.ContainsKey("swift"))
            {
                var raw = MatchLabeledValue(SwiftLabelRegex, line, next);
                if (raw is not null && TryNormalizeValidSwift(raw, out var swift))
                    result["swift"] = swift;
            }

            if (!result.ContainsKey("routingNumber"))
            {
                var raw = MatchLabeledValue(RoutingLabelRegex, line, next);
                if (raw is not null && TryNormalizeValidAbaRouting(raw, out var routing))
                    result["routingNumber"] = routing;
            }

            if (!result.ContainsKey("accountNumber"))
            {
                var raw = MatchLabeledValue(AccountNumberLabelRegex, line, next);
                if (raw is not null && TryNormalizeAccountNumber(raw, out var accountNo))
                    result["accountNumber"] = accountNo;
            }

            if (!result.ContainsKey("bankName"))
            {
                var raw = MatchLabeledValue(BankNameLabelRegex, line, next);
                if (!string.IsNullOrWhiteSpace(raw))
                    result["bankName"] = Truncate(raw, DocumentCaptureFieldSchemas.MaxTextLength);
            }

            if (!result.ContainsKey("name"))
            {
                var raw = MatchLabeledValue(AccountHolderLabelRegex, line, next);
                if (!string.IsNullOrWhiteSpace(raw))
                    result["name"] = Truncate(raw, DocumentCaptureFieldSchemas.MaxTextLength);
            }
        }

        return result;
    }

    public static bool TryNormalizeValidIban(string raw, out string iban)
    {
        iban = string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is ' ' or '-' or '\t')
                continue;
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
            else
                break;
        }

        var candidate = sb.ToString();
        if (candidate.Length < 15 || candidate.Length > 34)
            return false;

        var country = candidate[..2];
        if (!IbanLengths.TryGetValue(country, out var expectedLength) || candidate.Length != expectedLength)
            return false;

        if (!char.IsAsciiDigit(candidate[2]) || !char.IsAsciiDigit(candidate[3]))
            return false;

        // Rearrange: move first 4 chars to end, convert A=10..Z=35, compute mod 97 == 1
        var rearranged = candidate[4..] + candidate[..4];
        var remainder = 0;
        foreach (var ch in rearranged)
        {
            if (ch is >= '0' and <= '9')
            {
                remainder = (remainder * 10 + (ch - '0')) % 97;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                var val = ch - 'A' + 10;
                remainder = (remainder * 100 + val) % 97;
            }
            else
            {
                return false;
            }
        }

        if (remainder != 1)
            return false;

        iban = candidate;
        return true;
    }

    public static bool TryNormalizeValidAbaRouting(string raw, out string routing)
    {
        routing = string.Empty;
        var digits = new StringBuilder(9);
        foreach (var ch in raw)
        {
            if (ch is ' ' or '-')
                continue;
            if (ch is >= '0' and <= '9')
                digits.Append(ch);
            else
                break;
        }

        if (digits.Length != 9)
            return false;

        var d = new int[9];
        for (var i = 0; i < 9; i++)
            d[i] = digits[i] - '0';

        // Standard ABA routing number checksum
        var checksum = 3 * (d[0] + d[3] + d[6]) + 7 * (d[1] + d[4] + d[7]) + (d[2] + d[5] + d[8]);
        if (checksum % 10 != 0)
            return false;

        routing = digits.ToString();
        return true;
    }

    private static bool TryNormalizeValidSwift(string raw, out string swift)
    {
        swift = string.Empty;
        var token = raw.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var upper = token.Trim().ToUpperInvariant();
        if (upper.Length > 32)
            return false;

        try
        {
            if (!SwiftFormatRegex.IsMatch(upper))
                return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        swift = upper;
        return true;
    }

    private static bool TryNormalizeAccountNumber(string raw, out string accountNumber)
    {
        accountNumber = string.Empty;
        var sb = new StringBuilder(raw.Length);
        var digitCount = 0;
        foreach (var ch in raw.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or ' ')
            {
                if (ch is >= '0' and <= '9')
                    digitCount++;
                sb.Append(ch);
            }
            else
            {
                break;
            }
        }

        var trimmed = sb.ToString().Trim();
        if (digitCount < 4 || trimmed.Length > 64)
            return false;

        accountNumber = trimmed;
        return true;
    }

    private static string? MatchLabeledValue(Regex labelRegex, string line, string? nextLine)
    {
        try
        {
            var m = labelRegex.Match(line);
            if (!m.Success)
                return null;

            var value = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(value))
                return value;

            return string.IsNullOrWhiteSpace(nextLine) ? null : nextLine.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
}
