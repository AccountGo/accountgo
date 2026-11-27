// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Auditing;
using Api.Data;
using Api.Service;
using Microsoft.AspNetCore.Http;
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

    private sealed class FixedAuditContext(string correlationId, string? ipAddress = null) : IRequestAuditContext
    {
        public string CorrelationId => correlationId;
        public string? ActorUserId => null;
        public string ActorKind => "user";
        public string? IpAddress => ipAddress;
    }

    private sealed class ThrowingMetadata
    {
        public string Explode => throw new InvalidOperationException("boom during serialization");
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
    public async Task StageEvent_LogEventAsync_And_LogEventInTransactionAsync_PersistSameFieldsAndLogLine()
    {
        var (conn, db) = CreateIdentityDb();
        using var _ = conn;
        using var __ = db;

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "TestBrowser/1.0";
        var httpAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var logger = new RecordingLogger();
        var auditContext = new FixedAuditContext("corr-stage-parity-1", ipAddress: "203.0.113.10");
        var service = new SecurityAuditService(db, logger, auditContext, httpAccessor);
        var metadata = new { method = "webauthn", credentialId = 42 };

        await service.LogEventAsync(
            "mfa.challenge.success",
            outcome: "success",
            userId: "user-1",
            companyId: 7,
            metadata: metadata);

        var staged = service.StageEvent(
            "mfa.challenge.success",
            outcome: "success",
            userId: "user-1",
            companyId: 7,
            ipAddress: "203.0.113.10",
            userAgent: "TestBrowser/1.0",
            metadata: metadata);
        Assert.NotNull(staged);
        Assert.Equal(EntityState.Added, db.Entry(staged).State);
        await db.SaveChangesAsync();

        await using (var ownerDb = new ApplicationIdentityDbContext(
            new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(conn).Options))
        await using (var tx = await ownerDb.Database.BeginTransactionAsync())
        {
            await service.LogEventInTransactionAsync(
                ownerDb,
                "mfa.challenge.success",
                outcome: "success",
                userId: "user-1",
                companyId: 7,
                metadata: metadata,
                CancellationToken.None);
            await tx.CommitAsync();
        }

        var rows = await db.SecurityAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(3, rows.Count);

        var loggedRow = rows[0];
        var stagedRow = rows[1];
        var txRow = rows[2];

        foreach (var other in new[] { stagedRow, txRow })
        {
            Assert.Equal(loggedRow.EventType, other.EventType);
            Assert.Equal(loggedRow.Outcome, other.Outcome);
            Assert.Equal(loggedRow.UserId, other.UserId);
            Assert.Equal(loggedRow.CompanyId, other.CompanyId);
            Assert.Equal(loggedRow.IpAddress, other.IpAddress);
            Assert.Equal(loggedRow.UserAgent, other.UserAgent);
            Assert.Equal(loggedRow.CorrelationId, other.CorrelationId);
            Assert.Equal(loggedRow.MetadataJson, other.MetadataJson);
        }

        Assert.Equal("corr-stage-parity-1", stagedRow.CorrelationId);
        Assert.Equal("203.0.113.10", txRow.IpAddress);
        Assert.Equal("TestBrowser/1.0", txRow.UserAgent);

        Assert.Equal(3, logger.Messages.Count);
        Assert.Equal(logger.Messages[0], logger.Messages[1]);
        Assert.Equal(logger.Messages[0], logger.Messages[2]);
        Assert.Contains("[SecurityAudit]", logger.Messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataSerializationError_TransactionalThrowsAndLeavesNoRow_BestEffortWritesNullMetadata()
    {
        var (conn, db) = CreateIdentityDb();
        using var _ = conn;
        using var __ = db;

        var logger = new RecordingLogger();
        var auditContext = new FixedAuditContext("corr-bad-meta", ipAddress: "198.51.100.7");
        var service = new SecurityAuditService(db, logger, auditContext);
        var badMetadata = new ThrowingMetadata();

        await using (var ownerDb = new ApplicationIdentityDbContext(
            new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(conn).Options))
        await using (var tx = await ownerDb.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.LogEventInTransactionAsync(
                    ownerDb,
                    "security.critical",
                    outcome: "success",
                    userId: "user-tx",
                    companyId: 3,
                    metadata: badMetadata,
                    CancellationToken.None));
            await tx.RollbackAsync();
        }

        Assert.Equal(0, await db.SecurityAuditEvents.AsNoTracking().CountAsync());

        await service.LogEventAsync(
            "security.best_effort",
            outcome: "success",
            userId: "user-log",
            companyId: 3,
            metadata: badMetadata);

        var staged = service.StageEvent(
            "security.staged",
            outcome: "success",
            userId: "user-stage",
            companyId: 3,
            ipAddress: "198.51.100.7",
            userAgent: "TestAgent",
            metadata: badMetadata);
        Assert.Null(staged.MetadataJson);
        await db.SaveChangesAsync();

        var rows = await db.SecurityAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("security.best_effort", rows[0].EventType);
        Assert.Null(rows[0].MetadataJson);
        Assert.Equal("security.staged", rows[1].EventType);
        Assert.Null(rows[1].MetadataJson);
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
