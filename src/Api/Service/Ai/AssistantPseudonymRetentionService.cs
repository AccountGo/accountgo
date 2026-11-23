// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Service.Ai;

/// <summary>
/// Budgeted TTL purge for expired <c>AssistantPseudonyms</c> rows (30 days after <c>LastUsedAt</c>).
/// </summary>
public sealed class AssistantPseudonymRetentionService(
    ApiDbContext db,
    TimeProvider? timeProvider = null)
{
    public const int BatchSize = 500;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan PassBudget = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) =>
        PurgeExpiredAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        var passStarted = _timeProvider.GetTimestamp();

        while (WithinPassBudget(passStarted) && !cancellationToken.IsCancellationRequested)
        {
            var expiredIds = await db.AssistantPseudonyms.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.ExpiresAt <= utcNow)
                .OrderBy(row => row.ExpiresAt)
                .ThenBy(row => row.Id)
                .Select(row => row.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (expiredIds.Count == 0)
            {
                break;
            }

            var deleted = await db.AssistantPseudonyms.IgnoreQueryFilters()
                .Where(row => expiredIds.Contains(row.Id) && row.ExpiresAt <= utcNow)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            totalDeleted += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    private bool WithinPassBudget(long passStarted) =>
        _timeProvider.GetElapsedTime(passStarted) < PassBudget;
}
