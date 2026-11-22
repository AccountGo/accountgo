// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.DocumentCaptures;

public sealed class DocumentCaptureRetentionService(ApiDbContext db, DocumentCaptureAdmissionLock admissionLock, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
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
                purgedCaptures += await PurgeDeletedCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
                if (await HasDeletedCompanyResidueAsync(companyId, cancellationToken).ConfigureAwait(false))
                    stalledDeletedCompanies.Add(companyId);
            }
        }

        var stalledAbandonedCaptures = new HashSet<int>();
        while (WithinPassBudget(passStarted))
        {
            var abandoned = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .Where(capture => capture.ExpiresAt <= utcNow && capture.AcceptedAt == null && capture.RejectedAt == null
                    && capture.BytesDeletedAt == null && !stalledAbandonedCaptures.Contains(capture.Id))
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
                if (await PurgeAbandonedBytesAsync(item.CompanyId, item.Id, utcNow, cancellationToken).ConfigureAwait(false))
                    madeProgress = true;
                else
                    stalledAbandonedCaptures.Add(item.Id);
            }
            if (!madeProgress && abandoned.All(item => stalledAbandonedCaptures.Contains(item.Id)))
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
                if (await PurgeOldAttemptsAsync(companyId, utcNow - AttemptRetention, cancellationToken).ConfigureAwait(false) > 0)
                    madeProgress = true;
                else
                    stalledAttemptCompanies.Add(companyId);
            }
            if (!madeProgress && oldAttemptsByCompany.All(stalledAttemptCompanies.Contains))
                break;
        }

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

        var deleted = await db.DocumentCaptures.IgnoreQueryFilters()
            .Where(capture => capture.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return deleted;
    }

    private bool WithinPassBudget(long passStarted) => _timeProvider.GetElapsedTime(passStarted) < PassBudget;

    private async Task<bool> HasDeletedCompanyResidueAsync(int companyId, CancellationToken cancellationToken) =>
        await db.DocumentCaptures.IgnoreQueryFilters().AnyAsync(capture => capture.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
        || await db.DocumentCaptureAttempts.IgnoreQueryFilters().AnyAsync(attempt => attempt.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
        || await db.DocumentCaptureBlobs.IgnoreQueryFilters().AnyAsync(blob => blob.CompanyId == companyId, cancellationToken).ConfigureAwait(false);

    private async Task<bool> PurgeAbandonedBytesAsync(int companyId, int captureId, DateTime utcNow, CancellationToken cancellationToken)
    {
        await using var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        var capture = await db.DocumentCaptures.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == captureId
                && item.ExpiresAt <= utcNow && item.AcceptedAt == null && item.RejectedAt == null
                && item.BytesDeletedAt == null
                && (item.Status != "extracting" || item.LeaseExpiresAt == null || item.LeaseExpiresAt <= utcNow)
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null),
                cancellationToken).ConfigureAwait(false);
        if (capture is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await db.DocumentCaptureBlobs.IgnoreQueryFilters()
            .Where(blob => blob.CompanyId == companyId && blob.CaptureId == captureId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        capture.BytesDeletedAt = utcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
