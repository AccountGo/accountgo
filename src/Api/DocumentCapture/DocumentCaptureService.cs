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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Api.DocumentCaptures.Mapping;

namespace Api.DocumentCaptures;

public sealed record DocumentCaptureView(
    string Id,
    string Kind,
    int SchemaVersion,
    string Status,
    string ContentType,
    long ByteCount,
    int? Width,
    int? Height,
    int PageCount,
    JToken? Fields,
    JToken? AcceptedFields,
    JToken? Warnings,
    string? ErrorCode,
    bool BytesAvailable,
    bool CanAccept,
    bool CanApply,
    string? ProviderName,
    string? ProviderVersion,
    string SourceHash,
    string ContentHash,
    string NormalizationVersion,
    DocumentCaptureLinkView? Link,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    DateTime? RejectedAt,
    DocumentCaptureAiMappingView? AiMapping = null,
    IReadOnlyList<DocumentCaptureLinkView>? Links = null)
{
    public IReadOnlyList<DocumentCaptureLinkView> Links { get; init; } = Links ?? Array.Empty<DocumentCaptureLinkView>();
}

/// <summary>
/// Where the optional background field mapping stands for a capture: pending, done, failed or skipped (absent when mapping is not
/// on). <see cref="RuleHints"/> maps a field path to the value the rules found, for the fields the mapper changed.
/// </summary>
public sealed record DocumentCaptureAiMappingView(string Status, IReadOnlyDictionary<string, string> RuleHints);

public sealed record DocumentCaptureLinkView(
    string TargetType,
    string TargetId,
    DateTime LinkedAt,
    string? TaxTreatment = null,
    string Role = DocumentCaptureLinkRoles.Source);

public sealed class DocumentCaptureService(
    ApiDbContext db,
    IDocumentCaptureAdmissionLock admissionLock,
    IDocumentFieldExtractor extractor,
    IEntitlementService entitlements,
    IPublicIdResolver publicIds,
    ITenantContext tenant,
    IOptions<DocumentCaptureOptions> options,
    ILogger<DocumentCaptureService> logger,
    ISecurityAuditService? securityAudit = null,
    TimeProvider? timeProvider = null,
    ICaptureAiMappingPolicy? aiMappingPolicy = null,
    CaptureAiMappingSignal? aiMappingSignal = null)
{
    private static readonly TimeSpan ExtractorDeadline = CaptureWorkerRunner.Deadline;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DocumentCaptureView> UploadAsync(
        string kind,
        int schemaVersion,
        string contentType,
        byte[] sourceBytes,
        string? idempotencyKey,
        CancellationToken cancellationToken,
        bool force = false)
    {
        var config = options.Value;
        if (!config.Enabled || !config.ExtractionHost || !extractor.IsConfigured)
            throw new DocumentCaptureServiceException(503, "DOCUMENT_CAPTURE_UNCONFIGURED");
        if (idempotencyKey is { Length: 0 or > 80 })
            throw new DocumentCaptureServiceException(400, "DOCUMENT_IDEMPOTENCY_KEY_INVALID");

        var companyId = tenant.RequireCompanyId();
        var actorId = tenant.UserId;
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DocumentCaptureServiceException(401, "DOCUMENT_UNAUTHORIZED");
        if (!DocumentCaptureFieldSchemas.IsSupportedKind(kind))
            throw new DocumentCaptureServiceException(400, "DOCUMENT_KIND_UNKNOWN");
        var access = DocumentCaptureAccessPolicy.AuthorizeNew(entitlements, companyId, tenant.Permissions, kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, kind, companyId);
        if (schemaVersion != DocumentCaptureFieldSchemas.CurrentSchemaVersion)
            throw new DocumentCaptureServiceException(400, "DOCUMENT_KIND_UNKNOWN");

        var sourceHash = Hash(sourceBytes);
        var fingerprint = Hash(Encoding.UTF8.GetBytes($"{sourceHash}\n{kind}\n{schemaVersion}"));
        var prior = idempotencyKey is null ? null : await CapturesWithKey(companyId, idempotencyKey).AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (prior is not null && !await CanReadCandidateAsync(prior, companyId, actorId, cancellationToken).ConfigureAwait(false))
            prior = null;

        // The read-only check avoids preparing media and queuing for a replay. The transaction rechecks the result.
        var skipPreparation = prior is not null
            ? !FingerprintMatches(prior, fingerprint) || IsTerminalReplay(prior) || HasActiveLease(prior, _timeProvider.GetUtcNow().UtcDateTime)
            : !force && await FindReadableDuplicateAsync(companyId, actorId, sourceHash, kind, schemaVersion, cancellationToken)
                .ConfigureAwait(false) is not null;
        var prepared = skipPreparation ? null
            : await PrepareMediaAsync(kind, contentType, sourceBytes, config, cancellationToken).ConfigureAwait(false);
        using var ocrAdmission = prepared is not null && extractor is LocalOcrDocumentFieldExtractor localOcr
            ? await localOcr.AdmitAsync(prepared.PageCount, cancellationToken).ConfigureAwait(false)
            : null;
        var contentHash = prepared is null ? null : Hash(prepared.Bytes);
        var captureId = 0;
        var capturePublicId = string.Empty;
        var generation = 1;
        var replay = false;

        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            if (!await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == companyId && c.DeletedAt == null, cancellationToken).ConfigureAwait(false))
                throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var existing = idempotencyKey is null ? null : await CapturesWithKey(companyId, idempotencyKey)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null && !await CanReadCandidateAsync(existing, companyId, actorId, cancellationToken).ConfigureAwait(false))
            {
                existing = null;
                idempotencyKey = null;
            }
            if (existing is not null)
            {
                if (!FingerprintMatches(existing, fingerprint))
                    throw new DocumentCaptureServiceException(409, "IDEMPOTENCY_CONFLICT");
                if (IsTerminalReplay(existing))
                {
                    replay = true;
                    captureId = existing.Id;
                    capturePublicId = existing.PublicId;
                    generation = existing.ExtractionGeneration;
                }
                else if (HasActiveLease(existing, now))
                {
                    throw new DocumentCaptureServiceException(409, "DOCUMENT_EXTRACTION_IN_PROGRESS");
                }
                else
                {
                    var media = RequirePreparedMedia(prepared);
                    await CheckAttemptLimitAsync(companyId, now, config, cancellationToken).ConfigureAwait(false);
                    existing.ExtractionGeneration++;
                    existing.Status = "extracting";
                    existing.ExtractingStartedAt = now;
                    existing.LeaseExpiresAt = now.AddSeconds(config.ExtractionLeaseSeconds);
                    existing.ExpiresAt = now.AddHours(config.AbandonedRetentionHours);
                    existing.FieldsWipedAt = null;
                    existing.ErrorCode = null;
                    generation = existing.ExtractionGeneration;
                    captureId = existing.Id;
                    capturePublicId = existing.PublicId;
                    if (!await db.DocumentCaptureBlobs.IgnoreQueryFilters().AnyAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false))
                    {
                        db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob
                        {
                            CompanyId = companyId,
                            CaptureId = captureId,
                            DocumentBytes = media.Bytes
                        });
                        existing.BytesDeletedAt = null;
                    }
                    db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt
                    {
                        CompanyId = companyId,
                        CaptureId = captureId,
                        Generation = generation,
                        ReservedAt = now
                    });
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                var duplicate = force ? null : await FindReadableDuplicateAsync(companyId, actorId, sourceHash, kind,
                    schemaVersion, cancellationToken).ConfigureAwait(false);
                if (duplicate is not null)
                {
                    replay = true;
                    captureId = duplicate.Id;
                    capturePublicId = duplicate.PublicId;
                    generation = duplicate.ExtractionGeneration;
                }
                else
                {
                    var media = RequirePreparedMedia(prepared);
                    await CheckAttemptLimitAsync(companyId, now, config, cancellationToken).ConfigureAwait(false);
                    var capture = new DocumentCapture
                    {
                        CompanyId = companyId,
                        Kind = kind,
                        SchemaVersion = schemaVersion,
                        Status = "extracting",
                        SourceHash = sourceHash,
                        ContentHash = contentHash!,
                        NormalizationVersion = media.NormalizationVersion,
                        ContentType = media.ContentType,
                        ByteCount = media.Bytes.Length,
                        Width = media.Width,
                        Height = media.Height,
                        PageCount = media.PageCount,
                        IdempotencyKey = idempotencyKey ?? $"omitted:{Guid.NewGuid():N}",
                        RequestFingerprint = fingerprint,
                        ExtractionGeneration = generation,
                        ExtractingStartedAt = now,
                        LeaseExpiresAt = now.AddSeconds(config.ExtractionLeaseSeconds),
                        CreatedBy = actorId,
                        CreatedAt = now,
                        ExpiresAt = now.AddHours(config.AbandonedRetentionHours),
                        Blob = new DocumentCaptureBlob { CompanyId = companyId, DocumentBytes = media.Bytes },
                        Attempts =
                        [
                            new DocumentCaptureAttempt { CompanyId = companyId, Generation = generation, ReservedAt = now }
                        ]
                    };
                    db.DocumentCaptures.Add(capture);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    captureId = capture.Id;
                    capturePublicId = capture.PublicId;
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();

        if (replay)
            return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);

        await AuditTransitionAsync("document.capture.reserved", "success", companyId, capturePublicId, kind,
            "extracting", generation, actorId).ConfigureAwait(false);

        var result = await RunExtractionAsync(companyId, captureId, capturePublicId, generation, prepared!, ocrAdmission, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == "failed")
        {
            var code = result.ErrorCode ?? "DOCUMENT_UNREADABLE";
            var statusCode = code switch
            {
                "DOCUMENT_PROVIDER_TIMEOUT" => 504,
                "DOCUMENT_PROVIDER_UNAVAILABLE" or "DOCUMENT_CAPTURE_UNCONFIGURED" => 503,
                _ => 422
            };
            throw new DocumentCaptureServiceException(statusCode, code, result.Id);
        }
        return result;
    }

    private IQueryable<DocumentCapture> CapturesWithKey(int companyId, string idempotencyKey) =>
        db.DocumentCaptures.IgnoreQueryFilters()
            .Where(capture => capture.CompanyId == companyId && capture.IdempotencyKey == idempotencyKey);

    private static bool FingerprintMatches(DocumentCapture capture, string fingerprint) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(capture.RequestFingerprint), Convert.FromHexString(fingerprint));

    private static bool IsTerminalReplay(DocumentCapture capture) => capture.Status is "ready" or "accepted" or "rejected";

    private static bool HasActiveLease(DocumentCapture capture, DateTime now) =>
        capture.Status == "extracting" && capture.LeaseExpiresAt > now;

    private static PreparedCaptureMedia RequirePreparedMedia(PreparedCaptureMedia? media) =>
        media ?? throw new DocumentCaptureServiceException(429, "DOCUMENT_DECODE_BUSY");

    private async Task<DocumentCapture?> FindReadableDuplicateAsync(int companyId, string actorId, string sourceHash,
        string kind, int schemaVersion, CancellationToken cancellationToken)
    {
        var candidates = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .Where(capture => capture.CompanyId == companyId && capture.SourceHash == sourceHash
                && capture.Kind == kind && capture.SchemaVersion == schemaVersion
                && (capture.Status == "ready" || capture.Status == "accepted") && capture.BytesDeletedAt == null)
            .Where(capture => db.DocumentCaptureBlobs.IgnoreQueryFilters()
                .Any(blob => blob.CompanyId == companyId && blob.CaptureId == capture.Id))
            .OrderByDescending(capture => capture.CreatedAt)
            .Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
            if (await CanReadCandidateAsync(candidate, companyId, actorId, cancellationToken).ConfigureAwait(false))
                return candidate;
        return null;
    }

    public async Task<IReadOnlyList<Api.DocumentCaptures.Matching.PartyMatchCandidate>> MatchPartyAsync(
        string role,
        int captureId,
        CancellationToken cancellationToken)
    {
        var normalizedRole = Api.DocumentCaptures.Matching.PartyMatchingService.NormalizeRole(role);
        if (normalizedRole is null)
            throw new DocumentCaptureServiceException(400, "DOCUMENT_MATCH_ROLE_INVALID");

        var companyId = tenant.RequireCompanyId();
        if (!await IsCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");

        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");

        EnsureCreatorAccess(capture);
        var access = DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, capture.Kind, companyId);
        await EnsureLinkedTargetReadAsync(capture, companyId, cancellationToken).ConfigureAwait(false);

        var kindMatchesRole = normalizedRole == Api.DocumentCaptures.Matching.PartyMatchingService.RoleVendor
            ? capture.Kind is "purchase-invoice" or "expense-receipt"
            : capture.Kind == "sales-invoice";
        if (!kindMatchesRole)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");

        if (capture.FieldsWipedAt is not null
            || capture.RejectedAt is not null
            || string.Equals(capture.Status, "rejected", StringComparison.Ordinal))
        {
            return Array.Empty<Api.DocumentCaptures.Matching.PartyMatchCandidate>();
        }

        return await Api.DocumentCaptures.Matching.PartyMatchingService
            .MatchCaptureAsync(db, capture, normalizedRole, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DocumentCaptureView> GetAsync(int captureId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        if (!await IsCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(capture);
        var access = DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, capture.Kind, companyId);
        await EnsureLinkedTargetReadAsync(capture, companyId, cancellationToken).ConfigureAwait(false);
        var bytesAvailable = await db.DocumentCaptureBlobs.AsNoTracking()
            .AnyAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false);
        var link = await GetLinkViewAsync(capture, companyId, cancellationToken).ConfigureAwait(false);
        var links = await GetReadableLinkViewsAsync(capture.Id, companyId, cancellationToken).ConfigureAwait(false);
        var canWrite = DocumentCaptureAccessPolicy.AuthorizeNew(entitlements, companyId, tenant.Permissions, capture.Kind)
            == DocumentCaptureAccessFailure.None;
        var aiMapping = await GetAiMappingViewAsync(capture, companyId, cancellationToken).ConfigureAwait(false);
        return ToView(capture, bytesAvailable, canWrite, link, aiMapping, links);
    }

    private async Task<DocumentCaptureAiMappingView?> GetAiMappingViewAsync(DocumentCapture capture, int companyId, CancellationToken cancellationToken)
    {
        if (!options.Value.AiMapping.Enabled || capture.Status != "ready")
            return null;
        var job = await db.DocumentCaptureAiMappings.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CaptureId == capture.Id && item.ExtractionGeneration == capture.ExtractionGeneration)
            .Select(item => new { item.Status, item.RuleHintsJson })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (job is null)
            return null;
        var status = job.Status switch { "queued" or "running" => "pending", var other => other };
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        if (status == "done" && !string.IsNullOrWhiteSpace(job.RuleHintsJson))
        {
            try
            {
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(job.RuleHintsJson) ?? [])
                    hints[pair.Key] = pair.Value;
            }
            catch (JsonException)
            {
                hints.Clear();
            }
        }
        return new DocumentCaptureAiMappingView(status, hints);
    }

    public async Task<(byte[] Bytes, string ContentType)> GetBytesAsync(int captureId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        if (!await IsCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken).ConfigureAwait(false);
        if (capture is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(capture);
        var access = DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, capture.Kind, companyId);
        await EnsureLinkedTargetReadAsync(capture, companyId, cancellationToken).ConfigureAwait(false);
        var blob = await db.DocumentCaptureBlobs.AsNoTracking()
            .SingleOrDefaultAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false);
        if (blob is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_BYTES_UNAVAILABLE");
        return (blob.DocumentBytes, capture.ContentType);
    }

    public async Task<DocumentCaptureView> LinkAsync(
        int captureId,
        string targetType,
        string targetPublicId,
        CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var visibleCapture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(visibleCapture);
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(capture);
        var writeAccess = DocumentCaptureAccessPolicy.AuthorizeExistingWrite(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (writeAccess != DocumentCaptureAccessFailure.None)
            throw AccessFailure(writeAccess, capture.Kind, companyId);
        if (capture.Status != "accepted" || capture.AcceptedFieldsJson is null)
            throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
        if (!await db.DocumentCaptureBlobs.IgnoreQueryFilters()
                .AnyAsync(blob => blob.CompanyId == companyId && blob.CaptureId == captureId, cancellationToken)
                .ConfigureAwait(false))
            throw new DocumentCaptureServiceException(409, "DOCUMENT_BYTES_UNAVAILABLE");

        int targetId;
        string permission;
        string[] allowedKinds;
        if (targetType == "bill")
        {
            targetId = await ResolveTargetAsync(targetPublicId, "bill", cancellationToken).ConfigureAwait(false);
            permission = SystemPermissions.PurchasesBillsView;
            allowedKinds = ["expense-receipt", "purchase-invoice"];
        }
        else if (targetType == "invoice")
        {
            targetId = await ResolveTargetAsync(targetPublicId, "inv", cancellationToken).ConfigureAwait(false);
            permission = SystemPermissions.SalesInvoicesView;
            allowedKinds = ["sales-invoice"];
        }
        else
        {
            throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID");
        }
        if (!allowedKinds.Contains(capture.Kind, StringComparer.Ordinal)
            || !tenant.Permissions.Contains(permission))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        DocumentFiling.EnsureFilableKind(capture.Kind);
        var targetExists = targetType == "bill"
            ? await db.PurchaseInvoiceHeaders.AsNoTracking().AnyAsync(target => target.CompanyId == companyId && target.Id == targetId, cancellationToken).ConfigureAwait(false)
            : await db.SalesInvoiceHeaders.AsNoTracking().AnyAsync(target => target.CompanyId == companyId && target.Id == targetId, cancellationToken).ConfigureAwait(false);
        if (!targetExists)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");

        var existingSource = await db.DocumentCaptureLinks.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(link => link.CompanyId == companyId && link.CaptureId == captureId
                && link.Role == DocumentCaptureLinkRoles.Source, cancellationToken)
            .ConfigureAwait(false);
        if (existingSource is not null)
        {
            if (existingSource.TargetType != targetType || existingSource.TargetId != targetId)
                throw new DocumentCaptureServiceException(409, "DOCUMENT_LINK_CONFLICT");
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
        }

        if (await db.DocumentCaptureLinks.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(link => link.CompanyId == companyId && link.CaptureId == captureId
                    && link.TargetType == targetType && link.TargetId == targetId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new DocumentCaptureServiceException(409, "DOCUMENT_LINK_CONFLICT");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var actorId = tenant.UserId ?? string.Empty;
        db.DocumentCaptureLinks.Add(new DocumentCaptureLink
        {
            CompanyId = companyId,
            CaptureId = captureId,
            TargetType = targetType,
            TargetId = targetId,
            Role = DocumentCaptureLinkRoles.Source,
            LinkedBy = actorId,
            LinkedAt = now
        });
        await DocumentFiling.FileIfUnfiledAsync(db, companyId, captureId, capture.Kind, actorId, now, cancellationToken)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        CaptureTaxTreatment.RefreshForTarget(db, tenant, targetType, targetId);
        await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        await AuditTransitionAsync("document.capture.linked", "success", companyId, capture.PublicId, capture.Kind,
            capture.Status, capture.ExtractionGeneration, tenant.UserId).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentCaptureView>> GetLinkedAsync(
        string targetType,
        string targetPublicId,
        CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var (expectedPrefix, permission) = targetType switch
        {
            "bill" => ("bill", SystemPermissions.PurchasesBillsView),
            "invoice" => ("inv", SystemPermissions.SalesInvoicesView),
            _ => throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND")
        };
        if (!tenant.Permissions.Contains(permission))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var targetId = await ResolveTargetAsync(targetPublicId, expectedPrefix, cancellationToken).ConfigureAwait(false);
        var liveCompanies = LiveCompany(db, companyId);
        var targetExists = targetType == "bill"
            ? await db.PurchaseInvoiceHeaders.AsNoTracking()
                .AnyAsync(target => target.CompanyId == companyId && target.Id == targetId && liveCompanies.Any(), cancellationToken)
                .ConfigureAwait(false)
            : await db.SalesInvoiceHeaders.AsNoTracking()
                .AnyAsync(target => target.CompanyId == companyId && target.Id == targetId && liveCompanies.Any(), cancellationToken)
                .ConfigureAwait(false);
        if (!targetExists)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var links = await db.DocumentCaptureLinks.AsNoTracking()
            .Where(link => link.CompanyId == companyId && link.TargetType == targetType && link.TargetId == targetId
                && link.Role == DocumentCaptureLinkRoles.Source)
            .OrderBy(link => link.LinkedAt)
            .Select(link => new { link.CaptureId, link.TargetType, link.TargetId, link.LinkedAt, link.TaxTreatment, link.Role })
            .Take(100)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (links.Count == 0)
            return Array.Empty<DocumentCaptureView>();

        var captureIds = links.Select(link => link.CaptureId).ToArray();
        var captures = await db.DocumentCaptures.AsNoTracking()
            .Where(capture => capture.CompanyId == companyId && captureIds.Contains(capture.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var blobs = await db.DocumentCaptureBlobs.AsNoTracking()
            .Where(blob => blob.CompanyId == companyId && captureIds.Contains(blob.CaptureId))
            .Select(blob => blob.CaptureId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var blobIds = blobs.ToHashSet();
        var readableLinksByCapture = await GetReadableLinkViewsByCaptureAsync(
            captureIds,
            companyId,
            knownTarget: (targetType, targetId, targetPublicId),
            cancellationToken).ConfigureAwait(false);
        var linksById = links.ToDictionary(link => link.CaptureId);
        var views = new List<DocumentCaptureView>(captures.Count);
        foreach (var capture in captures.OrderBy(item => linksById[item.Id].LinkedAt))
        {
            EnsureCreatorAccess(capture);
            var access = DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind);
            if (access != DocumentCaptureAccessFailure.None)
                throw AccessFailure(access, capture.Kind, companyId);
            var link = linksById[capture.Id];
            var readableLinks = readableLinksByCapture.TryGetValue(capture.Id, out var foundLinks)
                ? foundLinks
                : Array.Empty<DocumentCaptureLinkView>();
            views.Add(ToView(capture, blobIds.Contains(capture.Id),
                DocumentCaptureAccessPolicy.AuthorizeExistingWrite(entitlements, companyId, tenant.Permissions, capture.Kind)
                    == DocumentCaptureAccessFailure.None,
                new DocumentCaptureLinkView(link.TargetType, targetPublicId, DateTime.SpecifyKind(link.LinkedAt, DateTimeKind.Utc), link.TaxTreatment, link.Role),
                links: readableLinks));
        }
        return views;
    }

    public async Task<DocumentCaptureView> AcceptAsync(
        int captureId,
        string fieldsJson,
        CancellationToken cancellationToken,
        bool purgeBytes = false)
    {
        var companyId = tenant.RequireCompanyId();
        if (!await IsCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var actorId = tenant.UserId;
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DocumentCaptureServiceException(401, "DOCUMENT_UNAUTHORIZED");
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(capture);
        if (!options.Value.Enabled)
            throw new DocumentCaptureServiceException(503, "DOCUMENT_CAPTURE_DISABLED");
        var access = DocumentCaptureAccessPolicy.AuthorizeExistingWrite(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, capture.Kind, companyId);
        fieldsJson = DocumentCaptureMasking.MaskFinancialFreeText(capture.Kind, fieldsJson);
        using var request = JsonDocument.Parse(fieldsJson);
        if (!DocumentCaptureFieldSchemas.TryValidateAcceptedFields(capture.Kind, capture.SchemaVersion, request.RootElement, out var canonical, out _))
            throw new DocumentCaptureServiceException(400, "DOCUMENT_ACCEPT_INVALID");
        if (capture.Kind == "assistant-attachment")
        {
            using var canonicalDoc = JsonDocument.Parse(canonical);
            canonical = DocumentCaptureMasking.MaskAssistantFields(canonicalDoc.RootElement);
        }

        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        capture = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        if (capture.Status == "accepted")
        {
            if (string.Equals(capture.AcceptedFieldsJson, canonical, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
            }
            throw new DocumentCaptureServiceException(409, "DOCUMENT_ACCEPT_CONFLICT");
        }
        if (capture.Status != "ready")
            throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
        if (!await db.DocumentCaptureBlobs.AnyAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(409, "DOCUMENT_BYTES_UNAVAILABLE");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = purgeBytes
            ? await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.Status == "ready")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, "accepted")
                    .SetProperty(c => c.AcceptedFieldsJson, canonical)
                    .SetProperty(c => c.AcceptedBy, actorId)
                    .SetProperty(c => c.AcceptedAt, now)
                    .SetProperty(c => c.BytesDeletedAt, now), cancellationToken).ConfigureAwait(false)
            : await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.Status == "ready")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, "accepted")
                    .SetProperty(c => c.AcceptedFieldsJson, canonical)
                    .SetProperty(c => c.AcceptedBy, actorId)
                    .SetProperty(c => c.AcceptedAt, now), cancellationToken).ConfigureAwait(false);
        if (updated != 1)
            throw new DocumentCaptureServiceException(409, "DOCUMENT_ACCEPT_CONFLICT");
        if (purgeBytes)
        {
            await db.DocumentCaptureBlobs.Where(b => b.CompanyId == companyId && b.CaptureId == captureId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await AuditTransitionAsync("document.capture.accepted", "success", companyId, capture.PublicId, capture.Kind,
            "accepted", capture.ExtractionGeneration, actorId).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentCaptureView> RejectAsync(int captureId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        if (!await IsCompanyLiveAsync(companyId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var actorId = tenant.UserId;
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DocumentCaptureServiceException(401, "DOCUMENT_UNAUTHORIZED");
        var visibleCapture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(capture => capture.CompanyId == companyId && capture.Id == captureId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(visibleCapture);
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null), cancellationToken)
            .ConfigureAwait(false);
        if (capture is null)
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        EnsureCreatorAccess(capture);
        var access = DocumentCaptureAccessPolicy.AuthorizeExistingWrite(entitlements, companyId, tenant.Permissions, capture.Kind);
        if (access != DocumentCaptureAccessFailure.None)
            throw AccessFailure(access, capture.Kind, companyId);
        if (capture.Status == "rejected")
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
        }
        if (capture.Status is "accepted" or "extracting")
            throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var emptyFields = DocumentCaptureFieldSchemas.EmptyFieldsJsonForKind(capture.Kind);
        var updated = await db.DocumentCaptures.Where(c => c.CompanyId == companyId && c.Id == captureId && c.Status != "accepted" && c.Status != "extracting")
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Status, "rejected")
                .SetProperty(c => c.RejectedBy, actorId)
                .SetProperty(c => c.RejectedAt, now)
                .SetProperty(c => c.BytesDeletedAt, now)
                .SetProperty(c => c.FieldsWipedAt, now)
                .SetProperty(c => c.FieldsJson, emptyFields)
                .SetProperty(c => c.RawText, (string?)null)
                .SetProperty(c => c.TextPageCount, 0)
                .SetProperty(c => c.WarningsJson, (string?)null), cancellationToken).ConfigureAwait(false);
        if (updated != 1)
            throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
        await db.DocumentCaptureBlobs.Where(b => b.CompanyId == companyId && b.CaptureId == captureId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        // The mapped fields hold the same document values the reject just wiped.
        await db.DocumentCaptureAiMappings.IgnoreQueryFilters().Where(job => job.CompanyId == companyId && job.CaptureId == captureId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await AuditTransitionAsync("document.capture.rejected", "success", companyId, capture.PublicId, capture.Kind,
            "rejected", capture.ExtractionGeneration, actorId).ConfigureAwait(false);
        return await GetAsync(captureId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues the optional background field mapping for a capture that just became ready. It never delays or fails the scan: the rule
    /// fields are already stored, and a mapping failure leaves them as they are.
    /// </summary>
    private async Task TryQueueAiMappingAsync(int companyId, int captureId, int generation, string kind, CancellationToken cancellationToken)
    {
        if (!options.Value.AiMapping.Enabled || aiMappingPolicy is null)
            return;
        try
        {
            var contentHash = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Id == captureId).Select(c => c.ContentHash)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (contentHash is null)
                return;
            await CaptureAiMappingQueue.TryEnqueueAsync(db, options, aiMappingPolicy, companyId, captureId, generation, kind, contentHash,
                _timeProvider.GetUtcNow().UtcDateTime, cancellationToken, aiMappingSignal).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning("Document capture AI mapping could not be queued. CaptureId={CaptureId} FailureType={FailureType}", captureId, ex.GetType().Name);
        }
    }

    private async Task<DocumentCaptureView> RunExtractionAsync(
        int companyId,
        int captureId,
        string capturePublicId,
        int generation,
        PreparedCaptureMedia media,
        LocalOcrDocumentFieldExtractor.Admission? ocrAdmission,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new DocumentExtractionRequest(media.Kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion,
                media.ContentType, media.Bytes, media.PageCount);
            var extracted = await ExtractAsync(request, ocrAdmission, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(extracted.FieldsJson)
                || extracted.FieldsJson.Length > DocumentCaptureFieldSchemas.MaxProviderJsonLength
                || extracted.Warnings is null
                || extracted.Warnings.Count > 32
                || extracted.RawText?.Length > DocumentCaptureFieldSchemas.MaxRawTextLength
                || extracted.DetectedKind?.Length > 40
                || extracted.DetectedDocumentCount is < 1 or > 100
                || string.IsNullOrWhiteSpace(extracted.ProviderName)
                || extracted.ProviderName.Length > 100
                || string.IsNullOrWhiteSpace(extracted.ProviderVersion)
                || extracted.ProviderVersion.Length > 100)
                throw new DocumentCaptureServiceException(422, "DOCUMENT_PROVIDER_OUTPUT_INVALID");
            var fieldsJson = DocumentCaptureFieldSchemas.SanitizeProviderFields(media.Kind, extracted.FieldsJson);
            fieldsJson = DocumentCaptureMasking.MaskFinancialFreeText(media.Kind, fieldsJson);
            using var fieldDocument = JsonDocument.Parse(fieldsJson);
            if (media.Kind == "assistant-attachment")
                fieldsJson = DocumentCaptureMasking.MaskAssistantFields(fieldDocument.RootElement);
            using var safeFields = JsonDocument.Parse(fieldsJson);
            if (!DocumentCaptureFieldSchemas.TryValidate(media.Kind, DocumentCaptureFieldSchemas.CurrentSchemaVersion,
                    safeFields.RootElement, out var canonical, out _))
                throw new DocumentCaptureServiceException(422, "DOCUMENT_PROVIDER_OUTPUT_INVALID");
            var warningsJson = JsonSerializer.Serialize(extracted.Warnings.Select(DocumentCaptureWarningCodes.Sanitize).Distinct(StringComparer.Ordinal));
            var rawText = DocumentCaptureMasking.MaskRawText(extracted.RawText);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await using var finalization = await AcquireFinalizationLockAsync(companyId).ConfigureAwait(false);
            if (!await IsCompanyLiveAsync(companyId, CancellationToken.None).ConfigureAwait(false))
            {
                await finalization.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
            }
            var updated = await db.DocumentCaptures.IgnoreQueryFilters()
                .Where(c => c.CompanyId == companyId && c.Id == captureId && c.ExtractionGeneration == generation && c.Status == "extracting")
                .Where(c => db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, "ready")
                    .SetProperty(c => c.FieldsJson, canonical)
                    .SetProperty(c => c.WarningsJson, warningsJson)
                    .SetProperty(c => c.RawText, rawText)
                    .SetProperty(c => c.TextPageCount, string.IsNullOrEmpty(rawText) ? 0 : Math.Max(1, extracted.TextPageCount))
                    .SetProperty(c => c.DetectedKind, extracted.DetectedKind)
                    .SetProperty(c => c.DetectedDocumentCount, extracted.DetectedDocumentCount)
                    .SetProperty(c => c.ProviderName, extracted.ProviderName)
                    .SetProperty(c => c.ProviderVersion, extracted.ProviderVersion)
                    .SetProperty(c => c.ErrorCode, (string?)null)
                    .SetProperty(c => c.LeaseExpiresAt, (DateTime?)null), CancellationToken.None).ConfigureAwait(false);
            if (updated != 1)
            {
                await finalization.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw new DocumentCaptureServiceException(409, "DOCUMENT_STATE_CONFLICT");
            }
            await finalization.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            await AuditTransitionAsync("document.capture.ready", "success", companyId, capturePublicId, media.Kind,
                "ready", generation, tenant.UserId).ConfigureAwait(false);
            await TryQueueAiMappingAsync(companyId, captureId, generation, media.Kind, cancellationToken).ConfigureAwait(false);
        }
        catch (DocumentCaptureServiceException ex) when (ex.ErrorCode is "DOCUMENT_STATE_CONFLICT" or "DOCUMENT_RATE_LIMITED")
        {
            throw;
        }
        catch (DocumentCaptureAdmissionBusyException)
        {
            throw new DocumentCaptureServiceException(503, "DOCUMENT_BUSY");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ocrAdmission is null)
                logger.LogWarning("Document capture extraction failed. CaptureId={CaptureId}, Kind={Kind}, FailureType={FailureType}",
                    captureId, media.Kind, ex.GetType().Name);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await using var finalization = await AcquireFinalizationLockAsync(companyId).ConfigureAwait(false);
            var updated = await db.DocumentCaptures.IgnoreQueryFilters()
                .Where(c => c.CompanyId == companyId && c.Id == captureId && c.ExtractionGeneration == generation && c.Status == "extracting")
                .Where(c => db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null))
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Status, "failed")
                    .SetProperty(c => c.ErrorCode, ex is TimeoutException or OperationCanceledException
                        ? "DOCUMENT_PROVIDER_TIMEOUT"
                        : ex is DocumentCaptureServiceException serviceException
                            ? serviceException.ErrorCode : "DOCUMENT_UNREADABLE")
                    .SetProperty(c => c.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(c => c.ExpiresAt, now.AddHours(options.Value.AbandonedRetentionHours)), CancellationToken.None).ConfigureAwait(false);
            if (updated == 1)
            {
                await finalization.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                await AuditTransitionAsync("document.capture.failed", "failure", companyId, capturePublicId, media.Kind,
                    "failed", generation, tenant.UserId).ConfigureAwait(false);
            }
            else
                await finalization.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            if (ex is DocumentCaptureServiceException { ErrorCode: "DOCUMENT_BUSY" or "DOCUMENT_DECODE_BUSY" or "DOCUMENT_TOO_COMPLEX" })
                throw;
        }

        return await GetAsync(captureId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request,
        LocalOcrDocumentFieldExtractor.Admission? ocrAdmission, CancellationToken cancellationToken)
    {
        // Local OCR owns its deadline so it can kill and reap the worker before returning.
        if (extractor is LocalOcrDocumentFieldExtractor localOcr && ocrAdmission is not null)
            return await localOcr.ExtractAdmittedAsync(request, ocrAdmission, cancellationToken).ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ExtractorDeadline);
        return await extractor.ExtractAsync(request, deadline.Token).WaitAsync(ExtractorDeadline, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedCaptureMedia> PrepareMediaAsync(
        string kind,
        string contentType,
        byte[] source,
        DocumentCaptureOptions config,
        CancellationToken cancellationToken)
    {
        if (source.Length > DocumentCaptureImageLimits.MaxInputBytes)
            throw new DocumentCaptureServiceException(413, "DOCUMENT_TOO_LARGE");
        if (source.AsSpan().StartsWith("%PDF-"u8))
        {
            var validation = await DocumentCapturePdfValidator.ValidateAsync(source, config.PdfParseSlots, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.IsValid)
                throw new DocumentCaptureServiceException(StatusForMediaError(validation.ErrorCode), validation.ErrorCode ?? "DOCUMENT_MALFORMED");
            if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                throw new DocumentCaptureServiceException(400, "DOCUMENT_MEDIA_MISMATCH");
            return new PreparedCaptureMedia(kind, source, "application/pdf", null, null, validation.PageCount ?? 1, "pdf-structure-v1");
        }

        var normalized = await DocumentCaptureImageNormalizer.NormalizeAsync(source, contentType, config.ImageDecodeSlots, cancellationToken).ConfigureAwait(false);
        return new PreparedCaptureMedia(kind, normalized.Bytes, normalized.ContentType,
            normalized.Width, normalized.Height, 1, "image-normalization-v2");
    }

    private async Task CheckAttemptLimitAsync(int companyId, DateTime now, DocumentCaptureOptions config, CancellationToken cancellationToken)
    {
        var hourCount = await db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .CountAsync(attempt => attempt.CompanyId == companyId && attempt.ReservedAt > now.AddHours(-1), cancellationToken).ConfigureAwait(false);
        var dayStart = now.Date;
        var dayCount = await db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .CountAsync(attempt => attempt.CompanyId == companyId && attempt.ReservedAt >= dayStart, cancellationToken).ConfigureAwait(false);
        if (hourCount >= config.MaxAttemptsPerHour || dayCount >= config.MaxAttemptsPerDay)
            throw new DocumentCaptureServiceException(429, "DOCUMENT_RATE_LIMITED");
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> AcquireFinalizationLockAsync(int companyId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await admissionLock.AcquireCompanyAsync(companyId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (DocumentCaptureAdmissionBusyException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), _timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    internal static IQueryable<DocumentCapture> LiveCompanyCaptures(ApiDbContext db, int companyId)
    {
        var liveCompany = LiveCompany(db, companyId);
        return db.DocumentCaptures.AsNoTracking()
            .Where(capture => capture.CompanyId == companyId && liveCompany.Any());
    }

    private Task<bool> IsCompanyLiveAsync(int companyId, CancellationToken cancellationToken) =>
        LiveCompany(db, companyId).AnyAsync(cancellationToken);

    private static IQueryable<Company> LiveCompany(ApiDbContext db, int companyId) =>
        db.Companies.IgnoreQueryFilters().Where(company => company.Id == companyId && company.DeletedAt == null);

    private async Task<int> ResolveTargetAsync(string publicId, string expectedPrefix, CancellationToken cancellationToken)
    {
        var lookup = await publicIds.ResolveAsync(publicId, expectedPrefix, cancellationToken).ConfigureAwait(false);
        if (!lookup.Ok)
        {
            if (lookup.Error == PublicIdLookupError.NotFound)
                throw new PublicIdSerializationException(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
            throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, "The identifier is invalid.");
        }
        return lookup.Id;
    }

    private async Task EnsureLinkedTargetReadAsync(DocumentCapture capture, int companyId, CancellationToken cancellationToken)
    {
        var link = await db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.CaptureId == capture.Id
                && item.Role == DocumentCaptureLinkRoles.Source, cancellationToken)
            .ConfigureAwait(false);
        if (link is null)
            return;

        if (!await CanReadTargetAsync(companyId, link.TargetType, link.TargetId, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
    }

    private async Task<bool> CanReadCandidateAsync(DocumentCapture capture, int companyId, string actorId,
        CancellationToken cancellationToken)
    {
        if (capture.Kind == "assistant-attachment" && !string.Equals(capture.CreatedBy, actorId, StringComparison.Ordinal))
            return false;
        if (DocumentCaptureAccessPolicy.AuthorizeRead(entitlements, companyId, tenant.Permissions, capture.Kind)
            != DocumentCaptureAccessFailure.None)
            return false;
        var link = await db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.CaptureId == capture.Id
                && item.Role == DocumentCaptureLinkRoles.Source, cancellationToken)
            .ConfigureAwait(false);
        if (link is null)
            return true;
        return await CanReadTargetAsync(companyId, link.TargetType, link.TargetId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CanReadTargetAsync(int companyId, string targetType, int targetId, CancellationToken cancellationToken) =>
        await ResolveReadableTargetPublicIdAsync(companyId, targetType, targetId, cancellationToken).ConfigureAwait(false) is not null;

    private async Task<string?> ResolveReadableTargetPublicIdAsync(int companyId, string targetType, int targetId,
        CancellationToken cancellationToken)
    {
        var targetPublicId = targetType switch
        {
            "bill" when tenant.Permissions.Contains(SystemPermissions.PurchasesBillsView) =>
                await db.PurchaseInvoiceHeaders.AsNoTracking()
                    .Where(target => target.CompanyId == companyId && target.Id == targetId)
                    .Select(target => target.PublicId)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false),
            "invoice" when tenant.Permissions.Contains(SystemPermissions.SalesInvoicesView) =>
                await db.SalesInvoiceHeaders.AsNoTracking()
                    .Where(target => target.CompanyId == companyId && target.Id == targetId)
                    .Select(target => target.PublicId)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false),
            "customer" when tenant.Permissions.Contains(SystemPermissions.SalesCustomersView) =>
                await db.Customers.AsNoTracking()
                    .Where(target => target.CompanyId == companyId && target.Id == targetId)
                    .Select(target => target.PublicId)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false),
            "vendor" when tenant.Permissions.Contains(SystemPermissions.PurchasesVendorsView) =>
                await db.Vendors.AsNoTracking()
                    .Where(target => target.CompanyId == companyId && target.Id == targetId)
                    .Select(target => target.PublicId)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false),
            _ => null
        };
        return string.IsNullOrWhiteSpace(targetPublicId) ? null : targetPublicId;
    }

    private async Task<DocumentCaptureLinkView?> GetLinkViewAsync(DocumentCapture capture, int companyId, CancellationToken cancellationToken)
    {
        var link = await db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.CaptureId == capture.Id
                && item.Role == DocumentCaptureLinkRoles.Source, cancellationToken)
            .ConfigureAwait(false);
        if (link is null)
            return null;

        var targetPublicId = await ResolveReadableTargetPublicIdAsync(companyId, link.TargetType, link.TargetId, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(targetPublicId))
            throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        return new DocumentCaptureLinkView(
            link.TargetType,
            targetPublicId,
            DateTime.SpecifyKind(link.LinkedAt, DateTimeKind.Utc),
            link.TaxTreatment,
            link.Role);
    }

    private async Task<IReadOnlyList<DocumentCaptureLinkView>> GetReadableLinkViewsAsync(
        int captureId,
        int companyId,
        CancellationToken cancellationToken)
    {
        var byCapture = await GetReadableLinkViewsByCaptureAsync([captureId], companyId, knownTarget: null, cancellationToken)
            .ConfigureAwait(false);
        return byCapture.TryGetValue(captureId, out var views) ? views : Array.Empty<DocumentCaptureLinkView>();
    }

    private async Task<Dictionary<int, IReadOnlyList<DocumentCaptureLinkView>>> GetReadableLinkViewsByCaptureAsync(
        IReadOnlyCollection<int> captureIds,
        int companyId,
        (string TargetType, int TargetId, string PublicId)? knownTarget,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<DocumentCaptureLinkView>>();
        if (captureIds.Count == 0)
            return result;

        var rawLinks = await db.DocumentCaptureLinks.AsNoTracking()
            .Where(item => item.CompanyId == companyId && captureIds.Contains(item.CaptureId))
            .OrderBy(item => item.LinkedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rawLinks.Count == 0)
            return result;

        var resolvedPublicIds = new Dictionary<(string TargetType, int TargetId), string>();
        if (knownTarget is { } known && !string.IsNullOrWhiteSpace(known.PublicId))
            resolvedPublicIds[(known.TargetType, known.TargetId)] = known.PublicId;

        var unresolvedByType = rawLinks
            .Select(link => (link.TargetType, link.TargetId))
            .Where(key => !resolvedPublicIds.ContainsKey(key))
            .Distinct()
            .GroupBy(key => key.TargetType, key => key.TargetId, StringComparer.Ordinal);

        foreach (var group in unresolvedByType)
        {
            var ids = group.ToArray();
            switch (group.Key)
            {
                case "bill" when tenant.Permissions.Contains(SystemPermissions.PurchasesBillsView):
                    foreach (var row in await db.PurchaseInvoiceHeaders.AsNoTracking()
                                 .Where(target => target.CompanyId == companyId && ids.Contains(target.Id))
                                 .Select(target => new { target.Id, target.PublicId })
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrWhiteSpace(row.PublicId))
                            resolvedPublicIds[("bill", row.Id)] = row.PublicId;
                    }
                    break;
                case "invoice" when tenant.Permissions.Contains(SystemPermissions.SalesInvoicesView):
                    foreach (var row in await db.SalesInvoiceHeaders.AsNoTracking()
                                 .Where(target => target.CompanyId == companyId && ids.Contains(target.Id))
                                 .Select(target => new { target.Id, target.PublicId })
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrWhiteSpace(row.PublicId))
                            resolvedPublicIds[("invoice", row.Id)] = row.PublicId;
                    }
                    break;
                case "customer" when tenant.Permissions.Contains(SystemPermissions.SalesCustomersView):
                    foreach (var row in await db.Customers.AsNoTracking()
                                 .Where(target => target.CompanyId == companyId && ids.Contains(target.Id))
                                 .Select(target => new { target.Id, target.PublicId })
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrWhiteSpace(row.PublicId))
                            resolvedPublicIds[("customer", row.Id)] = row.PublicId;
                    }
                    break;
                case "vendor" when tenant.Permissions.Contains(SystemPermissions.PurchasesVendorsView):
                    foreach (var row in await db.Vendors.AsNoTracking()
                                 .Where(target => target.CompanyId == companyId && ids.Contains(target.Id))
                                 .Select(target => new { target.Id, target.PublicId })
                                 .ToListAsync(cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        if (!string.IsNullOrWhiteSpace(row.PublicId))
                            resolvedPublicIds[("vendor", row.Id)] = row.PublicId;
                    }
                    break;
            }
        }

        foreach (var captureGroup in rawLinks.GroupBy(item => item.CaptureId))
        {
            var views = new List<DocumentCaptureLinkView>();
            foreach (var item in captureGroup)
            {
                if (!resolvedPublicIds.TryGetValue((item.TargetType, item.TargetId), out var targetPublicId))
                    continue;
                views.Add(new DocumentCaptureLinkView(
                    item.TargetType,
                    targetPublicId,
                    DateTime.SpecifyKind(item.LinkedAt, DateTimeKind.Utc),
                    item.Role == DocumentCaptureLinkRoles.Source ? item.TaxTreatment : null,
                    item.Role));
            }
            result[captureGroup.Key] = views;
        }

        return result;
    }

    private void EnsureCreatorAccess(DocumentCapture capture)
    {
        if (capture.Kind == "assistant-attachment" && !string.Equals(capture.CreatedBy, tenant.UserId, StringComparison.Ordinal))
            throw new PublicIdSerializationException(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
    }

    private DocumentCaptureServiceException AccessFailure(DocumentCaptureAccessFailure failure, string kind, int companyId) => failure switch
    {
        DocumentCaptureAccessFailure.FeatureRequired or DocumentCaptureAccessFailure.ModuleRequired =>
            new(403, "ENTITLEMENT_DENIED", entitlementDecision: DocumentCaptureAccessPolicy.GetEntitlementDecision(entitlements, companyId, kind, failure),
                requiredModules: failure == DocumentCaptureAccessFailure.ModuleRequired ? DocumentCaptureAccessPolicy.GetRequiredModules(kind) : null),
        DocumentCaptureAccessFailure.PermissionRequired => new(403, "DOCUMENT_PERMISSION_REQUIRED"),
        _ => new(404, "DOCUMENT_NOT_FOUND")
    };

    private Task AuditTransitionAsync(string eventType, string outcome, int companyId, string capturePublicId,
        string kind, string status, int generation, string? actorId) => securityAudit is null
        ? Task.CompletedTask
        : securityAudit.LogEventAsync(eventType, outcome, actorId, companyId,
            new { captureId = capturePublicId, kind, status, generation });

    private static int StatusForMediaError(string? errorCode) => errorCode switch
    {
        "DOCUMENT_EMPTY" or "DOCUMENT_MALFORMED" or "DOCUMENT_DIMENSIONS" => 400,
        "DOCUMENT_TOO_LARGE" => 413,
        "DOCUMENT_UNSUPPORTED_TYPE" => 415,
        "DOCUMENT_BUSY" => 503,
        "DOCUMENT_DECODE_BUSY" or "DOCUMENT_RATE_LIMITED" => 429,
        _ => 422
    };

    private static DocumentCaptureView ToView(DocumentCapture capture, bool bytesAvailable, bool canWrite, DocumentCaptureLinkView? link,
        DocumentCaptureAiMappingView? aiMapping = null, IReadOnlyList<DocumentCaptureLinkView>? links = null) => new(
        capture.PublicId, capture.Kind, capture.SchemaVersion, capture.Status, capture.ContentType, capture.ByteCount,
        capture.Width, capture.Height, capture.PageCount,
        capture.Status == "accepted" ? null : ParseJson(capture.FieldsJson),
        ParseJson(capture.AcceptedFieldsJson), ParseJson(capture.WarningsJson),
        capture.ErrorCode, bytesAvailable,
        canWrite && capture.Status == "ready" && bytesAvailable,
        canWrite && capture.Status == "accepted" && bytesAvailable
            && (capture.Kind is "purchase-invoice" or "expense-receipt" or "sales-invoice"),
        capture.ProviderName, capture.ProviderVersion, capture.SourceHash, capture.ContentHash, capture.NormalizationVersion, link,
        DateTime.SpecifyKind(capture.CreatedAt, DateTimeKind.Utc),
        capture.AcceptedAt is { } accepted ? DateTime.SpecifyKind(accepted, DateTimeKind.Utc) : null,
        capture.RejectedAt is { } rejected ? DateTime.SpecifyKind(rejected, DateTimeKind.Utc) : null,
        aiMapping,
        links);

    private static JToken? ParseJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        return JToken.Parse(json);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PreparedCaptureMedia(
        string Kind,
        byte[] Bytes,
        string ContentType,
        int? Width,
        int? Height,
        int PageCount,
        string NormalizationVersion);
}

public sealed class DocumentCaptureServiceException(int statusCode, string errorCode, string? captureId = null,
    EntitlementDecision? entitlementDecision = null, IReadOnlyList<string>? requiredModules = null) : Exception(errorCode)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string? CaptureId { get; } = captureId;
    public EntitlementDecision? EntitlementDecision { get; } = entitlementDecision;
    public IReadOnlyList<string>? RequiredModules { get; } = requiredModules;
}
