// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Migrations
{
    /// <summary>
    /// Adding <c>CorrelationId</c> / <c>Outcome</c> to <c>SecurityAuditEvents</c> keeps every existing row
    /// (the new columns stay NULL) on the SQLite schema upgrader and, when configured, the Postgres migration.
    /// </summary>
    public sealed class SecurityAuditEventCorrelationMigrationTests
    {
        private const string PreviousPostgresIdentityMigration = "20261104120000_CliOAuth";
        private const string CorrelationIdColumn = "CorrelationId";
        private const string OutcomeColumn = "Outcome";
        private const int LegacyEventCount = 3;

        private static readonly string[] NewIndexNames =
        {
            "IX_SecurityAuditEvents_CompanyId_CreatedAt",
            "IX_SecurityAuditEvents_EventType_CreatedAt"
        };

        private const string LegacySqliteTable = @"
            DROP TABLE IF EXISTS ""SecurityAuditEvents"";
            CREATE TABLE ""SecurityAuditEvents"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SecurityAuditEvents"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" TEXT NULL,
                ""CompanyId"" INTEGER NULL,
                ""EventType"" TEXT NOT NULL,
                ""IpAddress"" TEXT NULL,
                ""UserAgent"" TEXT NULL,
                ""MetadataJson"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
            CREATE INDEX ""IX_SecurityAuditEvents_EventType"" ON ""SecurityAuditEvents"" (""EventType"");
            CREATE INDEX ""IX_SecurityAuditEvents_UserId_CreatedAt"" ON ""SecurityAuditEvents"" (""UserId"", ""CreatedAt"");";

        [Fact]
        public void Sqlite_Upgrade_PreservesExistingEvents_WithNullCorrelationIdAndOutcome()
        {
            var path = Path.Combine(Path.GetTempPath(), "secaudit_corr_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite($"Data Source={path}").Options;
                using (var seed = new ApiDbContext(options, new Mock<ITenantContext>().Object))
                    seed.Database.EnsureCreated();

                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                SqliteSchemaUpgrader.Upgrade(connection);

                // Recreate the pre-PR3 table shape and seed rows the way production data would exist.
                Execute(connection, LegacySqliteTable);
                Assert.False(ColumnExists(connection, CorrelationIdColumn));
                Assert.False(ColumnExists(connection, OutcomeColumn));
                for (var index = 1; index <= LegacyEventCount; index++)
                {
                    Execute(connection, $@"INSERT INTO ""SecurityAuditEvents""
                        (""UserId"", ""CompanyId"", ""EventType"", ""IpAddress"", ""UserAgent"", ""MetadataJson"", ""CreatedAt"")
                        VALUES ('user-{index}', {index}, 'legacy.event.{index}', '10.0.0.{index}', 'agent', '{{""n"":{index}}}', '2026-01-0{index} 00:00:00');");
                }

                SqliteSchemaUpgrader.Upgrade(connection);

                Assert.True(ColumnExists(connection, CorrelationIdColumn));
                Assert.True(ColumnExists(connection, OutcomeColumn));
                foreach (var indexName in NewIndexNames)
                    Assert.True(SqliteIndexExists(connection, indexName), indexName);

                var rows = ReadLegacyRows(connection);
                Assert.Equal(LegacyEventCount, rows.Count);
                for (var index = 1; index <= LegacyEventCount; index++)
                {
                    var row = rows[index - 1];
                    Assert.Equal($"legacy.event.{index}", row.EventType);
                    Assert.Equal($"user-{index}", row.UserId);
                    Assert.Equal($"10.0.0.{index}", row.IpAddress);
                    Assert.Null(row.CorrelationId);
                    Assert.Null(row.Outcome);
                }

                // The upgrade is idempotent and the current model can read legacy rows and write new ones.
                SqliteSchemaUpgrader.Upgrade(connection);
                Assert.Equal(LegacyEventCount, ReadLegacyRows(connection).Count);

                var identityOptions = new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite($"Data Source={path}").Options;
                using var identity = new ApplicationIdentityDbContext(identityOptions);
                var legacy = identity.SecurityAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToList();
                Assert.Equal(LegacyEventCount, legacy.Count);
                Assert.All(legacy, e =>
                {
                    Assert.Null(e.CorrelationId);
                    Assert.Null(e.Outcome);
                });
                identity.SecurityAuditEvents.Add(new SecurityAuditEvent
                {
                    EventType = "new.event",
                    CorrelationId = "corr-1",
                    Outcome = "success",
                    CreatedAt = DateTime.UtcNow
                });
                identity.SaveChanges();
                var written = identity.SecurityAuditEvents.AsNoTracking().Single(e => e.EventType == "new.event");
                Assert.Equal("corr-1", written.CorrelationId);
                Assert.Equal("success", written.Outcome);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
        }

        [Fact]
        public void Sqlite_FreshDatabase_HasNullableCorrelationIdAndOutcomeColumns()
        {
            var path = Path.Combine(Path.GetTempPath(), "secaudit_fresh_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite($"Data Source={path}").Options;
                using (var seed = new ApiDbContext(options, new Mock<ITenantContext>().Object))
                    seed.Database.EnsureCreated();
                using var connection = new SqliteConnection($"Data Source={path}");
                connection.Open();
                SqliteSchemaUpgrader.Upgrade(connection);

                foreach (var column in new[] { CorrelationIdColumn, OutcomeColumn })
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT \"notnull\" FROM pragma_table_info('SecurityAuditEvents') WHERE name = '{column}'";
                    Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
        }

        [Fact]
        public void Postgres_Migration_PreservesExistingEvents_WithNullCorrelationIdAndOutcome()
        {
            if (!PostgresTestDatabase.IsConfigured)
                return;

            using var database = PostgresTestDatabase.CreateIfConfigured()!;
            var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>().UseNpgsql(database.ConnectionString).Options;
            using var identity = new PostgresIdentityDbContext(options);
            identity.GetService<IMigrator>().Migrate(PreviousPostgresIdentityMigration);

            // Escape braces so EF Core ExecuteSqlRaw does not treat MetadataJson "{}" as a format hole.
            identity.Database.ExecuteSqlRaw("""
                INSERT INTO "SecurityAuditEvents" ("UserId", "CompanyId", "EventType", "IpAddress", "UserAgent", "MetadataJson", "CreatedAt")
                VALUES ('user-1', 1, 'legacy.event.1', '10.0.0.1', 'agent', '{{}}', TIMESTAMP '2026-01-01 00:00:00'),
                       ('user-2', 2, 'legacy.event.2', '10.0.0.2', 'agent', '{{}}', TIMESTAMP '2026-01-02 00:00:00'),
                       ('user-3', NULL, 'legacy.event.3', '10.0.0.3', 'agent', '{{}}', TIMESTAMP '2026-01-03 00:00:00')
                """);

            identity.Database.Migrate();

            using var connection = new NpgsqlConnection(database.ConnectionString);
            connection.Open();
            foreach (var column in new[] { CorrelationIdColumn, OutcomeColumn })
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'SecurityAuditEvents' AND column_name = @column";
                command.Parameters.AddWithValue("column", column);
                Assert.Equal("YES", command.ExecuteScalar()?.ToString());
            }
            foreach (var indexName in NewIndexNames)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'SecurityAuditEvents' AND indexname = @name";
                command.Parameters.AddWithValue("name", indexName);
                Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
            }

            var rows = identity.SecurityAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToList();
            Assert.Equal(LegacyEventCount, rows.Count);
            Assert.Equal(new[] { "legacy.event.1", "legacy.event.2", "legacy.event.3" }, rows.Select(r => r.EventType));
            Assert.All(rows, row =>
            {
                Assert.Null(row.CorrelationId);
                Assert.Null(row.Outcome);
            });
        }

        [Fact]
        public void Postgres_Migration_ScriptAddsColumnsAndIndexes_WithoutSqliteStatements()
        {
            var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options;
            using var identity = new PostgresIdentityDbContext(options);

            var script = identity.GetService<IMigrator>().GenerateScript(PreviousPostgresIdentityMigration, "20261107120001_SecurityAuditEventCorrelation");

            Assert.Contains("\"CorrelationId\"", script);
            Assert.Contains("\"Outcome\"", script);
            foreach (var indexName in NewIndexNames)
                Assert.Contains(indexName, script);
            Assert.DoesNotContain("AUTOINCREMENT", script, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record LegacyRow(string EventType, string? UserId, string? IpAddress, string? CorrelationId, string? Outcome);

        private static List<LegacyRow> ReadLegacyRows(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ""EventType"", ""UserId"", ""IpAddress"", ""CorrelationId"", ""Outcome""
                FROM ""SecurityAuditEvents"" WHERE ""EventType"" LIKE 'legacy.event.%' ORDER BY ""Id""";
            using var reader = command.ExecuteReader();
            var rows = new List<LegacyRow>();
            while (reader.Read())
            {
                rows.Add(new LegacyRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
            return rows;
        }

        private static void Execute(DbConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static bool ColumnExists(DbConnection connection, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SecurityAuditEvents') WHERE name = '{column}'";
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static bool SqliteIndexExists(DbConnection connection, string indexName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{indexName}'";
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }
}
