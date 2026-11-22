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
        "REVIEW_REQUIRED"
    };

    public static string Sanitize(string? warning) => warning is not null && Known.Contains(warning) ? warning : "REVIEW_REQUIRED";
}
