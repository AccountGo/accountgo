// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data.Common;
using System.Linq;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

/// <summary>PR5: DB triggers and EF guard for SecurityAuditEvents retention / append-only.</summary>
public sealed class SecurityAuditEventRetentionTests
{
    private const string PreviousPostgresIdentityMigration = "20261107120001_SecurityAuditEventCorrelation";

    [Fact]
    public void Sqlite_CreatedAt_ComparesCorrectlyWithDatetimeNowOffsets()
    {
        using var connection = OpenSqlite();
        using var identity = CreateSqliteIdentity(connection);
        identity.Database.EnsureCreated();
        InstallSqliteTriggers(connection);

        var now = DateTime.UtcNow;
        InsertRaw(connection, "auth.signin.success", now.AddDays(-10));
        InsertRaw(connection, "auth.signin.success", now.AddDays(-731));
        InsertRaw(connection, "data.export", now.AddDays(-364));
        InsertRaw(connection, "data.export", now.AddDays(-366));

        // Prove EF-style TEXT timestamps compare correctly via julianday() (what the DELETE trigger uses).
        using var compare = connection.CreateCommand();
        compare.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM "SecurityAuditEvents" WHERE "EventType" = 'auth.signin.success'
                 AND julianday("CreatedAt") < julianday('now', '-730 days')),
              (SELECT COUNT(*) FROM "SecurityAuditEvents" WHERE "EventType" = 'data.export'
                 AND julianday("CreatedAt") < julianday('now', '-365 days')),
              (SELECT "CreatedAt" FROM "SecurityAuditEvents" ORDER BY "Id" LIMIT 1),
              (SELECT julianday("CreatedAt") IS NOT NULL FROM "SecurityAuditEvents" ORDER BY "Id" LIMIT 1)
            """;
        using var reader = compare.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        var sample = reader.GetString(2);
        Assert.True(string.CompareOrdinal(sample, "2000-01-01") > 0, sample);
        Assert.Equal(1L, reader.GetInt64(3));
    }

    [Fact]
    public void Sqlite_Update_IsBlocked()
    {
        using var connection = OpenSqlite();
        using var identity = CreateSqliteIdentity(connection);
        identity.Database.EnsureCreated();
        InstallSqliteTriggers(connection);
        var id = InsertRaw(connection, "auth.signin.success", DateTime.UtcNow.AddDays(-800));

        var error = Assert.ThrowsAny<Exception>(() =>
            Execute(connection, $"""UPDATE "SecurityAuditEvents" SET "Outcome" = 'x' WHERE "Id" = {id}"""));
        Assert.Contains("cannot be updated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_Delete_RespectsRetentionFloors()
    {
        using var connection = OpenSqlite();
        using var identity = CreateSqliteIdentity(connection);
        identity.Database.EnsureCreated();
        InstallSqliteTriggers(connection);

        var youngSecurity = InsertRaw(connection, "auth.signin.success", DateTime.UtcNow.AddDays(-10));
        var oldSecurity = InsertRaw(connection, "auth.signin.success", DateTime.UtcNow.AddDays(-731));
        var youngData = InsertRaw(connection, "data.export", DateTime.UtcNow.AddDays(-364));
        var oldData = InsertRaw(connection, "data.export", DateTime.UtcNow.AddDays(-366));
        var midSecurity = InsertRaw(connection, "auth.signin.success", DateTime.UtcNow.AddDays(-366));

        Assert.ThrowsAny<Exception>(() => Execute(connection, $"""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {youngSecurity}"""));
        Assert.ThrowsAny<Exception>(() => Execute(connection, $"""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {youngData}"""));
        Assert.ThrowsAny<Exception>(() => Execute(connection, $"""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {midSecurity}"""));

        Execute(connection, $"""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {oldSecurity}""");
        Execute(connection, $"""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {oldData}""");
        Assert.Equal(3, CountEvents(connection));
    }

    [Fact]
    public void EfGuard_RejectsTrackedModifyAndDelete()
    {
        using var connection = OpenSqlite();
        using var identity = CreateSqliteIdentity(connection);
        identity.Database.EnsureCreated();

        identity.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            EventType = "auth.signin.success",
            Outcome = "success",
            CreatedAt = DateTime.UtcNow.AddDays(-800)
        });
        identity.SaveChanges();
        var row = identity.SecurityAuditEvents.Single();

        row.Outcome = "changed";
        var modify = Assert.Throws<InvalidOperationException>(() => identity.SaveChanges());
        Assert.Contains("append-only", modify.Message, StringComparison.OrdinalIgnoreCase);
        identity.Entry(row).State = EntityState.Unchanged;

        identity.SecurityAuditEvents.Remove(row);
        var delete = Assert.Throws<InvalidOperationException>(() => identity.SaveChanges());
        Assert.Contains("append-only", delete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Postgres_Triggers_RespectRetentionFloors()
    {
        if (!PostgresTestDatabase.IsConfigured) return;

        using var database = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>().UseNpgsql(database.ConnectionString).Options;
        using var identity = new PostgresIdentityDbContext(options);
        identity.Database.Migrate();

        var youngSecurity = InsertPg(identity, "auth.signin.success", DateTime.UtcNow.AddDays(-10));
        var oldSecurity = InsertPg(identity, "auth.signin.success", DateTime.UtcNow.AddDays(-731));
        var youngData = InsertPg(identity, "data.export", DateTime.UtcNow.AddDays(-364));
        var oldData = InsertPg(identity, "invoice.pdf.public", DateTime.UtcNow.AddDays(-366));
        var midSecurity = InsertPg(identity, "auth.signin.success", DateTime.UtcNow.AddDays(-366));

        Assert.ThrowsAny<Exception>(() => identity.Database.ExecuteSqlRaw(
            """UPDATE "SecurityAuditEvents" SET "Outcome" = 'x' WHERE "Id" = {0}""", youngSecurity));
        Assert.ThrowsAny<Exception>(() => identity.Database.ExecuteSqlRaw(
            """DELETE FROM "SecurityAuditEvents" WHERE "Id" = {0}""", youngSecurity));
        Assert.ThrowsAny<Exception>(() => identity.Database.ExecuteSqlRaw(
            """DELETE FROM "SecurityAuditEvents" WHERE "Id" = {0}""", youngData));
        Assert.ThrowsAny<Exception>(() => identity.Database.ExecuteSqlRaw(
            """DELETE FROM "SecurityAuditEvents" WHERE "Id" = {0}""", midSecurity));

        identity.Database.ExecuteSqlRaw("""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {0}""", oldSecurity);
        identity.Database.ExecuteSqlRaw("""DELETE FROM "SecurityAuditEvents" WHERE "Id" = {0}""", oldData);
        Assert.Equal(3, identity.SecurityAuditEvents.Count());
    }

    [Fact]
    public void Postgres_Migration_ScriptIncludesTriggers()
    {
        var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var identity = new PostgresIdentityDbContext(options);
        var script = identity.GetService<IMigrator>().GenerateScript(
            PreviousPostgresIdentityMigration, "20261107140001_SecurityAuditEventRetention");
        Assert.Contains("RejectSecurityAuditEventMutation", script, StringComparison.Ordinal);
        Assert.Contains("TR_SecurityAuditEvents_Immutable", script, StringComparison.Ordinal);
        Assert.Contains("365 days", script, StringComparison.Ordinal);
        Assert.Contains("730 days", script, StringComparison.Ordinal);
    }

    private static SqliteConnection OpenSqlite()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static ApplicationIdentityDbContext CreateSqliteIdentity(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(connection).Options);

    private static void InstallSqliteTriggers(DbConnection connection)
    {
        foreach (var sql in new[] { SqliteSecurityAuditEventsSql.UpdateTrigger, SqliteSecurityAuditEventsSql.DeleteTrigger })
            Execute(connection, sql);
    }

    private static int InsertRaw(DbConnection connection, string eventType, DateTime createdAt)
    {
        // Match EF Core SQLite's default DateTime TEXT format (invariant — never the host calendar).
        var text = createdAt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "SecurityAuditEvents" ("EventType", "CreatedAt")
            VALUES ($type, $created);
            SELECT last_insert_rowid();
            """;
        var type = command.CreateParameter();
        type.ParameterName = "$type";
        type.Value = eventType;
        command.Parameters.Add(type);
        var created = command.CreateParameter();
        created.ParameterName = "$created";
        created.Value = text;
        command.Parameters.Add(created);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int InsertPg(PostgresIdentityDbContext identity, string eventType, DateTime createdAt)
    {
        identity.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            EventType = eventType,
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
        });
        identity.SaveChanges();
        return identity.SecurityAuditEvents.AsNoTracking()
            .Where(e => e.EventType == eventType)
            .OrderByDescending(e => e.Id)
            .Select(e => e.Id)
            .First();
    }

    private static int CountEvents(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"SecurityAuditEvents\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
