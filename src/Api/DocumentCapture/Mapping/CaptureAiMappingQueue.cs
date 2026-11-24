// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.DocumentCaptures.Parsing;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures.Mapping;

/// <summary>Queues a background mapping job when a capture becomes ready. Does nothing unless AI mapping is on and the workspace allows it.</summary>
public static class CaptureAiMappingQueue
{
    public static async Task<bool> TryEnqueueAsync(
        ApiDbContext db,
        IOptions<DocumentCaptureOptions> options,
        ICaptureAiMappingPolicy? policy,
        int companyId,
        int captureId,
        int generation,
        string kind,
        string contentHash,
        DateTime now,
        CancellationToken cancellationToken,
        CaptureAiMappingSignal? signal = null)
    {
        if (!options.Value.AiMapping.Enabled || policy is null || !CaptureMappingRequestFactory.IsMappableKind(kind))
            return false;
        if (!await policy.IsAllowedAsync(companyId, kind, cancellationToken).ConfigureAwait(false))
            return false;

        var exists = await db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(job => job.CompanyId == companyId && job.CaptureId == captureId && job.ExtractionGeneration == generation, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return false;
        db.DocumentCaptureAiMappings.Add(new DocumentCaptureAiMapping
        {
            CompanyId = companyId,
            CaptureId = captureId,
            ExtractionGeneration = generation,
            Status = "queued",
            Kind = kind,
            ContentHash = contentHash,
            RulesVersion = DocumentTextFieldParser.RulesVersion,
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            signal?.Notify();
            return true;
        }
        catch (DbUpdateException)
        {
            // Another request enqueued the same extraction first; the unique key keeps it to one job.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
