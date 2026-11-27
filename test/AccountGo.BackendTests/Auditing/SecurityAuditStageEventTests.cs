// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Auditing;
using Api.Data;
using Api.Service;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

public sealed class SecurityAuditStageEventTests
{
    private static (SqliteConnection Connection, ApplicationIdentityDbContext Db) CreateIdentityDb()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationIdentityDbContext(options);
        db.Database.EnsureCreated();
        return (connection, db);
    }

    private sealed class FixedAuditContext(string correlationId) : IRequestAuditContext
    {
        public string CorrelationId => correlationId;
        public string? ActorUserId => null;
        public string ActorKind => "user";
        public string? IpAddress => null;
    }

    private sealed class RecordingLogger : ILogger<SecurityAuditService>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task StageEvent_ThenSaveChanges_PersistsSameFieldsAndLogLineAsLogEventAsync()
    {
        var (conn, db) = CreateIdentityDb();
        using var _ = conn;
        using var __ = db;

        var logger = new RecordingLogger();
        var auditContext = new FixedAuditContext("corr-stage-parity-1");
        var service = new SecurityAuditService(db, logger, auditContext);
        var metadata = new { method = "webauthn", credentialId = 42 };

        await service.LogEventAsync(
            "mfa.challenge.success",
            userId: "user-1",
            companyId: 7,
            ipAddress: "203.0.113.10",
            userAgent: "TestBrowser/1.0",
            metadata: metadata);

        var staged = service.StageEvent(
            "mfa.challenge.success",
            outcome: null,
            userId: "user-1",
            companyId: 7,
            ipAddress: "203.0.113.10",
            userAgent: "TestBrowser/1.0",
            metadata: metadata);
        Assert.NotNull(staged);
        Assert.Equal(EntityState.Added, db.Entry(staged).State);

        await db.SaveChangesAsync();

        var rows = await db.SecurityAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, rows.Count);

        var loggedRow = rows[0];
        var stagedRow = rows[1];
        Assert.NotEqual(loggedRow.Id, stagedRow.Id);
        Assert.Equal(loggedRow.EventType, stagedRow.EventType);
        Assert.Equal(loggedRow.Outcome, stagedRow.Outcome);
        Assert.Equal(loggedRow.UserId, stagedRow.UserId);
        Assert.Equal(loggedRow.CompanyId, stagedRow.CompanyId);
        Assert.Equal(loggedRow.IpAddress, stagedRow.IpAddress);
        Assert.Equal(loggedRow.UserAgent, stagedRow.UserAgent);
        Assert.Equal(loggedRow.CorrelationId, stagedRow.CorrelationId);
        Assert.Equal(loggedRow.MetadataJson, stagedRow.MetadataJson);
        Assert.Equal("corr-stage-parity-1", stagedRow.CorrelationId);

        Assert.Equal(2, logger.Messages.Count);
        Assert.Equal(logger.Messages[0], logger.Messages[1]);
        Assert.Contains("[SecurityAudit]", logger.Messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageEvent_WithoutSaveChanges_WritesNothingToDatabase()
    {
        var (conn, db) = CreateIdentityDb();
        using var _ = conn;
        using var __ = db;

        var service = new SecurityAuditService(db, new RecordingLogger(), new FixedAuditContext("corr-nosave"));

        var staged = service.StageEvent(
            "mfa.challenge.success",
            outcome: "success",
            userId: "user-2",
            companyId: null,
            ipAddress: "198.51.100.5",
            userAgent: "TestAgent",
            metadata: new { method = "totp" });

        Assert.Equal(EntityState.Added, db.Entry(staged).State);
        Assert.Equal(0, await db.SecurityAuditEvents.AsNoTracking().CountAsync());

        // Detaching on a failure path prevents a later save from persisting the staged event.
        db.Entry(staged).State = EntityState.Detached;
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.SecurityAuditEvents.AsNoTracking().CountAsync());
    }
}
