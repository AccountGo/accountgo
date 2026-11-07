// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Api.Options;
using Api.Service;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

/// <summary>PR5: AuditRetentionWorker deletes only expired SecurityAuditEvents.</summary>
public sealed class AuditRetentionWorkerTests
{
    [Fact]
    public async Task RunOnce_DeletesExpiredEvents_AndWritesPurgeEvent()
    {
        await using var harness = await Harness.CreateAsync();
        var now = DateTime.UtcNow;
        harness.SeedEvent("auth.signin.success", now.AddDays(-10));
        harness.SeedEvent("auth.signin.success", now.AddDays(-731));
        harness.SeedEvent("data.export", now.AddDays(-10));
        harness.SeedEvent("data.export", now.AddDays(-366));
        harness.SeedEvent("invoice.pdf.public", now.AddDays(-400));
        harness.SeedEvent("data.report.view", now.AddDays(-200));
        harness.SeedProtectedEvidence();

        var result = await harness.Worker.RunOnceAsync();

        Assert.Equal(2, result.DataAccessDeleted); // data.export 366d + invoice.pdf.public 400d
        Assert.Equal(1, result.SecurityDeleted); // auth 731d
        using var identity = harness.CreateIdentity();
        var remaining = identity.SecurityAuditEvents.AsNoTracking().ToList();
        Assert.Contains(remaining, e => e.EventType == "auth.signin.success" && e.CreatedAt > now.AddDays(-30));
        Assert.Contains(remaining, e => e.EventType == "data.export" && e.CreatedAt > now.AddDays(-30));
        Assert.Contains(remaining, e => e.EventType == "data.report.view");
        Assert.Contains(remaining, e => e.EventType == "audit.retention.purge");
        Assert.Single(remaining, e => e.EventType == "audit.retention.purge");

        using var api = harness.CreateApi();
        Assert.Equal(3, api.RecordChanges.IgnoreQueryFilters().Count());
        Assert.Equal(2, api.AuditLogs.IgnoreQueryFilters().Count(a => a.TableName.StartsWith("__")));
    }

    [Fact]
    public async Task RunOnce_SecondPass_DeletesNothing()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedEvent("auth.signin.success", DateTime.UtcNow.AddDays(-800));
        await harness.Worker.RunOnceAsync();
        var second = await harness.Worker.RunOnceAsync();
        Assert.Equal(0, second.DataAccessDeleted);
        Assert.Equal(0, second.SecurityDeleted);
        using var identity = harness.CreateIdentity();
        Assert.Equal(1, identity.SecurityAuditEvents.Count(e => e.EventType == "audit.retention.purge"));
    }

    [Fact]
    public async Task RunOnce_Disabled_DeletesNothing()
    {
        await using var harness = await Harness.CreateAsync(enabled: false);
        harness.SeedEvent("auth.signin.success", DateTime.UtcNow.AddDays(-800));
        var result = await harness.Worker.RunOnceAsync();
        Assert.Equal(AuditRetentionRunResult.SkippedDisabled, result);
        using var identity = harness.CreateIdentity();
        Assert.Equal(1, identity.SecurityAuditEvents.Count());
    }

    [Fact]
    public async Task RunOnce_ConfigBelowFloor_IsRaised_WithWarning()
    {
        var logger = new ListLogger();
        await using var harness = await Harness.CreateAsync(
            securityDays: 30, dataAccessDays: 7, logger: logger);
        harness.SeedEvent("auth.signin.success", DateTime.UtcNow.AddDays(-100)); // younger than floor
        harness.SeedEvent("auth.signin.success", DateTime.UtcNow.AddDays(-731));
        var result = await harness.Worker.RunOnceAsync();
        Assert.Equal(1, result.SecurityDeleted);
        Assert.Contains(logger.Messages, m => m.Contains("SecurityEventDays", StringComparison.Ordinal)
            && m.Contains("below the floor", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("DataAccessDays", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunOnce_LongerConfiguredPeriod_KeepsMore()
    {
        await using var harness = await Harness.CreateAsync(securityDays: 1000, dataAccessDays: 500);
        harness.SeedEvent("auth.signin.success", DateTime.UtcNow.AddDays(-800));
        harness.SeedEvent("data.export", DateTime.UtcNow.AddDays(-400));
        var result = await harness.Worker.RunOnceAsync();
        Assert.Equal(0, result.SecurityDeleted);
        Assert.Equal(0, result.DataAccessDeleted);
        using var identity = harness.CreateIdentity();
        Assert.Equal(2, identity.SecurityAuditEvents.Count());
    }

    [Fact]
    public async Task Postgres_TwoWorkers_DeleteEachRowOnce()
    {
        if (!PostgresTestDatabase.IsConfigured) return;

        using var database = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using (var setup = new PostgresIdentityDbContext(options))
        {
            await setup.Database.MigrateAsync();
            for (var index = 0; index < 20; index++)
            {
                setup.SecurityAuditEvents.Add(new SecurityAuditEvent
                {
                    EventType = "auth.signin.success",
                    CreatedAt = DateTime.UtcNow.AddDays(-800)
                });
            }
            await setup.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationIdentityDbContext, PostgresIdentityDbContext>(o => o.UseNpgsql(database.ConnectionString));
        services.AddSingleton<IOptionsMonitor<AuditRetentionOptions>>(
            new StaticOptionsMonitor(new AuditRetentionOptions()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        var workerA = new AuditRetentionWorker(factory, provider.GetRequiredService<IOptionsMonitor<AuditRetentionOptions>>(), NullLogger<AuditRetentionWorker>.Instance);
        var workerB = new AuditRetentionWorker(factory, provider.GetRequiredService<IOptionsMonitor<AuditRetentionOptions>>(), NullLogger<AuditRetentionWorker>.Instance);

        var results = await Task.WhenAll(workerA.RunOnceAsync(), workerB.RunOnceAsync());
        Assert.Contains(results, r => r.SecurityDeleted > 0 || r.DataAccessDeleted > 0);
        Assert.Contains(results, r => r.Equals(AuditRetentionRunResult.SkippedLocked) || r.SecurityDeleted == 0);

        await using var identity = new PostgresIdentityDbContext(options);
        var leftover = await identity.SecurityAuditEvents.AsNoTracking()
            .Where(e => e.EventType == "auth.signin.success").CountAsync();
        Assert.Equal(0, leftover);
        Assert.Equal(1, await identity.SecurityAuditEvents.CountAsync(e => e.EventType == "audit.retention.purge"));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _path;
        private readonly ServiceProvider _provider;
        public AuditRetentionWorker Worker { get; }

        private Harness(string path, ServiceProvider provider, AuditRetentionWorker worker)
        {
            _path = path;
            _provider = provider;
            Worker = worker;
        }

        public static async Task<Harness> CreateAsync(
            bool enabled = true,
            int securityDays = 730,
            int dataAccessDays = 365,
            ILogger<AuditRetentionWorker>? logger = null)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audit_ret_" + Guid.NewGuid().ToString("N") + ".db");
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationIdentityDbContext>(o => o.UseSqlite("Data Source=" + path));
            services.AddDbContext<ApiDbContext>(o => o.UseSqlite("Data Source=" + path));
            services.AddSingleton<IOptionsMonitor<AuditRetentionOptions>>(new StaticOptionsMonitor(new AuditRetentionOptions
            {
                Enabled = enabled,
                SecurityEventDays = securityDays,
                DataAccessDays = dataAccessDays
            }));
            var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                // Api first: EnsureCreated on a non-empty SQLite file does not add missing tables.
                var api = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                await api.Database.EnsureCreatedAsync();
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                await identity.Database.EnsureCreatedAsync();
                await using var connection = new SqliteConnection("Data Source=" + path);
                await connection.OpenAsync();
                foreach (var sql in new[]
                {
                    """CREATE TABLE IF NOT EXISTS "RecordChanges" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "EntityType" TEXT NOT NULL, "EntityId" INTEGER NOT NULL, "Action" TEXT NOT NULL, "ChangesJson" TEXT NOT NULL, "ActorUserId" TEXT NULL, "ActorKind" TEXT NOT NULL, "CorrelationId" TEXT NOT NULL, "IpAddress" TEXT NULL, "OccurredAt" TEXT NOT NULL);""",
                    """CREATE TABLE IF NOT EXISTS "AuditLog" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "CompanyId" INTEGER NOT NULL, "UserName" TEXT NULL, "AuditEventDateUTC" TEXT NOT NULL, "AuditEventType" INTEGER NOT NULL, "TableName" TEXT NULL, "RecordId" TEXT NULL, "FieldName" TEXT NULL, "OriginalValue" TEXT NULL, "NewValue" TEXT NULL);""",
                    """CREATE TABLE IF NOT EXISTS "SecurityAuditEvents" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "UserId" TEXT NULL, "CompanyId" INTEGER NULL, "EventType" TEXT NOT NULL, "IpAddress" TEXT NULL, "UserAgent" TEXT NULL, "MetadataJson" TEXT NULL, "CorrelationId" TEXT NULL, "Outcome" TEXT NULL, "CreatedAt" TEXT NOT NULL);""",
                    """CREATE TABLE IF NOT EXISTS "ExternalTokenRedemptions" ("Provider" TEXT NOT NULL, "TokenId" TEXT NOT NULL, "ExpiresAtUtc" TEXT NOT NULL, "RedeemedAtUtc" TEXT NOT NULL, CONSTRAINT "PK_ExternalTokenRedemptions" PRIMARY KEY ("Provider", "TokenId"));""",
                    SqliteSecurityAuditEventsSql.UpdateTrigger,
                    SqliteSecurityAuditEventsSql.DeleteTrigger
                })
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    await command.ExecuteNonQueryAsync();
                }
            }

            var worker = new AuditRetentionWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IOptionsMonitor<AuditRetentionOptions>>(),
                logger ?? NullLogger<AuditRetentionWorker>.Instance);
            return new Harness(path, provider, worker);
        }

        public void SeedEvent(string eventType, DateTime createdAt)
        {
            using var identity = CreateIdentity();
            // Bypass EF CreatedAt default by raw insert so ages are exact for the trigger/worker.
            // Escape braces so ExecuteSqlRaw does not treat JSON/format holes; dates stay invariant.
            var createdText = createdAt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture);
            identity.Database.ExecuteSqlRaw(
                """
                INSERT INTO "SecurityAuditEvents" ("EventType", "CreatedAt", "Outcome")
                VALUES ({0}, {1}, 'success')
                """,
                eventType,
                createdText);
        }

        public void SeedProtectedEvidence()
        {
            using var api = CreateApi();
            var occurred = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            api.Database.ExecuteSqlRaw(
                """
                INSERT INTO "RecordChanges" ("CompanyId", "EntityType", "EntityId", "Action", "ChangesJson", "ActorKind", "CorrelationId", "OccurredAt")
                VALUES (1, 'Account', 1, 'Modified', '{{}}', 'system', 'c1', {0}),
                       (1, 'Account', 2, 'Modified', '{{}}', 'system', 'c2', {0}),
                       (1, 'Account', 3, 'Modified', '{{}}', 'system', 'c3', {0})
                """,
                occurred);
            api.Database.ExecuteSqlRaw(
                """
                INSERT INTO "AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
                VALUES (1, 'seed', {0}, 1, '__ApprovalEvidence', '1', 'State', 'a', 'b'),
                       (1, 'seed', {0}, 1, '__ApprovalState', '1', 'State', 'a', 'b')
                """,
                DateTime.UtcNow);
        }

        public ApplicationIdentityDbContext CreateIdentity() =>
            _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        public ApiDbContext CreateApi() =>
            _provider.CreateScope().ServiceProvider.GetRequiredService<ApiDbContext>();

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            {
                if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
            }
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuditRetentionOptions>
    {
        public StaticOptionsMonitor(AuditRetentionOptions current) => CurrentValue = current;
        public AuditRetentionOptions CurrentValue { get; }
        public AuditRetentionOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuditRetentionOptions, string?> listener) => null;
    }

    private sealed class ListLogger : ILogger<AuditRetentionWorker>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
