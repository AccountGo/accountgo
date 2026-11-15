// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Service;

/// <summary>
/// Serializes refresh-token family checks, token rotation/sibling insertion, and session/family revocation
/// across concurrent requests using an in-process gate paired with a serializable database transaction.
/// </summary>
public static class RefreshTokenFamilyLock
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> ExecuteAsync<T>(
        ApplicationIdentityDbContext db,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var ownsTransaction = db.Database.IsRelational() && db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = null;
            try
            {
                if (ownsTransaction)
                {
                    tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                }

                var result = await action();

                if (ownsTransaction && tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                return result;
            }
            catch
            {
                if (ownsTransaction && tx != null)
                {
                    try
                    {
                        await tx.RollbackAsync(cancellationToken);
                    }
                    catch
                    {
                        // Ignore rollback failures after an exception.
                    }
                }
                throw;
            }
            finally
            {
                if (ownsTransaction && tx != null)
                {
                    await tx.DisposeAsync();
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task ExecuteAsync(
        ApplicationIdentityDbContext db,
        Func<Task> action,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            db,
            async () =>
            {
                await action();
                return true;
            },
            cancellationToken);
}

/// <summary>
/// Per-host synchronization gate for deterministic concurrency tests between refresh and logout/revocation.
/// Registered as a singleton per <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// so parallel test hosts cannot overwrite or reset each other's callbacks.
/// </summary>
public sealed class SessionRefreshTestGate
{
    public Func<string, Task>? BeforeInsertAsync { get; set; }

    public Task PauseBeforeInsertIfConfiguredAsync(string familyId)
    {
        var hook = BeforeInsertAsync;
        return hook != null ? hook(familyId) : Task.CompletedTask;
    }

    public void Reset()
    {
        BeforeInsertAsync = null;
    }
}
