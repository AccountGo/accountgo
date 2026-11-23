// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text;

namespace Api.DocumentCaptures.Matching;

/// <summary>
/// Deterministic party-name normalizer (rule M-01): NFKD accent removal (preserving non-Latin combining marks such as
/// Thai vowels/tones), invariant lower-casing, punctuation-to-space collapsing, and repeated legal-suffix stripping
/// with a core-empty guard.
/// </summary>
public static class PartyNameNormalizer
{
    public const int MaxNormalizedLength = 200;

    /// <summary>Single-token legal suffixes stripped repeatedly from the end of a normalized party name.</summary>
    public static readonly IReadOnlySet<string> SingleTokenLegalSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ltd",
        "limited",
        "inc",
        "incorporated",
        "llc",
        "llp",
        "corp",
        "corporation",
        "co",
        "company",
        "pte",
        "gmbh",
        "plc",
        "sa",
        "bv",
        "pty",
    };

    /// <summary>Multi-token legal suffixes stripped repeatedly from the end of a normalized party name.</summary>
    public static readonly IReadOnlyList<string[]> MultiTokenLegalSuffixes =
    [
        ["sdn", "bhd"],
        ["l", "l", "c"],
        ["s", "a"],
        ["b", "v"],
    ];

    /// <summary>Thai legal prefix and suffix tokens stripped from normalized Thai company names.</summary>
    public const string ThaiCompanyPrefix = "บริษัท";
    public static readonly IReadOnlyList<string> ThaiCompanySuffixes =
    [
        "มหาชน",
        "จำกัด",
    ];

    /// <summary>
    /// Case, accent, punctuation, and whitespace folding WITHOUT legal-suffix stripping.
    /// Used as the exact lookup and storage key for <c>PartyAliases.NormalizedAlias</c> (M-04) so suffix variants
    /// such as "Northwind Office Ltd" vs "Northwind Office Limited" can be learned while "Northwind Office Inc"
    /// never collides with a learned "Ltd" alias.
    /// </summary>
    public static string NormalizeForAlias(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var folded = FoldUnicodeAndPunctuation(raw);
        if (string.IsNullOrEmpty(folded))
            return string.Empty;

        return folded.Length <= MaxNormalizedLength
            ? folded
            : folded[..MaxNormalizedLength].TrimEnd();
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var folded = FoldUnicodeAndPunctuation(raw);
        if (string.IsNullOrEmpty(folded))
            return string.Empty;

        var stripped = StripLegalAffixes(folded);
        // Core-empty guard: if stripping removes everything, keep the un-stripped normalized form.
        var finalValue = string.IsNullOrEmpty(stripped) ? folded : stripped;

        return finalValue.Length <= MaxNormalizedLength
            ? finalValue
            : finalValue[..MaxNormalizedLength].TrimEnd();
    }

    private static string FoldUnicodeAndPunctuation(string raw)
    {
        var decomposed = raw.Normalize(NormalizationForm.FormKD);
        var withoutLatinMarks = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (IsLatinDiacriticMark(ch))
                continue;
            withoutLatinMarks.Append(ch);
        }

        var recomposed = withoutLatinMarks.ToString()
            .Replace("\u0E4D\u0E32", "\u0E33", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(recomposed.Length);
        var prevSpace = true;

        foreach (var ch in recomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (char.IsLetterOrDigit(ch) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
            else if (!prevSpace)
            {
                sb.Append(' ');
                prevSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static bool IsLatinDiacriticMark(char ch) =>
        ch is (>= '\u0300' and <= '\u036F')
            or (>= '\u1AB0' and <= '\u1AFF')
            or (>= '\u1DC0' and <= '\u1DFF')
            or (>= '\u20D0' and <= '\u20FF')
            or (>= '\uFE20' and <= '\uFE2F');

    private static string StripLegalAffixes(string folded)
    {
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
            return string.Empty;

        var changed = true;
        while (changed && tokens.Count > 0)
        {
            changed = false;

            // Strip leading Thai prefix token ("บริษัท") or attached prefix ("บริษัทสยาม")
            if (tokens[0] == ThaiCompanyPrefix)
            {
                tokens.RemoveAt(0);
                changed = true;
                continue;
            }
            if (tokens[0].StartsWith(ThaiCompanyPrefix, StringComparison.Ordinal) && tokens[0].Length > ThaiCompanyPrefix.Length)
            {
                tokens[0] = tokens[0][ThaiCompanyPrefix.Length..];
                changed = true;
                continue;
            }

            // Strip multi-token trailing suffixes (e.g. "sdn bhd", "l l c")
            foreach (var seq in MultiTokenLegalSuffixes)
            {
                if (EndsWithSequence(tokens, seq))
                {
                    tokens.RemoveRange(tokens.Count - seq.Length, seq.Length);
                    changed = true;
                    break;
                }
            }
            if (changed)
                continue;

            // Strip single-token trailing suffixes
            var last = tokens[^1];
            if (SingleTokenLegalSuffixes.Contains(last))
            {
                tokens.RemoveAt(tokens.Count - 1);
                changed = true;
                continue;
            }

            // Strip Thai trailing suffixes (standalone or attached, e.g. "สยามจำกัด")
            foreach (var thaiSuffix in ThaiCompanySuffixes)
            {
                if (last == thaiSuffix)
                {
                    tokens.RemoveAt(tokens.Count - 1);
                    changed = true;
                    break;
                }
                if (last.EndsWith(thaiSuffix, StringComparison.Ordinal) && last.Length > thaiSuffix.Length)
                {
                    tokens[^1] = last[..^thaiSuffix.Length];
                    changed = true;
                    break;
                }
            }
        }

        return string.Join(' ', tokens);
    }

    private static bool EndsWithSequence(List<string> tokens, string[] sequence)
    {
        if (tokens.Count < sequence.Length)
            return false;
        var offset = tokens.Count - sequence.Length;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (!string.Equals(tokens[offset + i], sequence[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
