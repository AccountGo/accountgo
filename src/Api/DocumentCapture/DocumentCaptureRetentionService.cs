// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureRetentionService(ApiDbContext db, IDocumentCaptureAdmissionLock admissionLock,
    TimeProvider? timeProvider = null, ILogger<DocumentCaptureRetentionService>? logger = null,
    Microsoft.Extensions.Options.IOptions<DocumentsOptions>? documentsOptions = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<DocumentCaptureRetentionService> _logger = logger ?? NullLogger<DocumentCaptureRetentionService>.Instance;
    public const int BatchSize = 100;
    public static readonly TimeSpan AttemptRetention = TimeSpan.FromHours(48);
    private static readonly TimeSpan PassBudget = TimeSpan.FromMinutes(2);

    public Task<int> PurgeAsync(CancellationToken cancellationToken) =>
        PurgeAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var workerLock = await admissionLock.TryAcquirePurgeWorkerSingletonAsync(cancellationToken).ConfigureAwait(false);
        if (workerLock is null)
            return 0;
        await using var workerLease = workerLock;

        var purgedCaptures = 0;
        var passStarted = _timeProvider.GetTimestamp();
        var stalledDeletedCompanies = new HashSet<int>();
        var busyItems = 0;
        while (WithinPassBudget(passStarted))
        {
            var deletedCompanyIds = await db.Companies.IgnoreQueryFilters()
                .Where(company => company.DeletedAt != null
                    && !stalledDeletedCompanies.Contains(company.Id)
                    && (db.DocumentCaptures.IgnoreQueryFilters().Any(capture => capture.CompanyId == company.Id)
                        || db.DocumentCaptureAttempts.IgnoreQueryFilters().Any(attempt => attempt.CompanyId == company.Id)
                        || db.DocumentCaptureBlobs.IgnoreQueryFilters().Any(blob => blob.CompanyId == company.Id)))
                .OrderBy(company => company.Id)
                .Select(company => company.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (deletedCompanyIds.Count == 0)
                break;
            foreach (var companyId in deletedCompanyIds)
            {
                try
                {
                    purgedCaptures += await PurgeDeletedCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
                }
                catch (DocumentCaptureAdmissionBusyException)
                {
                    stalledDeletedCompanies.Add(companyId);
                    busyItems++;
                    continue;
                }
                if (await HasDeletedCompanyResidueAsync(companyId, cancellationToken).ConfigureAwait(false))
                    stalledDeletedCompanies.Add(companyId);
            }
        }

        var stalledAbandonedCaptures = new HashSet<int>();
        while (WithinPassBudget(passStarted))
        {
            var abandoned = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .Where(capture => capture.ExpiresAt <= utcNow && capture.AcceptedAt == null && capture.RejectedAt == null
                    && capture.FiledAt == null
                    && (capture.BytesDeletedAt == null || capture.FieldsWipedAt == null)
                    && !stalledAbandonedCaptures.Contains(capture.Id))
                .OrderBy(capture => capture.ExpiresAt)
                .Select(capture => new { capture.CompanyId, capture.Id })
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (abandoned.Count == 0)
                break;
            var madeProgress = false;
            foreach (var item in abandoned)
            {
                if (!WithinPassBudget(passStarted))
                    break;
                try
                {
                    if (await PurgeAbandonedContentAsync(item.CompanyId, item.Id, utcNow, cancellationToken).ConfigureAwait(false))
                        madeProgress = true;
                    else
                        stalledAbandonedCaptures.Add(item.Id);
                }
                catch (DocumentCaptureAdmissionBusyException)
                {
                    stalledAbandonedCaptures.Add(item.Id);
                    busyItems++;
                }
            }
            if (!madeProgress && abandoned.All(item => stalledAbandonedCaptures.Contains(item.Id)))
                break;
        }

        var stalledRejectedCaptures = new HashSet<int>();
        while (WithinPassBudget(passStarted))
        {
            var rejectedResidue = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .Where(capture => capture.AcceptedAt == null && capture.RejectedAt != null
                    && capture.FiledAt == null
                    && (capture.BytesDeletedAt == null || capture.FieldsWipedAt == null)
                    && !stalledRejectedCaptures.Contains(capture.Id))
                .OrderBy(capture => capture.RejectedAt)
                .Select(capture => new { capture.CompanyId, capture.Id })
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rejectedResidue.Count == 0)
                break;
            var madeProgress = false;
            foreach (var item in rejectedResidue)
            {
                if (!WithinPassBudget(passStarted))
                    break;
                try
                {
                    if (await PurgeRejectedResidueAsync(item.CompanyId, item.Id, utcNow, cancellationToken).ConfigureAwait(false))
                        madeProgress = true;
                    else
                        stalledRejectedCaptures.Add(item.Id);
                }
                catch (DocumentCaptureAdmissionBusyException)
                {
                    stalledRejectedCaptures.Add(item.Id);
                    busyItems++;
                }
            }
            if (!madeProgress && rejectedResidue.All(item => stalledRejectedCaptures.Contains(item.Id)))
                break;
        }

        // Library trash older than the retention period: purge the row, blob and links (design 3.9 S4, R5).
        var trashCutoff = utcNow.AddDays(-(documentsOptions?.Value.TrashRetentionDays ?? 30));
        var stalledTrash = new HashSet<int>();
        while (WithinPassBudget(passStarted))
        {
            var expiredTrash = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .Where(capture => capture.FiledAt != null && capture.DeletedAt != null && capture.DeletedAt <= trashCutoff
                    && !stalledTrash.Contains(capture.Id))
                .OrderBy(capture => capture.DeletedAt)
                .Select(capture => new { capture.CompanyId, capture.Id })
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (expiredTrash.Count == 0)
                break;
            var madeProgress = false;
            foreach (var item in expiredTrash)
            {
                if (!WithinPassBudget(passStarted))
                    break;
                try
                {
                    if (await PurgeExpiredTrashAsync(item.CompanyId, item.Id, trashCutoff, cancellationToken).ConfigureAwait(false))
                    {
                        madeProgress = true;
                        purgedCaptures++;
                    }
                    else
                    {
                        stalledTrash.Add(item.Id);
                    }
                }
                catch (DocumentCaptureAdmissionBusyException)
                {
                    stalledTrash.Add(item.Id);
                    busyItems++;
                }
            }
            if (!madeProgress && expiredTrash.All(item => stalledTrash.Contains(item.Id)))
                break;
        }

        var stalledAttemptCompanies = new HashSet<int>();
        while (WithinPassBudget(passStarted))
        {
            var oldAttemptsByCompany = await db.DocumentCaptureAttempts.IgnoreQueryFilters().AsNoTracking()
                .Where(attempt => attempt.ReservedAt <= utcNow - AttemptRetention
                    && !stalledAttemptCompanies.Contains(attempt.CompanyId))
                .Select(attempt => attempt.CompanyId)
                .Distinct()
                .OrderBy(companyId => companyId)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (oldAttemptsByCompany.Count == 0)
                break;
            var madeProgress = false;
            foreach (var companyId in oldAttemptsByCompany)
            {
                if (!WithinPassBudget(passStarted))
                    break;
                try
                {
                    if (await PurgeOldAttemptsAsync(companyId, utcNow - AttemptRetention, cancellationToken).ConfigureAwait(false) > 0)
                        madeProgress = true;
                    else
                        stalledAttemptCompanies.Add(companyId);
                }
                catch (DocumentCaptureAdmissionBusyException)
                {
                    stalledAttemptCompanies.Add(companyId);
                    busyItems++;
                }
            }
            if (!madeProgress && oldAttemptsByCompany.All(stalledAttemptCompanies.Contains))
                break;
        }

        if (busyItems > 0)
            _logger.LogInformation("Document capture retention pass skipped {BusyItems} locked items.", busyItems);
        return purgedCaptures;
    }

    public async Task<int> PurgeDeletedCompanyAsync(int companyId, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquirePurgeCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        if (!await db.Companies.IgnoreQueryFilters().AnyAsync(company => company.Id == companyId && company.DeletedAt != null, cancellationToken)
                .ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var hadFiledCaptures = await db.DocumentCaptures.IgnoreQueryFilters()
            .AnyAsync(capture => capture.CompanyId == companyId && capture.FiledAt != null, cancellationToken)
            .ConfigureAwait(false);
        var deleted = await db.DocumentCaptures.IgnoreQueryFilters()
            .Where(capture => capture.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (hadFiledCaptures)
            await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return deleted;
    }

    private bool WithinPassBudget(long passStarted) => _timeProvider.GetElapsedTime(passStarted) < PassBudget;

    private async Task<bool> HasDeletedCompanyResidueAsync(int companyId, CancellationToken cancellationToken) =>
        await db.DocumentCaptures.IgnoreQueryFilters().AnyAsync(capture => capture.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
        || await db.DocumentCaptureAttempts.IgnoreQueryFilters().AnyAsync(attempt => attempt.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
        || await db.DocumentCaptureBlobs.IgnoreQueryFilters().AnyAsync(blob => blob.CompanyId == companyId, cancellationToken).ConfigureAwait(false);

    private async Task<bool> PurgeAbandonedContentAsync(int companyId, int captureId, DateTime utcNow, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && item.ExpiresAt <= utcNow && item.AcceptedAt == null && item.RejectedAt == null
                && item.FiledAt == null
                && (item.BytesDeletedAt == null || item.FieldsWipedAt == null)
                && (item.Status != "extracting" || item.LeaseExpiresAt == null || item.LeaseExpiresAt <= utcNow)
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null),
                cancellationToken).ConfigureAwait(false);
        if (capture is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await WipeUnfiledCaptureContentAsync(capture.Kind, companyId, captureId, utcNow, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task<bool> PurgeRejectedResidueAsync(int companyId, int captureId, DateTime utcNow, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && item.AcceptedAt == null && item.RejectedAt != null
                && item.FiledAt == null
                && (item.BytesDeletedAt == null || item.FieldsWipedAt == null)
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null),
                cancellationToken).ConfigureAwait(false);
        if (capture is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await WipeUnfiledCaptureContentAsync(capture.Kind, companyId, captureId, utcNow, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task<bool> WipeUnfiledCaptureContentAsync(string kind, int companyId, int captureId,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        var emptyFields = DocumentCaptureFieldSchemas.EmptyFieldsJsonForKind(kind);
        var updated = await db.DocumentCaptures.IgnoreQueryFilters()
            .Where(item => item.CompanyId == companyId && item.Id == captureId && item.FiledAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.BytesDeletedAt, item => item.BytesDeletedAt ?? utcNow)
                .SetProperty(item => item.FieldsWipedAt, utcNow)
                .SetProperty(item => item.FieldsJson, emptyFields)
                .SetProperty(item => item.RawText, (string?)null)
                .SetProperty(item => item.TextPageCount, 0)
                .SetProperty(item => item.WarningsJson, (string?)null), cancellationToken)
            .ConfigureAwait(false);
        if (updated == 0)
            return false;

        await db.DocumentCaptureBlobs.IgnoreQueryFilters()
            .Where(blob => blob.CompanyId == companyId && blob.CaptureId == captureId
                && db.DocumentCaptures.IgnoreQueryFilters()
                    .Any(item => item.CompanyId == companyId && item.Id == captureId && item.FiledAt == null))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        // The mapped fields and hints hold the same document values as the fields wiped below, for every extraction generation.
        await db.DocumentCaptureAiMappings.IgnoreQueryFilters()
            .Where(job => job.CompanyId == companyId && job.CaptureId == captureId
                && db.DocumentCaptures.IgnoreQueryFilters()
                    .Any(item => item.CompanyId == companyId && item.Id == captureId && item.FiledAt == null))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Under the company lock and the linked records' row locks: purges a trashed library file whose retention has passed, unless
    /// it was restored, is a scan's source, or a linked bill/invoice is no longer a draft (it then stays in the trash as evidence).
    /// </summary>
    private async Task<bool> PurgeExpiredTrashAsync(int companyId, int captureId, DateTime trashCutoff, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var links = await db.DocumentCaptureLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.CompanyId == companyId && link.CaptureId == captureId)
            .Select(link => new { link.TargetType, link.TargetId, link.Role })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (links.Any(link => link.Role == Core.Domain.DocumentCaptureLinkRoles.Source))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        await DocumentTargetLocks.LockAsync(db, companyId, links.Select(link => (link.TargetType, link.TargetId)), cancellationToken)
            .ConfigureAwait(false);
        var billIds = links.Where(link => link.TargetType == "bill").Select(link => link.TargetId).ToArray();
        var invoiceIds = links.Where(link => link.TargetType == "invoice").Select(link => link.TargetId).ToArray();
        var locked = await db.PurchaseInvoiceHeaders.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(h => h.CompanyId == companyId && billIds.Contains(h.Id)
                    && ((h.Status != null && h.Status != Core.Domain.PurchaseInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null), cancellationToken)
                .ConfigureAwait(false)
            || await db.SalesInvoiceHeaders.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(h => h.CompanyId == companyId && invoiceIds.Contains(h.Id)
                    && ((h.Status != null && h.Status != Core.Domain.SalesInvoiceStatus.Draft) || h.GeneralLedgerHeaderId != null), cancellationToken)
                .ConfigureAwait(false);
        if (locked)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        var deleted = await db.DocumentCaptures.IgnoreQueryFilters()
            .Where(capture => capture.CompanyId == companyId && capture.Id == captureId && capture.FiledAt != null
                && capture.DeletedAt != null && capture.DeletedAt <= trashCutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (deleted == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        await DocumentRevision.BumpAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task<int> PurgeOldAttemptsAsync(int companyId, DateTime cutoff, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var deleted = await db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.CompanyId == companyId && attempt.ReservedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }
}
