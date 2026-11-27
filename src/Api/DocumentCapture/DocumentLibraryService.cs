// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.PublicIds;
using Api.Service;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures;

/// <param name="Locked">The record is posted, issued or void: its attachments are evidence.</param>
/// <param name="CanUnlink">The caller may remove this link. Everyone needs the record's attach (create/manage) permission;
/// on a posted record they must also hold Manage Documents (Owner and Admin do) or be the member who linked it. Scanned
/// sources never.</param>
public sealed record DocumentLinkView(string TargetType, string TargetId, string Role, DateTime LinkedAt, bool Locked, bool CanUnlink);

public sealed record DocumentView(
    string Id,
    string Kind,
    string Status,
    string Title,
    string? FileName,
    string? Category,
    DateTime? DocumentDate,
    string? Notes,
    string ContentType,
    long ByteCount,
    int PageCount,
    int? Width,
    int? Height,
    string StoredSha256,
    string UploadedSha256,
    bool StoredCopyOptimised,
    bool Archived,
    bool InTrash,
    DateTime FiledAt,
    DateTime CreatedAt,
    IReadOnlyList<DocumentLinkView> Links,
    bool CanManage,
    bool HasLockedLink,
    bool HasSourceLink);

public sealed record DocumentPage(IReadOnlyList<DocumentView> Items, int Total, int Page, int PageSize);

public sealed record DocumentMetadataInput(string? Title, string? Category, DateTime? DocumentDate, string? Notes);

public sealed record DocumentListQuery(string? Category, bool? Linked, bool Archived, bool Trash, string? Search, int Page, int PageSize);

public sealed record DocumentCapabilitiesView(
    bool Enabled,
    IReadOnlyList<string> AllowedMimeTypes,
    IReadOnlyList<string> Categories,
    DocumentUploadLimit LibraryLimit,
    DocumentUploadLimit RecordLimit,
    long UsedBytes,
    long? StorageLimitBytes,
    bool CanView,
    bool CanManage);

/// <summary>
/// The documents library over the capture tables: stored-only <c>document</c> uploads, filed scans, metadata, many-to-many links,
/// archive, trash and permanent delete. Every write that depends on a record's state takes the company admission lock, then the
/// linked records' rows in (TargetType, TargetId) order, then reads their state fresh (design 3.8 R1, 3.9 S5, 3.11 U1).
/// </summary>
public sealed class DocumentLibraryService(
    ApiDbContext db,
    IDocumentCaptureAdmissionLock admissionLock,
    IEntitlementService entitlements,
    IPublicIdResolver publicIds,
    ITenantContext tenant,
    IOptions<DocumentsOptions> options,
    IOptions<DocumentCaptureOptions> captureOptions,
    ISecurityAuditService? securityAudit = null,
    TimeProvider? timeProvider = null,
    ITransactionalSecurityAudit? transactionalAudit = null)
{
    public const string DocumentKind = "document";
    public const string StoredStatus = "stored";
    public const int MaxPageSize = 100;

    public static IReadOnlyList<string> Categories { get; } =
        ["contract", "agreement", "receipt", "invoice", "statement", "tax", "legal", "other"];

    public static IReadOnlyList<string> AllowedMimeTypes { get; } = ["application/pdf", "image/jpeg", "image/png"];

    private static readonly string[] ScanKinds = ["expense-receipt", "purchase-invoice", "sales-invoice"];
    private static readonly string[] TargetTypes = ["bill", "customer", "invoice", "vendor"];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // ---------- capabilities ----------

    /// <summary>Always answers (also when the library is switched off) so the client can hide the library: <c>Enabled</c> says which.</summary>
    public async Task<DocumentCapabilitiesView> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var policy = entitlements.GetDocumentStoragePolicy(companyId).Clamp();
        var used = await UsedBytesAsync(companyId, cancellationToken).ConfigureAwait(false);
        return new DocumentCapabilitiesView(
            options.Value.Enabled,
            AllowedMimeTypes,
            Categories,
            DocumentUploadLimits.Resolve(DocumentUploadProfile.Library, policy),
            DocumentUploadLimits.Resolve(DocumentUploadProfile.Record, policy),
            used,
            policy.StorageLimitBytes,
            Has(SystemPermissions.DocumentsView),
            Has(SystemPermissions.DocumentsManage));
    }

    // ---------- upload ----------

    public async Task<DocumentView> UploadAsync(
        byte[] source,
        string declaredContentType,
        string? fileName,
        DocumentMetadataInput metadata,
        string? linkTargetType,
        string? linkTargetPublicId,
        string? idempotencyKey,
        bool force,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var companyId = tenant.RequireCompanyId();
        var actorId = RequireActor();
        if (idempotencyKey is { Length: 0 or > 80 })
            throw new DocumentCaptureServiceException(400, "DOCUMENT_IDEMPOTENCY_KEY_INVALID");
        if (string.IsNullOrEmpty(linkTargetType) != string.IsNullOrEmpty(linkTargetPublicId))
            throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID");

        // Who may upload: into the library needs documents.manage; while attaching, the record's write permission is enough.
        int? linkTargetId = null;
        if (linkTargetType is not null)
        {
            EnsureKnownTargetType(linkTargetType);
            // Attaching needs the record's write permission and its read permission: the new file is then always readable
            // through that link, so the response after commit can never be a not-found.
            if (!Has(AttachPermission(linkTargetType)) || !Has(ReadPermission(linkTargetType)))
                throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
            linkTargetId = await ResolveTargetIdAsync(linkTargetType, linkTargetPublicId!, cancellationToken).ConfigureAwait(false);
        }
        else if (!Has(SystemPermissions.DocumentsManage) || !Has(SystemPermissions.DocumentsView))
        {
            // Into the library needs manage AND view: the new file has no link, so view is the only way to read it back.
            throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
        }

        var normalizedMetadata = NormalizeMetadata(metadata, fileName);
        var policy = entitlements.GetDocumentStoragePolicy(companyId).Clamp();
        var limit = DocumentUploadLimits.Resolve(DocumentUploadLimits.ProfileFor(linkTargetType), policy);
        if (source.Length == 0)
            throw new DocumentCaptureServiceException(400, "DOCUMENT_EMPTY");
        if (source.Length > limit.MaxBytes)
            throw new DocumentLimitException(413, "DOCUMENT_TOO_LARGE", limit);

        var sourceHash = Hash(source);
        var linkKey = linkTargetType is null ? "none" : $"{linkTargetType}:{linkTargetId}";
        var fingerprint = Hash(Encoding.UTF8.GetBytes(string.Join('\n', sourceHash, DocumentKind,
            JsonSerializer.Serialize(normalizedMetadata), linkKey, force ? "copy" : "single")));

        // Replays skip media preparation; the locked transaction rechecks. A key that belongs to a file the caller can't read
        // is ignored (treated as no key), exactly like the scan pipeline, so a guessed key reveals nothing.
        if (idempotencyKey is not null)
        {
            var prior = await KeyedDocumentAsync(companyId, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (prior is not null)
            {
                if (await CanSeeKeyedAsync(companyId, prior, cancellationToken).ConfigureAwait(false))
                    return await ReplayAsync(prior, fingerprint, cancellationToken).ConfigureAwait(false);
                idempotencyKey = null;
            }
        }

        // Every upload attempt reserves a slot before any decode work, so invalid files, failures and later deletions all count
        // against the hourly limit (design 3.2 abuse guard). Replays above never reach this point.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ReserveUploadSlotAsync(companyId, now, cancellationToken).ConfigureAwait(false);
        var media = await PrepareAsync(source, declaredContentType, limit, cancellationToken).ConfigureAwait(false);
        int captureId;
        string capturePublicId;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            if (linkTargetType is not null)
            {
                await LockTargetsAsync(companyId, [(linkTargetType, linkTargetId!.Value)], cancellationToken).ConfigureAwait(false);
                if (!await TargetExistsAsync(companyId, linkTargetType, linkTargetId!.Value, cancellationToken).ConfigureAwait(false))
                    throw NotFound();
            }

            if (idempotencyKey is not null)
            {
                var existing = await KeyedDocumentAsync(companyId, idempotencyKey, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (await CanSeeKeyedAsync(companyId, existing, cancellationToken).ConfigureAwait(false))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return await ReplayAsync(existing, fingerprint, cancellationToken).ConfigureAwait(false);
                    }
                    idempotencyKey = null;
                }
            }

            if (!force)
            {
                var duplicate = await FindReadableDuplicateAsync(companyId, sourceHash, cancellationToken).ConfigureAwait(false);
                if (duplicate is not null)
                    throw new DocumentCaptureServiceException(409, "DOCUMENT_DUPLICATE", duplicate.PublicId);
            }


            if (policy.StorageLimitBytes is { } storageLimit)
            {
                var used = await UsedBytesAsync(companyId, cancellationToken).ConfigureAwait(false);
                if (used + media.Bytes.Length > storageLimit)
                    throw new DocumentStorageLimitException(used, storageLimit, media.Bytes.Length);
            }

            var capture = new DocumentCapture
            {
                CompanyId = companyId,
                Kind = DocumentKind,
                SchemaVersion = DocumentCaptureFieldSchemas.CurrentSchemaVersion,
                Status = StoredStatus,
                SourceHash = sourceHash,
                ContentHash = Hash(media.Bytes),
                NormalizationVersion = media.NormalizationVersion,
                ContentType = media.ContentType,
                ByteCount = media.Bytes.Length,
                Width = media.Width,
                Height = media.Height,
                PageCount = media.PageCount,
                IdempotencyKey = idempotencyKey ?? $"omitted:{Guid.NewGuid():N}",
                RequestFingerprint = fingerprint,
                ExtractionGeneration = 0,
                CreatedBy = actorId,
                CreatedAt = now,
                ExpiresAt = now,
                FileName = normalizedMetadata.FileName,
                Title = normalizedMetadata.Title,
                Category = normalizedMetadata.Category,
                DocumentDate = normalizedMetadata.DocumentDate,
                Notes = normalizedMetadata.Notes,
                FiledAt = now,
                FiledBy = actorId,
                Blob = new DocumentCaptureBlob { CompanyId = companyId, DocumentBytes = media.Bytes }
            };
            if (linkTargetType is not null)
            {
                capture.Links.Add(new DocumentCaptureLink
                {
                    CompanyId = companyId,
                    TargetType = linkTargetType,
                    TargetId = linkTargetId!.Value,
                    Role = DocumentCaptureLinkRoles.Attachment,
                    LinkedBy = actorId,
                    LinkedAt = now
                });
            }
            db.DocumentCaptures.Add(capture);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            captureId = capture.Id;
            capturePublicId = capture.PublicId;
        }
        db.ChangeTracker.Clear();

        await AuditAsync("document.uploaded", companyId, capturePublicId, new
        {
            contentType = media.ContentType,
            byteCount = media.Bytes.Length,
            pageCount = media.PageCount,
            category = normalizedMetadata.Category
        }).ConfigureAwait(false);
        if (linkTargetType is not null)
            await AuditAsync("document.linked", companyId, capturePublicId, new { targetType = linkTargetType, targetId = linkTargetPublicId, role = DocumentCaptureLinkRoles.Attachment }).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    // ---------- reads ----------

    public async Task<DocumentPage> ListAsync(DocumentListQuery query, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var companyId = tenant.RequireCompanyId();
        if (!Has(SystemPermissions.DocumentsView))
            throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
        await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = LibraryVisible(companyId);
        if (query.Trash)
            rows = rows.Where(c => c.DeletedAt != null);
        else if (query.Archived)
            rows = rows.Where(c => c.DeletedAt == null && c.ArchivedAt != null);
        else
            rows = rows.Where(c => c.DeletedAt == null && c.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim().ToLowerInvariant();
            rows = rows.Where(c => c.Category == category);
        }
        if (query.Linked is { } linked)
        {
            var readableTargets = ReadableTargetTypes();
            rows = linked
                ? rows.Where(c => db.DocumentCaptureLinks.Any(l => l.CompanyId == companyId && l.CaptureId == c.Id && readableTargets.Contains(l.TargetType)))
                : rows.Where(c => !db.DocumentCaptureLinks.Any(l => l.CompanyId == companyId && l.CaptureId == c.Id && readableTargets.Contains(l.TargetType)));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            if (term.Length > 100)
                term = term[..100];
            rows = rows.Where(c => (c.Title != null && c.Title.ToLower().Contains(term))
                || (c.FileName != null && c.FileName.ToLower().Contains(term))
                || (c.Notes != null && c.Notes.ToLower().Contains(term)));
        }

        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows.OrderByDescending(c => c.FiledAt).ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var views = await ToViewsAsync(companyId, items, cancellationToken).ConfigureAwait(false);
        return new DocumentPage(views, total, page, pageSize);
    }

    public async Task<DocumentView> GetAsync(int captureId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var capture = await RequireReadableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
        return (await ToViewsAsync(companyId, [capture], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> GetBytesAsync(int captureId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var capture = await RequireReadableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
        var blob = await db.DocumentCaptureBlobs.AsNoTracking()
            .SingleOrDefaultAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_BYTES_UNAVAILABLE");
        return (blob.DocumentBytes, capture.ContentType, DownloadName(capture));
    }

    public async Task<DocumentPage> ListLinkedAsync(string targetType, string targetPublicId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var companyId = tenant.RequireCompanyId();
        EnsureKnownTargetType(targetType);
        if (!Has(ReadPermission(targetType)))
            throw NotFound();
        await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
        var targetId = await ResolveTargetIdAsync(targetType, targetPublicId, cancellationToken).ConfigureAwait(false);
        if (!await TargetExistsAsync(companyId, targetType, targetId, cancellationToken).ConfigureAwait(false))
            throw NotFound();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var allowedScanKinds = ReadableScanKinds(companyId);
        var rows = db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiledAt != null && c.BytesDeletedAt == null && c.DeletedAt == null
                && (c.Kind == DocumentKind || allowedScanKinds.Contains(c.Kind))
                && db.DocumentCaptureLinks.Any(l => l.CompanyId == companyId && l.CaptureId == c.Id
                    && l.TargetType == targetType && l.TargetId == targetId));
        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows.OrderBy(c => c.FiledAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var views = await ToViewsAsync(companyId, items, cancellationToken).ConfigureAwait(false);
        return new DocumentPage(views, total, page, pageSize);
    }

    // ---------- writes ----------

    public async Task<DocumentView> UpdateMetadataAsync(int captureId, DocumentMetadataInput input, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        RequireManage();
        var companyId = tenant.RequireCompanyId();
        var actorId = RequireActor();
        object? before;
        object? after;
        string publicId;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await LibraryRowForWriteAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            var normalized = NormalizeMetadata(input with { Title = input.Title ?? capture.Title }, capture.FileName);
            before = new { capture.Title, capture.Category, documentDate = capture.DocumentDate, capture.Notes };
            after = new { normalized.Title, normalized.Category, documentDate = normalized.DocumentDate, normalized.Notes };
            publicId = capture.PublicId;
            await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Title, normalized.Title)
                    .SetProperty(c => c.Category, normalized.Category)
                    .SetProperty(c => c.DocumentDate, normalized.DocumentDate)
                    .SetProperty(c => c.Notes, normalized.Notes), cancellationToken).ConfigureAwait(false);
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync("document.updated", companyId, publicId, new { before, after, actorId }).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentView> LinkAsync(int captureId, string targetType, string targetPublicId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var companyId = tenant.RequireCompanyId();
        var actorId = RequireActor();
        EnsureKnownTargetType(targetType);
        if (!Has(AttachPermission(targetType)) || !Has(ReadPermission(targetType)))
            throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
        var targetId = await ResolveTargetIdAsync(targetType, targetPublicId, cancellationToken).ConfigureAwait(false);
        string publicId;
        var created = false;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await RequireReadableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            if (capture.DeletedAt is not null)
                throw new DocumentCaptureServiceException(409, "DOCUMENT_IN_TRASH");
            publicId = capture.PublicId;
            await LockTargetsAsync(companyId, [(targetType, targetId)], cancellationToken).ConfigureAwait(false);
            if (!await TargetExistsAsync(companyId, targetType, targetId, cancellationToken).ConfigureAwait(false))
                throw NotFound();
            var exists = await db.DocumentCaptureLinks.AsNoTracking().AnyAsync(l => l.CompanyId == companyId && l.CaptureId == captureId
                && l.TargetType == targetType && l.TargetId == targetId, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                db.DocumentCaptureLinks.Add(new DocumentCaptureLink
                {
                    CompanyId = companyId,
                    CaptureId = captureId,
                    TargetType = targetType,
                    TargetId = targetId,
                    Role = DocumentCaptureLinkRoles.Attachment,
                    LinkedBy = actorId,
                    LinkedAt = _timeProvider.GetUtcNow().UtcDateTime
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
                created = true;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        if (created)
            await AuditAsync("document.linked", companyId, publicId, new { targetType, targetId = targetPublicId, role = DocumentCaptureLinkRoles.Attachment }).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an attachment link. Returns nothing: the link may have been the caller's only read path to the file, so the
    /// result is not re-read after commit (the caller already knows the outcome).
    /// </summary>
    public async Task UnlinkAsync(int captureId, string targetType, string targetPublicId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var companyId = tenant.RequireCompanyId();
        RequireActor();
        EnsureKnownTargetType(targetType);
        if (!Has(AttachPermission(targetType)))
            throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
        var targetId = await ResolveTargetIdAsync(targetType, targetPublicId, cancellationToken).ConfigureAwait(false);
        string publicId;
        var postedRecord = false;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await RequireReadableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            publicId = capture.PublicId;
            var link = await db.DocumentCaptureLinks.AsNoTracking().SingleOrDefaultAsync(l => l.CompanyId == companyId
                && l.CaptureId == captureId && l.TargetType == targetType && l.TargetId == targetId, cancellationToken).ConfigureAwait(false)
                ?? throw NotFound();
            if (link.Role != DocumentCaptureLinkRoles.Attachment)
                throw new DocumentCaptureServiceException(409, "DOCUMENT_SOURCE_LINK_FIXED");
            await LockTargetsAsync(companyId, [(targetType, targetId)], cancellationToken).ConfigureAwait(false);
            postedRecord = await IsTargetLockedAsync(companyId, targetType, targetId, cancellationToken).ConfigureAwait(false);
            if (!CanUnlink(link, postedRecord))
                throw new DocumentCaptureServiceException(403, "DOCUMENT_UNLINK_RESTRICTED");
            await db.DocumentCaptureLinks.Where(l => l.CompanyId == companyId && l.Id == link.Id)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            // Removing evidence is recorded in the same transaction: no unlink commits without its audit event.
            if (transactionalAudit is not null)
                await transactionalAudit.LogEventInTransactionAsync(db, "document.unlinked", "success", tenant.UserId, companyId,
                    UnlinkAuditMetadata(publicId, targetType, targetPublicId, postedRecord), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        if (transactionalAudit is null)
            await AuditAsync("document.unlinked", companyId, publicId, new { targetType, targetId = targetPublicId, postedRecord }).ConfigureAwait(false);
    }

    public Task<DocumentView> ArchiveAsync(int captureId, CancellationToken cancellationToken) =>
        SetArchivedAsync(captureId, archived: true, cancellationToken);

    public Task<DocumentView> UnarchiveAsync(int captureId, CancellationToken cancellationToken) =>
        SetArchivedAsync(captureId, archived: false, cancellationToken);

    public async Task<DocumentView> TrashAsync(int captureId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        RequireManage();
        var companyId = tenant.RequireCompanyId();
        var actorId = RequireActor();
        string publicId;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await LibraryRowForWriteAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            publicId = capture.PublicId;
            if (capture.DeletedAt is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
            }
            await EnsureRemovableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, now).SetProperty(c => c.DeletedBy, actorId), cancellationToken)
                .ConfigureAwait(false);
            if (updated != 1)
                throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync("document.trashed", companyId, publicId, null).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restores from the trash. Adds no bytes, so the storage cap is never rechecked (design 3.9 S4).</summary>
    public async Task<DocumentView> RestoreAsync(int captureId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        RequireManage();
        var companyId = tenant.RequireCompanyId();
        RequireActor();
        string publicId;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await LibraryRowForWriteAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            publicId = capture.PublicId;
            var updated = await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.DeletedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, (DateTime?)null).SetProperty(c => c.DeletedBy, (string?)null), cancellationToken)
                .ConfigureAwait(false);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
            }
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync("document.restored", companyId, publicId, null).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Purges a trashed document now: the row with its blob, links, attempts and mapping jobs. Refused while any link is locked or it is a scan's source.</summary>
    public async Task DeletePermanentlyAsync(int captureId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        RequireManage();
        var companyId = tenant.RequireCompanyId();
        RequireActor();
        string publicId;
        long byteCount;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await LibraryRowForWriteAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            if (capture.DeletedAt is null)
                throw new DocumentCaptureServiceException(409, "DOCUMENT_NOT_IN_TRASH");
            publicId = capture.PublicId;
            byteCount = capture.ByteCount;
            await EnsureRemovableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            // The whole row goes: blob, links, extraction attempts and AI-mapping jobs cascade, so no scanned text or field
            // value survives a permanent delete of any kind.
            await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync("document.deleted", companyId, publicId, new { byteCount }).ConfigureAwait(false);
    }

    /// <summary>
    /// Reserves one upload slot under the company lock: refuses with <c>DOCUMENT_RATE_LIMITED</c> when the workspace already has
    /// <see cref="DocumentsOptions.MaxUploadsPerHour"/> admissions in the last hour, otherwise records this one and commits.
    /// </summary>
    private async Task ReserveUploadSlotAsync(int companyId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var hourAgo = now.AddHours(-1);
        var admitted = await db.DocumentUploadAdmissions.AsNoTracking()
            .CountAsync(a => a.CompanyId == companyId && a.ReservedAt > hourAgo, cancellationToken).ConfigureAwait(false);
        if (admitted >= options.Value.MaxUploadsPerHour)
            throw new DocumentCaptureServiceException(429, "DOCUMENT_RATE_LIMITED");
        db.DocumentUploadAdmissions.Add(new DocumentUploadAdmission { CompanyId = companyId, ReservedAt = now });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    /// <summary>Whether a keyed earlier upload may be replayed to this caller: a live file the caller can read.</summary>
    private async Task<bool> CanSeeKeyedAsync(int companyId, DocumentCapture prior, CancellationToken cancellationToken) =>
        prior.FiledAt is not null && prior.BytesDeletedAt is null
        && await CanReadAsync(companyId, prior, cancellationToken).ConfigureAwait(false);

    // ---------- record-side hook ----------

    /// <summary>
    /// For record delete paths (draft bill/invoice delete, party delete): call inside the record's own transaction after the
    /// company admission lock and the record's row lock. Removes every link to the record and bumps the revision when any existed.
    /// The documents stay filed in the library.
    /// </summary>
    public static async Task<int> RemoveLinksForDeletedTargetAsync(ApiDbContext db, int companyId, string targetType, int targetId,
        CancellationToken cancellationToken)
    {
        var removed = await db.DocumentCaptureLinks
            .Where(l => l.CompanyId == companyId && l.TargetType == targetType && l.TargetId == targetId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (removed > 0)
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    // ---------- internals ----------

    private async Task<DocumentView> SetArchivedAsync(int captureId, bool archived, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        RequireManage();
        var companyId = tenant.RequireCompanyId();
        var actorId = RequireActor();
        string publicId;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
            var capture = await LibraryRowForWriteAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
            publicId = capture.PublicId;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = archived
                ? await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.ArchivedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchivedAt, now).SetProperty(c => c.ArchivedBy, actorId), cancellationToken).ConfigureAwait(false)
                : await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.ArchivedAt != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchivedAt, (DateTime?)null).SetProperty(c => c.ArchivedBy, (string?)null), cancellationToken).ConfigureAwait(false);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
            }
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync(archived ? "document.archived" : "document.unarchived", companyId, publicId, null).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Under the company lock: reads the links, locks every linked record in (TargetType, TargetId) order, then refuses when the
    /// file is a scan's source or any linked bill/invoice is issued, posted or void.
    /// </summary>
    private async Task EnsureRemovableAsync(int companyId, int captureId, CancellationToken cancellationToken)
    {
        var links = await db.DocumentCaptureLinks.AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.CaptureId == captureId)
            .Select(l => new { l.TargetType, l.TargetId, l.Role })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (links.Any(l => l.Role == DocumentCaptureLinkRoles.Source))
            throw new DocumentCaptureServiceException(409, "DOCUMENT_SOURCE_LINK_FIXED");
        var targets = links.Select(l => (l.TargetType, l.TargetId)).Distinct().ToList();
        await LockTargetsAsync(companyId, targets, cancellationToken).ConfigureAwait(false);
        foreach (var (type, id) in targets)
        {
            if (await IsTargetLockedAsync(companyId, type, id, cancellationToken).ConfigureAwait(false))
                throw new DocumentCaptureServiceException(409, "DOCUMENT_LOCKED_BY_POSTED_RECORD");
        }
    }

    internal Task LockTargetsAsync(int companyId, IEnumerable<(string TargetType, int TargetId)> targets, CancellationToken cancellationToken) =>
        DocumentTargetLocks.LockAsync(db, companyId, targets, cancellationToken);

    /// <summary>Fresh read after the row lock: true when the bill or invoice is no longer a draft (issued, posted or void).</summary>
    private async Task<bool> IsTargetLockedAsync(int companyId, string targetType, int targetId, CancellationToken cancellationToken) =>
        targetType switch
        {
            "bill" => await db.PurchaseInvoiceHeaders.AsNoTracking()
                .AnyAsync(h => h.CompanyId == companyId && h.Id == targetId
                    && ((h.Status != null && h.Status != PurchaseInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null), cancellationToken)
                .ConfigureAwait(false),
            "invoice" => await db.SalesInvoiceHeaders.AsNoTracking()
                .AnyAsync(h => h.CompanyId == companyId && h.Id == targetId
                    && ((h.Status != null && h.Status != SalesInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null), cancellationToken)
                .ConfigureAwait(false),
            _ => false
        };

    private async Task<bool> TargetExistsAsync(int companyId, string targetType, int targetId, CancellationToken cancellationToken) =>
        targetType switch
        {
            "bill" => await db.PurchaseInvoiceHeaders.AsNoTracking().AnyAsync(h => h.CompanyId == companyId && h.Id == targetId, cancellationToken).ConfigureAwait(false),
            "invoice" => await db.SalesInvoiceHeaders.AsNoTracking().AnyAsync(h => h.CompanyId == companyId && h.Id == targetId, cancellationToken).ConfigureAwait(false),
            "customer" => await db.Customers.AsNoTracking().AnyAsync(h => h.CompanyId == companyId && h.Id == targetId, cancellationToken).ConfigureAwait(false),
            "vendor" => await db.Vendors.AsNoTracking().AnyAsync(h => h.CompanyId == companyId && h.Id == targetId, cancellationToken).ConfigureAwait(false),
            _ => false
        };

    private async Task<int> ResolveTargetIdAsync(string targetType, string publicId, CancellationToken cancellationToken)
    {
        var prefix = targetType switch
        {
            "bill" => "bill",
            "invoice" => "inv",
            "customer" => "cus",
            "vendor" => "ven",
            _ => throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID")
        };
        var lookup = await publicIds.ResolveAsync(publicId, prefix, cancellationToken).ConfigureAwait(false);
        if (!lookup.Ok)
            throw NotFound();
        return lookup.Id;
    }

    /// <summary>Filed rows of filable kinds the caller may see in the library (design 3.8 R3 "list" column).</summary>
    private IQueryable<DocumentCapture> LibraryVisible(int companyId)
    {
        var allowedScanKinds = ReadableScanKinds(companyId);
        var billsReadable = Has(SystemPermissions.PurchasesBillsView);
        var invoicesReadable = Has(SystemPermissions.SalesInvoicesView);
        return db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiledAt != null && c.BytesDeletedAt == null
                && (c.Kind == DocumentKind
                    || (allowedScanKinds.Contains(c.Kind)
                        // Same rule as a scan read today: kind policy, and the source record readable when there is one
                        // (a scan whose draft was deleted keeps no source link and stays visible, removable evidence).
                        && (!db.DocumentCaptureLinks.Any(l => l.CompanyId == companyId && l.CaptureId == c.Id
                                && l.Role == DocumentCaptureLinkRoles.Source)
                        || db.DocumentCaptureLinks.Any(l => l.CompanyId == companyId && l.CaptureId == c.Id
                            && l.Role == DocumentCaptureLinkRoles.Source
                            && ((l.TargetType == "bill" && billsReadable
                                    && db.PurchaseInvoiceHeaders.Any(h => h.CompanyId == companyId && h.Id == l.TargetId))
                                || (l.TargetType == "invoice" && invoicesReadable
                                    && db.SalesInvoiceHeaders.Any(h => h.CompanyId == companyId && h.Id == l.TargetId))))))));
    }

    private string[] ReadableScanKinds(int companyId) => ScanKinds
        .Where(kind => DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, kind) == DocumentCaptureAccessFailure.None)
        .ToArray();

    private string[] ReadableTargetTypes() => TargetTypes.Where(type => Has(ReadPermission(type))).ToArray();

    /// <summary>Loads a filed capture and applies the per-kind read rule (design 3.8 R3). Unreadable is reported as not found.</summary>
    private async Task<DocumentCapture> RequireReadableAsync(int companyId, int captureId, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await EnsureCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId && c.FiledAt != null && c.BytesDeletedAt == null,
                cancellationToken).ConfigureAwait(false)
            ?? throw NotFound();
        if (!await CanReadAsync(companyId, capture, cancellationToken).ConfigureAwait(false))
            throw NotFound();
        return capture;
    }

    private async Task<bool> CanReadAsync(int companyId, DocumentCapture capture, CancellationToken cancellationToken)
    {
        if (capture.Kind == DocumentKind)
        {
            if (Has(SystemPermissions.DocumentsView))
                return true;
            var links = await db.DocumentCaptureLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.CaptureId == capture.Id)
                .Select(l => new { l.TargetType, l.TargetId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var link in links)
            {
                if (Has(ReadPermission(link.TargetType))
                    && await TargetExistsAsync(companyId, link.TargetType, link.TargetId, cancellationToken).ConfigureAwait(false))
                    return true;
            }
            return false;
        }
        if (!ScanKinds.Contains(capture.Kind, StringComparer.Ordinal))
            return false;
        if (DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind) != DocumentCaptureAccessFailure.None)
            return false;
        var source = await db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.CompanyId == companyId && l.CaptureId == capture.Id && l.Role == DocumentCaptureLinkRoles.Source,
                cancellationToken).ConfigureAwait(false);
        if (source is null)
            return true;
        return Has(ReadPermission(source.TargetType))
            && await TargetExistsAsync(companyId, source.TargetType, source.TargetId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A filed row managed from the library: <c>document</c> kind, or a filed scan the caller can read.</summary>
    private async Task<DocumentCapture> LibraryRowForWriteAsync(int companyId, int captureId, CancellationToken cancellationToken)
    {
        var capture = await RequireReadableAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
        if (!Has(SystemPermissions.DocumentsView))
            throw NotFound();
        return capture;
    }

    private async Task<IReadOnlyList<DocumentView>> ToViewsAsync(int companyId, IReadOnlyList<DocumentCapture> captures,
        CancellationToken cancellationToken)
    {
        if (captures.Count == 0)
            return Array.Empty<DocumentView>();
        var ids = captures.Select(c => c.Id).ToArray();
        var links = await db.DocumentCaptureLinks.AsNoTracking()
            .Where(l => l.CompanyId == companyId && ids.Contains(l.CaptureId))
            .OrderBy(l => l.LinkedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var resolved = new Dictionary<(string, int), (string PublicId, bool Locked)>();
        foreach (var group in links.GroupBy(l => l.TargetType, StringComparer.Ordinal))
        {
            if (!Has(ReadPermission(group.Key)))
                continue;
            var targetIds = group.Select(l => l.TargetId).Distinct().ToArray();
            switch (group.Key)
            {
                case "bill":
                    foreach (var row in await db.PurchaseInvoiceHeaders.AsNoTracking()
                                 .Where(h => h.CompanyId == companyId && targetIds.Contains(h.Id))
                                 .Select(h => new { h.Id, h.PublicId, Locked = (h.Status != null && h.Status != PurchaseInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null })
                                 .ToListAsync(cancellationToken).ConfigureAwait(false))
                        resolved[("bill", row.Id)] = (row.PublicId, row.Locked);
                    break;
                case "invoice":
                    foreach (var row in await db.SalesInvoiceHeaders.AsNoTracking()
                                 .Where(h => h.CompanyId == companyId && targetIds.Contains(h.Id))
                                 .Select(h => new { h.Id, h.PublicId, Locked = (h.Status != null && h.Status != SalesInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null })
                                 .ToListAsync(cancellationToken).ConfigureAwait(false))
                        resolved[("invoice", row.Id)] = (row.PublicId, row.Locked);
                    break;
                case "customer":
                    foreach (var row in await db.Customers.AsNoTracking()
                                 .Where(h => h.CompanyId == companyId && targetIds.Contains(h.Id))
                                 .Select(h => new { h.Id, h.PublicId })
                                 .ToListAsync(cancellationToken).ConfigureAwait(false))
                        resolved[("customer", row.Id)] = (row.PublicId, false);
                    break;
                case "vendor":
                    foreach (var row in await db.Vendors.AsNoTracking()
                                 .Where(h => h.CompanyId == companyId && targetIds.Contains(h.Id))
                                 .Select(h => new { h.Id, h.PublicId })
                                 .ToListAsync(cancellationToken).ConfigureAwait(false))
                        resolved[("vendor", row.Id)] = (row.PublicId, false);
                    break;
            }
        }

        // Locks are computed from every link (readable or not): hiding a locked record must not make the file look removable.
        var lockedCaptureIds = await LockedCaptureIdsAsync(companyId, links, resolved, cancellationToken).ConfigureAwait(false);
        var canManage = Has(SystemPermissions.DocumentsManage);
        var linksByCapture = links.ToLookup(l => l.CaptureId);
        return captures.Select(c =>
        {
            var views = linksByCapture[c.Id]
                .Where(l => resolved.ContainsKey((l.TargetType, l.TargetId)) && !string.IsNullOrWhiteSpace(resolved[(l.TargetType, l.TargetId)].PublicId))
                .Select(l =>
                {
                    var target = resolved[(l.TargetType, l.TargetId)];
                    return new DocumentLinkView(l.TargetType, target.PublicId, l.Role, DateTime.SpecifyKind(l.LinkedAt, DateTimeKind.Utc), target.Locked,
                        CanUnlink(l, target.Locked));
                })
                .ToList();
            return new DocumentView(
                c.PublicId,
                c.Kind,
                c.Kind == DocumentKind ? c.Status : "filed",
                c.Title ?? DefaultTitle(c),
                c.FileName,
                c.Category,
                c.DocumentDate is { } date ? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified) : null,
                c.Notes,
                c.ContentType,
                c.ByteCount,
                c.PageCount,
                c.Width,
                c.Height,
                c.ContentHash,
                c.SourceHash,
                !string.Equals(c.SourceHash, c.ContentHash, StringComparison.Ordinal),
                c.ArchivedAt is not null,
                c.DeletedAt is not null,
                DateTime.SpecifyKind(c.FiledAt ?? c.CreatedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                views,
                canManage && Has(SystemPermissions.DocumentsView),
                lockedCaptureIds.Contains(c.Id),
                linksByCapture[c.Id].Any(l => l.Role == DocumentCaptureLinkRoles.Source));
        }).ToList();
    }

    private async Task<HashSet<int>> LockedCaptureIdsAsync(int companyId, IReadOnlyList<DocumentCaptureLink> links,
        IReadOnlyDictionary<(string, int), (string PublicId, bool Locked)> resolved, CancellationToken cancellationToken)
    {
        var locked = new HashSet<int>();
        foreach (var link in links.Where(l => l.TargetType is "bill" or "invoice"))
        {
            bool isLocked;
            if (resolved.TryGetValue((link.TargetType, link.TargetId), out var known))
                isLocked = known.Locked;
            else
                isLocked = await IsTargetLockedAsync(companyId, link.TargetType, link.TargetId, cancellationToken).ConfigureAwait(false);
            if (isLocked)
                locked.Add(link.CaptureId);
        }
        return locked;
    }

    private async Task<DocumentCapture?> FindReadableDuplicateAsync(int companyId, string sourceHash, CancellationToken cancellationToken)
    {
        var candidates = await db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.SourceHash == sourceHash && c.FiledAt != null && c.BytesDeletedAt == null
                && (c.Kind == DocumentKind || ScanKinds.Contains(c.Kind)))
            .OrderByDescending(c => c.FiledAt)
            .Take(20)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            if (await CanReadAsync(companyId, candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }
        return null;
    }

    private Task<DocumentCapture?> KeyedDocumentAsync(int companyId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<DocumentView> ReplayAsync(DocumentCapture prior, string fingerprint, CancellationToken cancellationToken)
    {
        if (prior.Kind != DocumentKind
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(prior.RequestFingerprint), Convert.FromHexString(fingerprint)))
            throw new DocumentCaptureServiceException(409, "IDEMPOTENCY_CONFLICT");
        return await GetAsync(prior.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedDocument> PrepareAsync(byte[] source, string declaredContentType, DocumentUploadLimit limit,
        CancellationToken cancellationToken)
    {
        var config = captureOptions.Value;
        if (source.AsSpan().StartsWith("%PDF-"u8))
        {
            if (!string.Equals(declaredContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                throw new DocumentCaptureServiceException(400, "DOCUMENT_MEDIA_MISMATCH");
            var validation = await DocumentCapturePdfValidator.ValidateAsync(source, config.PdfParseSlots,
                DocumentCapturePdfProfile.ForStoredDocument(limit.MaxBytes, limit.MaxPdfPages), _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.IsValid)
            {
                if (validation.ErrorCode is "DOCUMENT_TOO_LARGE" or "DOCUMENT_TOO_MANY_PAGES")
                    throw new DocumentLimitException(validation.ErrorCode == "DOCUMENT_TOO_LARGE" ? 413 : 422, validation.ErrorCode, limit);
                throw new DocumentCaptureServiceException(StatusFor(validation.ErrorCode), validation.ErrorCode ?? "DOCUMENT_MALFORMED");
            }
            return new PreparedDocument(source, "application/pdf", null, null, validation.PageCount ?? 1, "pdf-structure-v1");
        }
        if (!string.Equals(declaredContentType, "image/png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declaredContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            throw new DocumentCaptureServiceException(415, "DOCUMENT_UNSUPPORTED_TYPE");
        try
        {
            var normalized = await DocumentCaptureImageNormalizer.NormalizeAsync(source, declaredContentType, config.ImageDecodeSlots,
                limit.MaxBytes, cancellationToken).ConfigureAwait(false);
            return new PreparedDocument(normalized.Bytes, normalized.ContentType, normalized.Width, normalized.Height, 1, "image-normalization-v2");
        }
        catch (DocumentCaptureMediaException ex) when (ex.ErrorCode == "DOCUMENT_TOO_LARGE")
        {
            throw new DocumentLimitException(413, ex.ErrorCode, limit);
        }
        catch (DocumentCaptureMediaException ex)
        {
            throw new DocumentCaptureServiceException(StatusFor(ex.ErrorCode), ex.ErrorCode);
        }
    }

    private static int StatusFor(string? errorCode) => errorCode switch
    {
        "DOCUMENT_EMPTY" or "DOCUMENT_MALFORMED" or "DOCUMENT_DIMENSIONS" => 400,
        "DOCUMENT_TOO_LARGE" => 413,
        "DOCUMENT_UNSUPPORTED_TYPE" => 415,
        "DOCUMENT_BUSY" => 503,
        "DOCUMENT_DECODE_BUSY" => 429,
        _ => 422
    };

    private async Task<long> UsedBytesAsync(int companyId, CancellationToken cancellationToken) =>
        await db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiledAt != null && c.BytesDeletedAt == null)
            .SumAsync(c => (long?)c.ByteCount, cancellationToken).ConfigureAwait(false) ?? 0L;

    private async Task EnsureCompanyLiveAsync(int companyId, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId && c.DeletedAt == null, cancellationToken).ConfigureAwait(false))
            throw NotFound();
    }

    private void EnsureEnabled()
    {
        if (!options.Value.Enabled)
            throw new DocumentCaptureServiceException(404, "DOCUMENTS_DISABLED");
    }

    private void RequireManage()
    {
        if (!Has(SystemPermissions.DocumentsManage) || !Has(SystemPermissions.DocumentsView))
            throw new DocumentCaptureServiceException(403, "DOCUMENT_PERMISSION_REQUIRED");
    }

    private string RequireActor() =>
        string.IsNullOrWhiteSpace(tenant.UserId) ? throw new DocumentCaptureServiceException(401, "DOCUMENT_UNAUTHORIZED") : tenant.UserId!;

    private bool Has(string permission) => tenant.Permissions.Contains(permission);

    /// <summary>
    /// Removing needs the record's attach permission, for everyone: losing invoice or bill create rights ends the right to
    /// change their evidence. On a draft (or a party) that is enough. Once a bill or invoice is posted, issued or void, the
    /// caller must also hold Manage Documents (Owner and Admin do), or be the member who linked the file. A scanned source is
    /// never removable.
    /// </summary>
    private bool CanUnlink(DocumentCaptureLink link, bool postedRecord)
    {
        if (link.Role != DocumentCaptureLinkRoles.Attachment || !Has(AttachPermission(link.TargetType)))
            return false;
        if (!postedRecord)
            return true;
        // Manage Documents is the library's admin authority (Owner and Admin hold it through Full access).
        if (Has(SystemPermissions.DocumentsManage))
            return true;
        return !string.IsNullOrWhiteSpace(tenant.UserId) && string.Equals(link.LinkedBy, tenant.UserId, StringComparison.Ordinal);
    }

    private static void EnsureKnownTargetType(string targetType)
    {
        if (!TargetTypes.Contains(targetType, StringComparer.Ordinal))
            throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID");
    }

    internal static string ReadPermission(string targetType) => targetType switch
    {
        "bill" => SystemPermissions.PurchasesBillsView,
        "invoice" => SystemPermissions.SalesInvoicesView,
        "customer" => SystemPermissions.SalesCustomersView,
        "vendor" => SystemPermissions.PurchasesVendorsView,
        _ => "\u0000none"
    };

    internal static string AttachPermission(string targetType) => targetType switch
    {
        "bill" => SystemPermissions.PurchasesBillsCreate,
        "invoice" => SystemPermissions.SalesInvoicesCreate,
        "customer" => SystemPermissions.SalesCustomersManage,
        "vendor" => SystemPermissions.PurchasesVendorsManage,
        _ => "\u0000none"
    };

    private static DocumentCaptureServiceException NotFound() => new(404, "DOCUMENT_NOT_FOUND");

    private static object UnlinkAuditMetadata(string documentId, string targetType, string targetId, bool postedRecord) =>
        new { documentId, details = new { targetType, targetId, postedRecord } };

    private Task AuditAsync(string eventType, int companyId, string documentId, object? details) => securityAudit is null
        ? Task.CompletedTask
        : securityAudit.LogEventAsync(eventType, "success", tenant.UserId, companyId, new { documentId, details });

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string DefaultTitle(DocumentCapture capture) => capture.Kind switch
    {
        "expense-receipt" => "Scanned receipt",
        "purchase-invoice" => "Scanned bill",
        "sales-invoice" => "Scanned invoice",
        _ => capture.FileName is { Length: > 0 } name ? Path.GetFileNameWithoutExtension(name) : "Document"
    };

    private static string DownloadName(DocumentCapture capture)
    {
        var extension = capture.ContentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            _ => ".png"
        };
        var title = SafeName(capture.Title ?? DefaultTitle(capture), 80);
        return (string.IsNullOrEmpty(title) ? "document" : title) + extension;
    }

    internal static string SafeName(string value, int maxLength)
    {
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value)
        {
            if (builder.Length >= maxLength)
                break;
            builder.Append(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' ? ch : '_');
        }
        return builder.ToString().Trim().TrimStart('.');
    }

    internal static NormalizedMetadata NormalizeMetadata(DocumentMetadataInput input, string? fileName)
    {
        // Browsers may send a full client path with either separator; keep only the last segment, on every host OS.
        var rawName = fileName ?? string.Empty;
        var lastSeparator = rawName.LastIndexOfAny(['/', '\\']);
        var cleanFileName = CleanText(lastSeparator >= 0 ? rawName[(lastSeparator + 1)..] : rawName, 255);
        var title = CleanText(input.Title, 200);
        if (string.IsNullOrEmpty(title))
            title = string.IsNullOrEmpty(cleanFileName) ? "Document" : CleanText(Path.GetFileNameWithoutExtension(cleanFileName), 200);
        if (string.IsNullOrEmpty(title))
            title = "Document";
        string? category = null;
        if (!string.IsNullOrWhiteSpace(input.Category))
        {
            category = input.Category.Trim().ToLowerInvariant();
            if (!Categories.Contains(category, StringComparer.Ordinal))
                throw new DocumentCaptureServiceException(400, "DOCUMENT_CATEGORY_INVALID");
        }
        if (input.Notes is { Length: > 1_000 })
            throw new DocumentCaptureServiceException(400, "DOCUMENT_NOTES_TOO_LONG");
        if (input.Title is { Length: > 200 })
            throw new DocumentCaptureServiceException(400, "DOCUMENT_TITLE_TOO_LONG");
        var notes = CleanText(input.Notes, 1_000, keepNewLines: true);
        var date = input.DocumentDate is { } d ? DateTime.SpecifyKind(d.Date, DateTimeKind.Unspecified) : (DateTime?)null;
        if (date is { Year: < 1900 or > 2200 })
            throw new DocumentCaptureServiceException(400, "DOCUMENT_DATE_INVALID");
        return new NormalizedMetadata(string.IsNullOrEmpty(cleanFileName) ? null : cleanFileName, title, category, date,
            string.IsNullOrEmpty(notes) ? null : notes);
    }

    private static string CleanText(string? value, int maxLength, bool keepNewLines = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value)
        {
            if (builder.Length >= maxLength)
                break;
            if (keepNewLines && ch is '\n')
                builder.Append(ch);
            else if (!char.IsControl(ch))
                builder.Append(ch);
        }
        return builder.ToString().Trim();
    }

    internal sealed record NormalizedMetadata(string? FileName, string Title, string? Category, DateTime? DocumentDate, string? Notes);

    private sealed record PreparedDocument(byte[] Bytes, string ContentType, int? Width, int? Height, int PageCount, string NormalizationVersion);
}

/// <summary>
/// Row locks on records that documents link to, in one deterministic order: (TargetType ordinal, TargetId ascending). Callers hold
/// the company admission lock first (design 3.11 U1). Postgres takes <c>FOR UPDATE</c>; SQLite already serialises writers through
/// the admission lock's IMMEDIATE transaction.
/// </summary>
public static class DocumentTargetLocks
{
    public static async Task LockAsync(ApiDbContext db, int companyId, IEnumerable<(string TargetType, int TargetId)> targets,
        CancellationToken cancellationToken)
    {
        if (!(db.Database.ProviderName ?? string.Empty).Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var (type, id) in targets.Distinct().OrderBy(t => t.TargetType, StringComparer.Ordinal).ThenBy(t => t.TargetId))
        {
            var entity = type switch
            {
                "bill" => db.Model.FindEntityType(typeof(PurchaseInvoiceHeader)),
                "invoice" => db.Model.FindEntityType(typeof(SalesInvoiceHeader)),
                "customer" => db.Model.FindEntityType(typeof(Customer)),
                "vendor" => db.Model.FindEntityType(typeof(Vendor)),
                _ => null
            } ?? throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID");
            // The table name comes from the EF model for one of four fixed entity types, never from input; values are parameters.
            var sql = "SELECT \"Id\" AS \"Value\" FROM " + QualifiedTable(entity)
                + " WHERE \"CompanyId\" = {0} AND \"Id\" = {1} FOR UPDATE";
            await db.Database.SqlQueryRaw<int>(sql, companyId, id).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string QualifiedTable(IEntityType entity)
    {
        var table = entity.GetTableName() ?? throw new InvalidOperationException("Entity has no table.");
        var schema = entity.GetSchema();
        return schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
    }
}

/// <summary>A per-file limit was exceeded; carries the limit that applied so the client can say it.</summary>
public sealed class DocumentLimitException(int statusCode, string errorCode, DocumentUploadLimit limit)
    : DocumentCaptureServiceException(statusCode, errorCode)
{
    public DocumentUploadLimit Limit { get; } = limit;
}

/// <summary>The workspace's document storage cap would be exceeded by this upload.</summary>
public sealed class DocumentStorageLimitException(long usedBytes, long limitBytes, long requestedBytes)
    : DocumentCaptureServiceException(403, "DOCUMENT_STORAGE_LIMIT")
{
    public long UsedBytes { get; } = usedBytes;
    public long LimitBytes { get; } = limitBytes;
    public long RequestedBytes { get; } = requestedBytes;
}
