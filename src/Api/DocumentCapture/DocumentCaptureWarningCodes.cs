// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Api.DocumentCaptures;

public static class DocumentCaptureWarningCodes
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "LOW_CONFIDENCE",
        "MULTIPLE_DOCUMENTS",
        "UNSUPPORTED_DOCUMENT_CLASS",
        "AMBIGUOUS_DATE",
        "AMBIGUOUS_CURRENCY",
        "FOREIGN_CURRENCY",
        "PRICE_BASIS_UNKNOWN",
        "PAYMENT_STATUS_UNKNOWN",
        "OCR_PARTIAL_PAGES",
        "TOTALS_INCONSISTENT",
        "TOTAL_UNLABELED",
        "PARSE_TRUNCATED",
        "REVIEW_REQUIRED",
        "AI_VALUE_NOT_IN_SOURCE",
        "AI_TOTALS_INCONSISTENT",
        "AI_DISAGREES_WITH_RULES",
        "AI_UNAVAILABLE"
    };

    public static string Sanitize(string? warning) => warning is not null && Known.Contains(warning) ? warning : "REVIEW_REQUIRED";
}
