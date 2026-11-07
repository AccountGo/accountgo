// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Service;

/// <summary>
/// Daily purge of expired <c>SecurityAuditEvents</c> only. Never touches change or evidence tables.
/// Runs off the request path with a short start delay; Postgres instances coordinate via advisory lock.
/// </summary>
public sealed class AuditRetentionWorker : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RunInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<AuditRetentionOptions> _options;
    private readonly ILogger<AuditRetentionWorker> _logger;

    public AuditRetentionWorker(
        IServiceScopeFactory scopes,
        IOptionsMonitor<AuditRetentionOptions> options,
        ILogger<AuditRetentionWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit retention run failed");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One purge pass. Public for tests.</summary>
    public async Task<AuditRetentionRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var configured = _options.CurrentValue;
        if (!configured.Enabled)
        {
            _logger.LogInformation("Audit retention skipped (disabled)");
            return AuditRetentionRunResult.SkippedDisabled;
        }

        var securityDays = configured.SecurityEventDays;
        var dataAccessDays = configured.DataAccessDays;
        if (securityDays < AuditRetentionOptions.SecurityEventFloorDays)
        {
            _logger.LogWarning(
                "AuditRetention:SecurityEventDays={Configured} is below the floor {Floor}; using the floor",
                securityDays, AuditRetentionOptions.SecurityEventFloorDays);
            securityDays = AuditRetentionOptions.SecurityEventFloorDays;
        }
        if (dataAccessDays < AuditRetentionOptions.DataAccessFloorDays)
        {
            _logger.LogWarning(
                "AuditRetention:DataAccessDays={Configured} is below the floor {Floor}; using the floor",
                dataAccessDays, AuditRetentionOptions.DataAccessFloorDays);
            dataAccessDays = AuditRetentionOptions.DataAccessFloorDays;
        }

        using var scope = _scopes.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        if (!await TryAcquirePostgresLockAsync(identity, cancellationToken))
        {
            _logger.LogInformation("Audit retention skipped (another instance holds the advisory lock)");
            return AuditRetentionRunResult.SkippedLocked;
        }

        try
        {
            var now = DateTime.UtcNow;
            var securityCutoff = now.AddDays(-securityDays);
            var dataAccessCutoff = now.AddDays(-dataAccessDays);

            var dataAccessDeleted = await DeleteExpiredAsync(
                identity,
                dataAccess: true,
                dataAccessCutoff,
                cancellationToken);
            var securityDeleted = await DeleteExpiredAsync(
                identity,
                dataAccess: false,
                securityCutoff,
                cancellationToken);

            _logger.LogInformation(
                "Audit retention complete: dataAccessDeleted={DataAccessDeleted} securityDeleted={SecurityDeleted} dataAccessCutoff={DataAccessCutoff:o} securityCutoff={SecurityCutoff:o}",
                dataAccessDeleted, securityDeleted, dataAccessCutoff, securityCutoff);

            if (dataAccessDeleted + securityDeleted > 0)
            {
                identity.SecurityAuditEvents.Add(new SecurityAuditEvent
                {
                    EventType = "audit.retention.purge",
                    Outcome = "success",
                    CompanyId = null,
                    UserId = null,
                    CorrelationId = null,
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        dataAccessDeleted,
                        securityDeleted,
                        dataAccessCutoff,
                        securityCutoff,
                        dataAccessDays,
                        securityDays
                    }),
                    CreatedAt = DateTime.UtcNow
                });
                await identity.SaveChangesAsync(cancellationToken);
            }

            return new AuditRetentionRunResult(dataAccessDeleted, securityDeleted, securityCutoff, dataAccessCutoff);
        }
        finally
        {
            // Release even when shutdown cancels the run, so the session lock never outlives the pass.
            await ReleasePostgresLockAsync(identity);
        }
    }

    private static async Task<int> DeleteExpiredAsync(
        ApplicationIdentityDbContext identity,
        bool dataAccess,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var query = identity.SecurityAuditEvents.AsNoTracking()
                .Where(e => e.CreatedAt < cutoff);
            query = dataAccess
                ? query.Where(e => e.EventType.StartsWith("data.") || e.EventType == "invoice.pdf.public")
                : query.Where(e => !(e.EventType.StartsWith("data.") || e.EventType == "invoice.pdf.public"));

            var ids = await query
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(AuditRetentionOptions.DeleteBatchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) break;

            var deleted = await identity.SecurityAuditEvents
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
            if (deleted == 0) break;
        }
        return total;
    }

    private static async Task<bool> TryAcquirePostgresLockAsync(
        ApplicationIdentityDbContext identity,
        CancellationToken cancellationToken)
    {
        if (!IsNpgsql(identity)) return true;
        await identity.Database.OpenConnectionAsync(cancellationToken);
        await using var command = identity.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = AuditRetentionOptions.PostgresAdvisoryLockKey;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            true => true,
            false => false,
            int i => i != 0,
            long l => l != 0,
            null => false,
            _ => Convert.ToBoolean(result)
        };
    }

    private static async Task ReleasePostgresLockAsync(ApplicationIdentityDbContext identity)
    {
        if (!IsNpgsql(identity)) return;
        try
        {
            await using var command = identity.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = AuditRetentionOptions.PostgresAdvisoryLockKey;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch
        {
            // Best effort on shutdown.
        }
    }

    private static bool IsNpgsql(ApplicationIdentityDbContext identity) =>
        identity.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}

public readonly record struct AuditRetentionRunResult(
    int DataAccessDeleted,
    int SecurityDeleted,
    DateTime SecurityCutoff,
    DateTime DataAccessCutoff)
{
    public static AuditRetentionRunResult SkippedDisabled { get; } =
        new(0, 0, default, default);

    public static AuditRetentionRunResult SkippedLocked { get; } =
        new(-1, -1, default, default);
}
