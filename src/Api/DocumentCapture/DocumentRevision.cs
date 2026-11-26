// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.DocumentCaptures;

/// <summary>
/// Tracks per-workspace document library revisions so exports can detect any filing, link, metadata, or purge
/// change that occurred during or after selection. All writes go through <see cref="BumpAsync"/> inside the
/// caller's active company-locked transaction.
/// </summary>
public static class DocumentRevision
{
    public static async Task BumpAsync(ApiDbContext db, int companyId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new ArgumentOutOfRangeException(nameof(companyId));

        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.Ordinal))
        {
            var updated = await db.Database.ExecuteSqlRawAsync(
                "UPDATE [DocumentLibraryRevisions] SET [Revision] = [Revision] + 1 WHERE [CompanyId] = {0}",
                [companyId],
                cancellationToken).ConfigureAwait(false);
            if (updated == 0)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO [DocumentLibraryRevisions] ([CompanyId], [Revision]) VALUES ({0}, 1)",
                    [companyId],
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "DocumentLibraryRevisions" ("CompanyId", "Revision")
                VALUES ({0}, 1)
                ON CONFLICT ("CompanyId") DO UPDATE
                SET "Revision" = "DocumentLibraryRevisions"."Revision" + 1
                """,
                [companyId],
                cancellationToken).ConfigureAwait(false);
        }

        var tracked = db.DocumentLibraryRevisions.Local.FirstOrDefault(r => r.CompanyId == companyId);
        if (tracked is not null)
            db.Entry(tracked).State = EntityState.Detached;
    }

    public static async Task<long> GetCurrentAsync(ApiDbContext db, int companyId, CancellationToken cancellationToken = default) =>
        await db.DocumentLibraryRevisions.IgnoreQueryFilters().AsNoTracking()
            .Where(revision => revision.CompanyId == companyId)
            .Select(revision => (long?)revision.Revision)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L;
}

internal static class DocumentFiling
{
    public static bool IsFilableKind(string kind) =>
        kind is "expense-receipt" or "purchase-invoice" or "sales-invoice";

    public static void EnsureFilableKind(string kind)
    {
        if (!IsFilableKind(kind))
            throw new DocumentCaptureServiceException(400, "DOCUMENT_LINK_INVALID");
    }

    public static Task<int> FileIfUnfiledAsync(
        ApiDbContext db,
        int companyId,
        int captureId,
        string kind,
        string actorId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        EnsureFilableKind(kind);
        return db.DocumentCaptures.IgnoreQueryFilters()
            .Where(item => item.CompanyId == companyId && item.Id == captureId && item.FiledAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.FiledAt, utcNow)
                .SetProperty(item => item.FiledBy, actorId), cancellationToken);
    }
}
