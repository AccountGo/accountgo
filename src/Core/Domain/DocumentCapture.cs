// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Core.Domain;

[PublicIdPrefix("cap")]
public sealed class DocumentCapture : BaseEntity, ICompanyOwned, IHasPublicId
{
    public int CompanyId { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string NormalizationVersion { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long ByteCount { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int PageCount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public int ExtractionGeneration { get; set; }
    public DateTime? ExtractingStartedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public string? DetectedKind { get; set; }
    public int? DetectedDocumentCount { get; set; }
    public string? WarningsJson { get; set; }
    public string? FieldsJson { get; set; }
    public string? AcceptedFieldsJson { get; set; }
    public string? RawText { get; set; }
    /// <summary>The distinct pages whose text lines are kept in <see cref="RawText"/> (0 when there is none, or for rows written before this column existed).</summary>
    public int TextPageCount { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderVersion { get; set; }
    public string? ErrorCode { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? AcceptedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? BytesDeletedAt { get; set; }
    public DateTime? FieldsWipedAt { get; set; }
    public string? FileName { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string? Notes { get; set; }
    public DateTime? FiledAt { get; set; }
    public string? FiledBy { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? ArchivedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public DocumentCaptureBlob? Blob { get; set; }
    public ICollection<DocumentCaptureAttempt> Attempts { get; set; } = new List<DocumentCaptureAttempt>();
    public ICollection<DocumentCaptureLink> Links { get; set; } = new List<DocumentCaptureLink>();
}

public sealed class DocumentCaptureBlob
{
    public int CaptureId { get; set; }
    public int CompanyId { get; set; }
    public byte[] DocumentBytes { get; set; } = Array.Empty<byte>();
    public DocumentCapture Capture { get; set; } = null!;
}

public sealed class DocumentCaptureAttempt : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int CaptureId { get; set; }
    public int Generation { get; set; }
    public DateTime ReservedAt { get; set; }
    public DocumentCapture Capture { get; set; } = null!;
}

/// <summary>
/// One background field-mapping job for one extraction of a capture. Metadata and the mapped fields only: never document text
/// beyond the fields the user already reviews. The unique (company, capture, generation) key makes enqueueing idempotent.
/// </summary>
public sealed class DocumentCaptureAiMapping : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int CaptureId { get; set; }
    public int ExtractionGeneration { get; set; }
    /// <summary>queued, running, done, failed or skipped.</summary>
    public string Status { get; set; } = "queued";
    public int Attempts { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string RulesVersion { get; set; } = string.Empty;
    public string? MapperName { get; set; }
    public string? MapperVersion { get; set; }
    public int? LatencyMs { get; set; }
    public string? Outcome { get; set; }
    /// <summary>The rules' value per mapped field path, so the review step can show "rules found ...". Bounded.</summary>
    public string? RuleHintsJson { get; set; }
    /// <summary>The stored (guarded and masked) mapped fields, kept so an identical document in the same company needs no second call. Bounded.</summary>
    public string? MappedFieldsJson { get; set; }
    /// <summary>Pages reserved against the host's usage meter for this job; 0 when none. Makes a retried job unable to reserve twice and a release idempotent.</summary>
    public int ReservedPages { get; set; }
    /// <summary>When (UTC) <see cref="ReservedPages"/> was reserved, so a release goes back to the period the reservation was made in. Set and cleared together with the pages; null when none are reserved (or on a row from before this column existed).</summary>
    public DateTime? ReservedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DocumentCapture Capture { get; set; } = null!;
}

/// <summary>
/// A workspace owner's consent to send the text of scanned documents to the field mapper. One row per workspace; a revoke keeps the row
/// as evidence and a later grant replaces the consent. <see cref="ConsentVersion"/> is the notice wording the owner agreed to, so a
/// changed wording asks again.
/// </summary>
public sealed class CaptureAiMappingConsent : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public DateTime ConsentedAt { get; set; }
    public string ConsentedByUserId { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByUserId { get; set; }
}

public static class DocumentCaptureLinkRoles
{
    public const string Source = "source";
    public const string Attachment = "attachment";
}

public sealed class DocumentCaptureLink : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int CaptureId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string Role { get; set; } = DocumentCaptureLinkRoles.Source;
    public string LinkedBy { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
    /// <summary>How the document's tax was treated on the linked bill or invoice. Derived by the server, never client-supplied. Null when not derived.</summary>
    public string? TaxTreatment { get; set; }
    public DocumentCapture Capture { get; set; } = null!;
}

/// <summary>
/// One admitted stored-document upload attempt, reserved before any decode work. Independent of the document rows, so deleting
/// documents or failing validation never frees a slot in the per-workspace hourly upload limit. Cleared by the retention worker.
/// </summary>
public sealed class DocumentUploadAdmission : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public DateTime ReservedAt { get; set; }
}

/// <summary>
/// One "Download all documents" run (design 3.9 S2/S3, 3.10 T1/T2). The selection (ids and stored hashes) and the document
/// revision are fixed under the company lock when the run is created; the ZIP is then streamed once. <see cref="Status"/>:
/// <c>ready</c> (selected, not yet downloaded), <c>running</c> (streaming), <c>server_finished</c> (every selected file written
/// and verified, index and archive finalised, response completed without abort) or <c>failed</c> (with <see cref="FailureReason"/>).
/// "server_finished" never claims the browser saved the file.
/// </summary>
[PublicIdPrefix("dex")]
public sealed class DocumentExportRun : BaseEntity, ICompanyOwned, IHasPublicId
{
    public int CompanyId { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string Status { get; set; } = "ready";
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StreamStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long SelectedRevision { get; set; }
    public int SelectedCount { get; set; }
    public long SelectedBytes { get; set; }
    public int WrittenCount { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>JSON array of [captureId, storedSha256] fixed at selection.</summary>
    public string InventoryJson { get; set; } = "[]";
}

public sealed class DocumentLibraryRevision : ICompanyOwned
{
    public int CompanyId { get; set; }
    public long Revision { get; set; }
}
