// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Entitlements;

namespace Api.DocumentCaptures;

/// <summary>
/// The documents library (stored files and record attachments). Independent of <see cref="DocumentCaptureOptions"/>: the library
/// stores files without extraction, so it works on hosts that run no OCR.
/// </summary>
public sealed class DocumentsOptions
{
    public const string SectionName = "Documents";

    /// <summary>Library upload, list, metadata, link and trash endpoints. Off returns <c>DOCUMENTS_DISABLED</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Stored-document uploads per workspace per rolling hour.</summary>
    public int MaxUploadsPerHour { get; set; } = 120;

    /// <summary>Days a trashed document stays restorable before the retention worker purges it.</summary>
    public int TrashRetentionDays { get; set; } = 30;

    public static void Validate(DocumentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxUploadsPerHour is < 1 or > 2_000 || options.TrashRetentionDays is < 1 or > 365)
            throw new InvalidOperationException("Documents options are outside the supported range.");
    }
}

/// <summary>Which per-file limits an upload gets. Decided by the server from the shape of the authorised request.</summary>
public enum DocumentUploadProfile
{
    /// <summary>Uploaded while attaching to a bill or invoice.</summary>
    Record,
    /// <summary>Uploaded in the library: no link, or a customer/vendor link.</summary>
    Library
}

public sealed record DocumentUploadLimit(int MaxBytes, int MaxPdfPages);

public static class DocumentUploadLimits
{
    /// <summary>The largest body any stored-document upload may send (largest file plus multipart overhead).</summary>
    public const int MaxRequestBodyBytes = DocumentStoragePolicy.StoredMaxFileBytes + 256 * 1024;

    public static DocumentUploadProfile ProfileFor(string? linkTargetType) => linkTargetType switch
    {
        "bill" or "invoice" => DocumentUploadProfile.Record,
        _ => DocumentUploadProfile.Library
    };

    public static DocumentUploadLimit Resolve(DocumentUploadProfile profile, DocumentStoragePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var safe = policy.Clamp();
        return profile == DocumentUploadProfile.Record
            ? new DocumentUploadLimit(safe.RecordMaxFileBytes, safe.RecordMaxPdfPages)
            : new DocumentUploadLimit(safe.LibraryMaxFileBytes, safe.LibraryMaxPdfPages);
    }
}
