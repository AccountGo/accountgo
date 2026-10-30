// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.Banking
{
    /// <summary>Stable key for matching saved maps and presets to a CSV header row (#447 Phase B).</summary>
    public static class StatementHeaderSignature
    {
        public static string FromHeaders(IEnumerable<string?> headers)
        {
            var tokens = headers
                .Select(h => StatementColumnDetector.NormalizeHeaderToken(h ?? string.Empty))
                .Where(t => t.Length > 0)
                .ToList();
            return string.Join('|', tokens);
        }

        public static string FromRecord(IReadOnlyList<string> headerRow) => FromHeaders(headerRow);
    }
}
