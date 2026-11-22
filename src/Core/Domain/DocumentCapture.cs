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

public sealed class DocumentCaptureLink : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int CaptureId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string LinkedBy { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
    public DocumentCapture Capture { get; set; } = null!;
}
