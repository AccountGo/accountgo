// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.DocumentCaptures;

/// <summary>
/// Lease timings for a streaming export. A live stream renews every <see cref="RenewEvery"/> on its own connection, cancels its
/// own output after <see cref="SelfFenceAfter"/> without a successful renewal, and only after <see cref="Lease"/> without one may
/// another request expire it. Because <see cref="SelfFenceAfter"/> &lt; <see cref="Lease"/>, a live process stops writing an
/// export before a successor can start.
/// </summary>
public sealed record DocumentExportLeaseSettings(TimeSpan RenewEvery, TimeSpan SelfFenceAfter, TimeSpan Lease)
{
    public static DocumentExportLeaseSettings Default { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
}

public enum DocumentExportLeaseRenewal
{
    /// <summary>The heartbeat moved forward to the attempt's time.</summary>
    Renewed,
    /// <summary>Still running, but a later renewal already recorded a newer heartbeat; nothing changed.</summary>
    AlreadyNewer,
    /// <summary>The run is no longer running (expired or finished elsewhere): the stream must stop.</summary>
    Fenced
}

/// <summary>Renews an export's lease on a connection of its own, so renewal never shares the streaming request's DbContext.</summary>
public interface IDocumentExportLease
{
    /// <summary>
    /// Moves the heartbeat forward to <paramref name="now"/> only (never backwards), so a late, abandoned attempt can't make a
    /// live lease look older than it is.
    /// </summary>
    Task<DocumentExportLeaseRenewal> RenewAsync(int companyId, int runId, DateTime now, CancellationToken cancellationToken);
}

public sealed class ScopedDocumentExportLease(IServiceScopeFactory scopes) : IDocumentExportLease
{
    public async Task<DocumentExportLeaseRenewal> RenewAsync(int companyId, int runId, DateTime now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.Impersonate(companyId);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return await RenewAsync(db, companyId, runId, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The single renewal statement: monotonic, conditional on the run still running.</summary>
    public static async Task<DocumentExportLeaseRenewal> RenewAsync(ApiDbContext db, int companyId, int runId, DateTime now,
        CancellationToken cancellationToken)
    {
        var renewed = await db.DocumentExportRuns
            .Where(r => r.CompanyId == companyId && r.Id == runId && r.Status == DocumentExportService.Running
                && (r.LastHeartbeatAt == null || r.LastHeartbeatAt < now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeatAt, now), cancellationToken).ConfigureAwait(false);
        if (renewed == 1)
            return DocumentExportLeaseRenewal.Renewed;
        var stillRunning = await db.DocumentExportRuns.AsNoTracking()
            .AnyAsync(r => r.CompanyId == companyId && r.Id == runId && r.Status == DocumentExportService.Running, cancellationToken)
            .ConfigureAwait(false);
        return stillRunning ? DocumentExportLeaseRenewal.AlreadyNewer : DocumentExportLeaseRenewal.Fenced;
    }
}
