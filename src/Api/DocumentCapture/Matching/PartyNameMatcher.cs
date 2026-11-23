// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Api.DocumentCaptures.Matching;

/// <summary>
/// Deterministic party-name similarity scorer (rule M-02): combines symmetric fuzzy token-set similarity (weight 0.60)
/// with Jaro-Winkler character similarity over raw and token-sorted forms (weight 0.40) on normalized names.
/// </summary>
public static class PartyNameMatcher
{
    public const double MatchThreshold = 0.82;
    public const double TokenFuzzyFloor = 0.88;
    public const double TokenWeight = 0.60;
    public const double CharWeight = 0.40;
    public const int MaxCandidates = 3;

    public static double Score(string? rawA, string? rawB) =>
        ScoreNormalized(PartyNameNormalizer.Normalize(rawA), PartyNameNormalizer.Normalize(rawB));

    public static double ScoreNormalized(string normalizedA, string normalizedB)
    {
        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB))
            return 0.0;

        if (string.Equals(normalizedA, normalizedB, StringComparison.Ordinal))
            return 1.0;

        var tokensA = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        var tokensB = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (tokensA.Length == 0 || tokensB.Length == 0)
            return 0.0;

        var tokenSim = TokenSetSimilarity(tokensA, tokensB);

        var sortedA = string.Join(' ', tokensA.OrderBy(t => t, StringComparer.Ordinal));
        var sortedB = string.Join(' ', tokensB.OrderBy(t => t, StringComparer.Ordinal));
        var charSim = Math.Max(JaroWinkler(normalizedA, normalizedB), JaroWinkler(sortedA, sortedB));

        var combined = TokenWeight * tokenSim + CharWeight * charSim;
        return Math.Round(Math.Clamp(combined, 0.0, 1.0), 4);
    }

    private static double TokenSetSimilarity(string[] tokensA, string[] tokensB)
    {
        double sumA = 0.0;
        foreach (var a in tokensA)
        {
            var best = 0.0;
            foreach (var b in tokensB)
            {
                var sim = string.Equals(a, b, StringComparison.Ordinal) ? 1.0 : JaroWinkler(a, b);
                if (sim > best)
                    best = sim;
            }
            if (best >= TokenFuzzyFloor)
                sumA += best;
        }

        double sumB = 0.0;
        foreach (var b in tokensB)
        {
            var best = 0.0;
            foreach (var a in tokensA)
            {
                var sim = string.Equals(a, b, StringComparison.Ordinal) ? 1.0 : JaroWinkler(a, b);
                if (sim > best)
                    best = sim;
            }
            if (best >= TokenFuzzyFloor)
                sumB += best;
        }

        return (sumA + sumB) / (tokensA.Length + tokensB.Length);
    }

    internal static double JaroWinkler(string s1, string s2)
    {
        if (string.Equals(s1, s2, StringComparison.Ordinal))
            return 1.0;

        var len1 = s1.Length;
        var len2 = s2.Length;
        if (len1 == 0 || len2 == 0)
            return 0.0;

        var matchDistance = Math.Max(0, Math.Max(len1, len2) / 2 - 1);
        var s1Matches = new bool[len1];
        var s2Matches = new bool[len2];

        var matches = 0;
        for (var i = 0; i < len1; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, len2);
            for (var j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j])
                    continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
            return 0.0;

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < len1; i++)
        {
            if (!s1Matches[i])
                continue;
            while (!s2Matches[k])
                k++;
            if (s1[i] != s2[k])
                transpositions++;
            k++;
        }

        var m = (double)matches;
        var jaro = (m / len1 + m / len2 + (m - transpositions / 2.0) / m) / 3.0;

        var prefix = 0;
        var maxPrefix = Math.Min(4, Math.Min(len1, len2));
        for (var i = 0; i < maxPrefix && s1[i] == s2[i]; i++)
            prefix++;

        return jaro + prefix * 0.1 * (1.0 - jaro);
    }
}
