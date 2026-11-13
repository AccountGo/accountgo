// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Services.Banking
{
    /// <summary>Shared normalization for expense and owner movement suggestions.</summary>
    public static class StatementDescriptionKey
    {
        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var lower = value.Trim().ToLowerInvariant();
            lower = Regex.Replace(lower, @"\b\d{4}-\d{2}-\d{2}\b", " ");
            lower = Regex.Replace(lower, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", " ");
            lower = Regex.Replace(lower, @"\b(?:ref|reference|inv|invoice|txn|trx|id)[:#-]?\s*\S+\b", " ");
            lower = Regex.Replace(lower, @"\d+", " ");
            lower = Regex.Replace(lower, @"[^a-z\s]+", " ");
            var normalized = string.Join(' ', lower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0)
                return null;
            return normalized.Length <= 200 ? normalized : normalized[..200];
        }

    }
}
