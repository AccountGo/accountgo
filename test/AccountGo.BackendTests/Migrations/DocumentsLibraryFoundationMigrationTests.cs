// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class DocumentsLibraryFoundationMigrationTests
{
    private const string SqliteProvider = "Sqlite";
    private const string PostgresProvider = "Postgres";
    private const string TargetMigrationSuffix = "_DocumentsLibraryFoundation";

    public static IEnumerable<object[]> Providers()
    {
        yield return [SqliteProvider];
        if (PostgresTestDatabase.IsConfigured)
            yield return [PostgresProvider];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EfMigrations_FreshSchema_CreatesMultiLinkIndexesAndRevisionTable(string provider)
    {
        using var harness = new MigrationHarness(provider);
        harness.MigrateToLatest();

        Assert.True(harness.TableExists("DocumentLibraryRevisions"));
        Assert.False(harness.IndexExists("IX_DocumentCaptureLinks_CompanyId_CaptureId"));
        Assert.True(harness.IndexExists("IX_DocumentCaptureLinks_Company_Capture_Target"));
        Assert.True(harness.IndexExists("IX_DocumentCaptureLinks_SourceRole"));
        Assert.True(harness.IndexExists("IX_DocumentCaptureLinks_CompanyId_TargetType_TargetId"));
        Assert.True(harness.IndexExists("IX_DocumentCaptures_Filed"));

        foreach (var col in new[]
        {
            "FileName", "Title", "Category", "DocumentDate", "Notes",
            "FiledAt", "FiledBy", "ArchivedAt", "ArchivedBy", "DeletedAt", "DeletedBy"
        })
        {
            Assert.True(harness.ColumnExists("DocumentCaptures", col), $"Missing column DocumentCaptures.{col}");
        }
        Assert.True(harness.ColumnExists("DocumentCaptureLinks", "Role"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EfMigrations_UpgradeBackfillsOnlyLinkedScanCapturesWithBytes_AndAllowsCleanDownWhenSingleSourceLinksOnly(string provider)
    {
        using var harness = new MigrationHarness(provider);
        harness.MigrateToPrior();
        harness.SeedCompany(1, "Alpha Books", "alpha-books");

        // 1-3: scan kinds with a link AND bytes -> must be filed with AcceptedAt/AcceptedBy
        harness.SeedCapturePreMigration(101, 1, "cap_01scanreceipt00000000001", "expense-receipt", "accepted", "2026-11-10T12:00:00", "alice", withBlob: true);
        harness.SeedLegacyLink(1, 101, "bill", 501);

        harness.SeedCapturePreMigration(102, 1, "cap_01scanpurch0000000000002", "purchase-invoice", "accepted", "2026-11-11T12:00:00", "bob", withBlob: true);
        harness.SeedLegacyLink(1, 102, "bill", 502);

        harness.SeedCapturePreMigration(103, 1, "cap_01scansales0000000000003", "sales-invoice", "accepted", "2026-11-12T12:00:00", "carol", withBlob: true);
        harness.SeedLegacyLink(1, 103, "invoice", 601);

        // 4: scan kind WITHOUT link -> not filed
        harness.SeedCapturePreMigration(104, 1, "cap_01unlinked00000000000004", "purchase-invoice", "accepted", "2026-11-13T12:00:00", "dave", withBlob: true);

        // 5: scan kind WITH link but WITHOUT blob -> not filed
        harness.SeedCapturePreMigration(105, 1, "cap_01noblob0000000000000005", "expense-receipt", "accepted", "2026-11-14T12:00:00", "erin", withBlob: false);
        harness.SeedLegacyLink(1, 105, "bill", 503);

        // 6: assistant-attachment (even with link + blob) -> never filed (I6)
        harness.SeedCapturePreMigration(106, 1, "cap_01asst000000000000000006", "assistant-attachment", "accepted", "2026-11-15T12:00:00", "frank", withBlob: true);
        harness.SeedLegacyLink(1, 106, "bill", 504);

        // 7: bank (even with link + blob) -> never filed (I6)
        harness.SeedCapturePreMigration(107, 1, "cap_01bank000000000000000007", "bank", "accepted", "2026-11-16T12:00:00", "grace", withBlob: true);
        harness.SeedLegacyLink(1, 107, "bill", 505);

        harness.MigrateToLatest();

        using (var db = harness.CreateContext())
        {
            var captures = db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(c => c.Id)
                .ToDictionary(c => c.Id);

            Assert.NotNull(captures[101].FiledAt);
            Assert.Equal("alice", captures[101].FiledBy);
            Assert.Equal(captures[101].AcceptedAt, captures[101].FiledAt);

            Assert.NotNull(captures[102].FiledAt);
            Assert.Equal("bob", captures[102].FiledBy);
            Assert.Equal(captures[102].AcceptedAt, captures[102].FiledAt);

            Assert.NotNull(captures[103].FiledAt);
            Assert.Equal("carol", captures[103].FiledBy);
            Assert.Equal(captures[103].AcceptedAt, captures[103].FiledAt);

            Assert.Null(captures[104].FiledAt);
            Assert.Null(captures[104].FiledBy);
            Assert.Null(captures[105].FiledAt);
            Assert.Null(captures[106].FiledAt);
            Assert.Null(captures[107].FiledAt);

            var roles = db.DocumentCaptureLinks.IgnoreQueryFilters().AsNoTracking()
                .Select(l => l.Role)
                .Distinct()
                .ToList();
            Assert.Equal(["source"], roles);
        }

        // Because every capture still has at most one 'source' link and no 'document' rows, Down succeeds cleanly.
        harness.MigrateDownToPrior();

        Assert.False(harness.TableExists("DocumentLibraryRevisions"));
        Assert.False(harness.ColumnExists("DocumentCaptures", "FiledAt"));
        Assert.False(harness.ColumnExists("DocumentCaptureLinks", "Role"));
        Assert.True(harness.IndexExists("IX_DocumentCaptureLinks_CompanyId_CaptureId"));
        Assert.Equal(6, harness.CountRows("DocumentCaptureLinks"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EfMigrations_DownRefusesWithoutChangingDataOrSchema_WhenMultiLinksOrAttachmentOrDocumentRowsExist(string provider)
    {
        // Case A: Capture with two links (one source, one attachment)
        using (var multi = new MigrationHarness(provider))
        {
            multi.MigrateToPrior();
            multi.SeedCompany(1, "Multi Co", "multi-co");
            multi.SeedCapturePreMigration(201, 1, "cap_01multi00000000000000001", "purchase-invoice", "accepted", "2026-11-20T00:00:00", "owner", withBlob: true);
            multi.SeedLegacyLink(1, 201, "bill", 701);
            multi.MigrateToLatest();

            multi.ExecuteSql("""
                INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "Role", "LinkedBy", "LinkedAt")
                VALUES (1, 201, 'bill', 702, 'attachment', 'owner', '2026-11-21T00:00:00');
                """);

            Assert.ThrowsAny<Exception>(() => multi.MigrateDownToPrior());

            // Schema and data must remain completely intact.
            Assert.True(multi.TableExists("DocumentLibraryRevisions"));
            Assert.True(multi.ColumnExists("DocumentCaptures", "FiledAt"));
            Assert.True(multi.ColumnExists("DocumentCaptureLinks", "Role"));
            Assert.True(multi.IndexExists("IX_DocumentCaptureLinks_Company_Capture_Target"));
            Assert.True(multi.IndexExists("IX_DocumentCaptureLinks_SourceRole"));
            Assert.False(multi.IndexExists("IX_DocumentCaptureLinks_CompanyId_CaptureId"));
            Assert.Equal(2, multi.CountRows("DocumentCaptureLinks"));
        }

        // Case B: Single link with Role = 'attachment'
        using (var attachOnly = new MigrationHarness(provider))
        {
            attachOnly.MigrateToPrior();
            attachOnly.SeedCompany(1, "Attach Co", "attach-co");
            attachOnly.SeedCapturePreMigration(202, 1, "cap_01attach0000000000000002", "purchase-invoice", "accepted", "2026-11-20T00:00:00", "owner", withBlob: true);
            attachOnly.MigrateToLatest();

            attachOnly.ExecuteSql("""
                INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "Role", "LinkedBy", "LinkedAt")
                VALUES (1, 202, 'bill', 703, 'attachment', 'owner', '2026-11-21T00:00:00');
                """);

            Assert.ThrowsAny<Exception>(() => attachOnly.MigrateDownToPrior());
            Assert.True(attachOnly.TableExists("DocumentLibraryRevisions"));
            Assert.Equal(1, attachOnly.CountRows("DocumentCaptureLinks"));
        }

        // Case C: Row with Kind = 'document'
        using (var docKind = new MigrationHarness(provider))
        {
            docKind.MigrateToPrior();
            docKind.SeedCompany(1, "DocKind Co", "dockind-co");
            docKind.MigrateToLatest();

            docKind.ExecuteSql("""
                INSERT INTO "DocumentCaptures" ("Id", "CompanyId", "PublicId", "Kind", "SchemaVersion", "Status", "SourceHash", "ContentHash", "NormalizationVersion", "ContentType", "ByteCount", "PageCount", "TextPageCount", "RequestFingerprint", "ExtractionGeneration", "CreatedBy", "CreatedAt", "ExpiresAt")
                VALUES (203, 1, 'cap_01doc0000000000000000003', 'document', 1, 'stored', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'pdf-structure-v1', 'application/pdf', 100, 1, 0, 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc', 0, 'owner', '2026-11-20T00:00:00', '2026-11-21T00:00:00');
                """);

            Assert.ThrowsAny<Exception>(() => docKind.MigrateDownToPrior());
            Assert.True(docKind.TableExists("DocumentLibraryRevisions"));
            Assert.Equal(1, docKind.CountRows("DocumentCaptures"));
        }
    }

    [Fact]
    public async Task RealHostStartup_Sqlite_FreshUpgradeWithOrWithoutHistoryIdempotentRestartsAndExplicitRollbackLifecycle()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"accountgo_sa1_host_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1. Fresh SQLite database (no EF history, started via real API host)
            var freshDbPath = Path.Combine(tempDir, "fresh.db");
            await BootHostAsync(freshDbPath);
            using (var conn = OpenSqlite(freshDbPath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(1, ScalarInt(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentLibraryRevisions';"));
                Assert.Equal(0, ScalarInt(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureLinks_CompanyId_CaptureId';"));
            }
            // Restart fresh DB twice (marker 'applied', no EF history -> normal start)
            await BootHostAsync(freshDbPath);
            await BootHostAsync(freshDbPath);

            // 2. Upgrade from pre-S-A1 SQLite database WITHOUT EF history, then restart with multi-links
            var noHistoryUpgradePath = Path.Combine(tempDir, "upgrade_no_history.db");
            await BootHostAsync(noHistoryUpgradePath);
            RevertToPreFoundationSqliteSchemaAndSeedLegacyCapture(noHistoryUpgradePath, withPreexistingEfHistory: false);

            await BootHostAsync(noHistoryUpgradePath);
            using (var conn = OpenSqlite(noHistoryUpgradePath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(0, ScalarInt(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureLinks_CompanyId_CaptureId';"));
                Assert.Equal(1, ScalarInt(conn, "SELECT COUNT(*) FROM \"DocumentCaptures\" WHERE \"Id\" = 901 AND \"FiledAt\" = '2026-11-20T01:00:00' AND \"FiledBy\" = 'approver';"));
                Assert.Equal(1, ScalarInt(conn, "SELECT COUNT(*) FROM \"DocumentCaptureLinks\" WHERE \"CaptureId\" = 901 AND \"Role\" = 'source';"));

                ExecuteSqlite(conn, """
                    INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "Role", "LinkedBy", "LinkedAt")
                    VALUES (1, 901, 'bill', 1002, 'attachment', 'approver', '2026-11-20T02:00:00');
                    """);
            }
            await BootHostAsync(noHistoryUpgradePath);

            // 3. Upgrade from pre-S-A1 SQLite database WITH preexisting __EFMigrationsHistory (older migrations, NO foundation)
            //    -> first boot upgrades & backfills without forging __EFMigrationsHistory -> second and third boots succeed!
            var historyUpgradePath = Path.Combine(tempDir, "upgrade_with_history.db");
            await BootHostAsync(historyUpgradePath);
            RevertToPreFoundationSqliteSchemaAndSeedLegacyCapture(historyUpgradePath, withPreexistingEfHistory: true);

            // First boot: upgrades schema & writes marker 'applied' without touching __EFMigrationsHistory
            await BootHostAsync(historyUpgradePath);
            using (var conn = OpenSqlite(historyUpgradePath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(1, ScalarInt(conn, "SELECT COUNT(*) FROM \"DocumentCaptures\" WHERE \"Id\" = 901 AND \"FiledAt\" = '2026-11-20T01:00:00' AND \"FiledBy\" = 'approver';"));
                // Verify __EFMigrationsHistory was NOT forged with DocumentsLibraryFoundation
                Assert.Equal(1, ScalarInt(conn, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";"));
                Assert.Equal(0, ScalarInt(conn, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%DocumentsLibraryFoundation%';"));

                // Add a second attachment link before subsequent restarts
                ExecuteSqlite(conn, """
                    INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "Role", "LinkedBy", "LinkedAt")
                    VALUES (1, 901, 'bill', 1002, 'attachment', 'approver', '2026-11-20T02:00:00');
                    """);
            }
            // Second and third boots with preexisting EF history + multi-links must succeed cleanly
            await BootHostAsync(historyUpgradePath);
            await BootHostAsync(historyUpgradePath);

            // 4. Legacy 2-column marker table (missing Status column) is upgraded via AddColumnIfMissing and treated as 'applied'
            var twoColMarkerPath = Path.Combine(tempDir, "two_col_marker.db");
            File.Copy(freshDbPath, twoColMarkerPath);
            using (var conn = OpenSqlite(twoColMarkerPath))
            {
                ExecuteSqlite(conn, """
                    DROP TABLE "__AccountGoSchemaMarkers";
                    CREATE TABLE "__AccountGoSchemaMarkers" (
                        "Key" TEXT NOT NULL PRIMARY KEY,
                        "AppliedAt" TEXT NOT NULL
                    );
                    INSERT INTO "__AccountGoSchemaMarkers" ("Key", "AppliedAt")
                    VALUES ('documents-library-v1', '2026-11-26T00:00:00Z');
                    """);
            }
            await BootHostAsync(twoColMarkerPath);
            using (var conn = OpenSqlite(twoColMarkerPath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
            }

            // 5. Real EF Down migration on SQLite -> sets Status='rolled-back' -> next host boot refused -> EF Up -> host boot succeeds;
            //    and real EF Down migration refuses when multi-links exist!
            var rollbackLifecyclePath = Path.Combine(tempDir, "rollback_lifecycle.db");
            await BootHostAsync(rollbackLifecyclePath);

            // Run real EF Down migration on rollbackLifecyclePath (only single/zero links exist, so Down succeeds and marks 'rolled-back')
            ApplyEfMigrationToFile(rollbackLifecyclePath, up: false);
            using (var conn = OpenSqlite(rollbackLifecyclePath))
            {
                Assert.Equal("rolled-back", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(0, ScalarInt(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentLibraryRevisions';"));
            }

            var logs = new List<string>();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BootHostAsync(rollbackLifecyclePath, logs));
            Assert.Contains("rolled-back", ex.Message, StringComparison.Ordinal);
            Assert.Contains("DocumentsLibraryFoundation", ex.Message, StringComparison.Ordinal);
            Assert.Contains(logs, msg => msg.Contains("rolled-back", StringComparison.Ordinal));

            // Confirm the refused startup did NOT silently re-upgrade the rolled-back database
            using (var conn = OpenSqlite(rollbackLifecyclePath))
            {
                Assert.Equal("rolled-back", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(0, ScalarInt(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentLibraryRevisions';"));
            }

            // Migrate forward (EF Up) -> updates marker to 'applied' -> host boots cleanly again!
            ApplyEfMigrationToFile(rollbackLifecyclePath, up: true);
            using (var conn = OpenSqlite(rollbackLifecyclePath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
            }
            await BootHostAsync(rollbackLifecyclePath);

            // Also verify real EF Down refuses on historyUpgradePath (which holds multi-links on capture 901), keeping marker 'applied'
            Assert.ThrowsAny<Exception>(() => ApplyEfMigrationToFile(historyUpgradePath, up: false));
            using (var conn = OpenSqlite(historyUpgradePath))
            {
                Assert.Equal("applied", ScalarText(conn, "SELECT \"Status\" FROM \"__AccountGoSchemaMarkers\" WHERE \"Key\" = 'documents-library-v1';"));
                Assert.Equal(2, ScalarInt(conn, "SELECT COUNT(*) FROM \"DocumentCaptureLinks\" WHERE \"CaptureId\" = 901;"));
            }
            await BootHostAsync(historyUpgradePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void RevertToPreFoundationSqliteSchemaAndSeedLegacyCapture(string dbPath, bool withPreexistingEfHistory)
    {
        using var conn = OpenSqlite(dbPath);
        ExecuteSqlite(conn, """
            PRAGMA foreign_keys = OFF;
            DROP TABLE IF EXISTS "__AccountGoSchemaMarkers";
            DROP TABLE IF EXISTS "DocumentLibraryRevisions";
            DROP INDEX IF EXISTS "IX_DocumentCaptureLinks_Company_Capture_Target";
            DROP INDEX IF EXISTS "IX_DocumentCaptureLinks_SourceRole";
            DROP INDEX IF EXISTS "IX_DocumentCaptures_Filed";
            ALTER TABLE "DocumentCaptureLinks" DROP COLUMN "Role";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "FileName";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "Title";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "Category";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "DocumentDate";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "Notes";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "FiledAt";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "FiledBy";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "ArchivedAt";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "ArchivedBy";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "DeletedAt";
            ALTER TABLE "DocumentCaptures" DROP COLUMN "DeletedBy";
            CREATE UNIQUE INDEX "IX_DocumentCaptureLinks_CompanyId_CaptureId" ON "DocumentCaptureLinks" ("CompanyId", "CaptureId");
            INSERT INTO "DocumentCaptures" ("Id", "CompanyId", "PublicId", "Kind", "SchemaVersion", "Status", "SourceHash", "ContentHash", "NormalizationVersion", "ContentType", "ByteCount", "PageCount", "TextPageCount", "RequestFingerprint", "ExtractionGeneration", "CreatedBy", "CreatedAt", "AcceptedBy", "AcceptedAt", "ExpiresAt")
            VALUES (901, 1, 'cap_01hostupgrade00000000001', 'purchase-invoice', 1, 'accepted', '1111111111111111111111111111111111111111111111111111111111111111', '2222222222222222222222222222222222222222222222222222222222222222', 'v2', 'image/png', 16, 1, 0, '3333333333333333333333333333333333333333333333333333333333333333', 1, 'upgrader', '2026-11-20T00:00:00', 'approver', '2026-11-20T01:00:00', '2026-11-21T00:00:00');
            INSERT INTO "DocumentCaptureBlobs" ("CaptureId", "CompanyId", "DocumentBytes")
            VALUES (901, 1, X'01020304');
            INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "LinkedBy", "LinkedAt")
            VALUES (1, 901, 'bill', 1001, 'approver', '2026-11-20T01:05:00');
            PRAGMA foreign_keys = ON;
            """);

        if (withPreexistingEfHistory)
        {
            ExecuteSqlite(conn, """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20261126040444_BillSourceDocumentType', '10.0.0');
                """);
        }
    }

    private static void ApplyEfMigrationToFile(string dbPath, bool up)
    {
        using var conn = OpenSqlite(dbPath);
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options)
        {
            SuppressTenantSecurityCheck = true
        };
        var migration = new Api.Data.Migrations.ApiDb.DocumentsLibraryFoundation
        {
            ActiveProvider = db.Database.ProviderName!
        };
        var rawTargetModel = up
            ? migration.TargetModel
            : new Api.Data.Migrations.ApiDb.BillSourceDocumentType().TargetModel;
        var targetModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(((Microsoft.EntityFrameworkCore.Metadata.IMutableModel)rawTargetModel).FinalizeModel(), designTime: true);
        var operations = up ? migration.UpOperations : migration.DownOperations;
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel);
        var executor = db.GetService<IMigrationCommandExecutor>();
        var relationalConnection = db.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalConnection>();
        executor.ExecuteNonQuery(commands, relationalConnection);
    }

    private static async Task BootHostAsync(string dbPath, List<string>? capturedErrorLogs = null)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={dbPath}");
            builder.UseSetting("SEED_DEMO_DATA", "false");
            builder.UseSetting("ENVIRONMENT", "Development");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            if (capturedErrorLogs is not null)
            {
                builder.ConfigureServices(services =>
                    services.AddSingleton<ILoggerProvider>(new ListLoggerProvider(capturedErrorLogs)));
            }
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode, $"Expected /health 200 OK, got {(int)response.StatusCode}");
    }

    private static SqliteConnection OpenSqlite(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private static void ExecuteSqlite(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string ScalarText(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private sealed class ListLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(sink);
        public void Dispose() { }

        private sealed class ListLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class MigrationHarness : IDisposable
    {
        private readonly PostgresTestDatabase? _postgres;
        private readonly SqliteConnection? _sqlite;
        public bool IsPostgres => _postgres is not null;
        public DbConnection Connection { get; }

        public MigrationHarness(string provider)
        {
            if (provider == PostgresProvider)
            {
                _postgres = PostgresTestDatabase.CreateIfConfigured()
                    ?? throw new InvalidOperationException("Postgres not configured.");
                var pg = new NpgsqlConnection(_postgres.ConnectionString);
                pg.Open();
                Connection = pg;
            }
            else
            {
                _sqlite = new SqliteConnection("Data Source=:memory:");
                _sqlite.Open();
                Connection = _sqlite;
            }
        }

        public ApiDbContext CreateContext()
        {
            if (_postgres is not null)
            {
                var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                    .UseNpgsql(_postgres.ConnectionString)
                    .Options;
                return new PostgresApiDbContext(options) { SuppressTenantSecurityCheck = true };
            }

            var sqliteOptions = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite(_sqlite!)
                .Options;
            return new ApiDbContext(sqliteOptions) { SuppressTenantSecurityCheck = true };
        }

        public void MigrateToLatest()
        {
            if (IsPostgres)
            {
                using var db = CreateContext();
                db.Database.Migrate();
                return;
            }

            EnsureSqlitePreSchemaIfNeeded();
            ApplySqliteMigration(up: true);
        }

        public void MigrateToPrior()
        {
            if (IsPostgres)
            {
                using var db = CreateContext();
                var migrations = db.Database.GetMigrations().ToList();
                var idx = migrations.FindIndex(m => m.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
                Assert.True(idx > 0, "DocumentsLibraryFoundation migration not found in Postgres migrations.");
                db.GetService<IMigrator>().Migrate(migrations[idx - 1]);
                ExecuteSql("""TRUNCATE TABLE "Company" CASCADE;""");
                return;
            }

            EnsureSqlitePreSchemaIfNeeded();
        }

        public void MigrateDownToPrior()
        {
            if (IsPostgres)
            {
                using var db = CreateContext();
                var migrations = db.Database.GetMigrations().ToList();
                var idx = migrations.FindIndex(m => m.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
                Assert.True(idx > 0);
                db.GetService<IMigrator>().Migrate(migrations[idx - 1]);
                return;
            }

            ApplySqliteMigration(up: false);
        }

        private void EnsureSqlitePreSchemaIfNeeded()
        {
            if (TableExists("DocumentCaptures"))
                return;

            // Build current schema via EnsureCreated, clear seed rows, and step back to the exact pre-S-A1 capture schema.
            using (var db = CreateContext())
            {
                db.Database.EnsureCreated();
            }

            ExecuteSql("""
                PRAGMA foreign_keys = OFF;
                DELETE FROM "Company";
                DROP TABLE IF EXISTS "DocumentLibraryRevisions";
                DROP INDEX IF EXISTS "IX_DocumentCaptureLinks_Company_Capture_Target";
                DROP INDEX IF EXISTS "IX_DocumentCaptureLinks_SourceRole";
                DROP INDEX IF EXISTS "IX_DocumentCaptures_Filed";
                ALTER TABLE "DocumentCaptureLinks" DROP COLUMN "Role";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "FileName";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "Title";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "Category";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "DocumentDate";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "Notes";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "FiledAt";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "FiledBy";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "ArchivedAt";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "ArchivedBy";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "DeletedAt";
                ALTER TABLE "DocumentCaptures" DROP COLUMN "DeletedBy";
                CREATE UNIQUE INDEX "IX_DocumentCaptureLinks_CompanyId_CaptureId" ON "DocumentCaptureLinks" ("CompanyId", "CaptureId");
                PRAGMA foreign_keys = ON;
                """);
        }

        private void ApplySqliteMigration(bool up)
        {
            using var db = CreateContext();
            var migration = new Api.Data.Migrations.ApiDb.DocumentsLibraryFoundation
            {
                ActiveProvider = db.Database.ProviderName!
            };
            var rawTargetModel = up
                ? migration.TargetModel
                : new Api.Data.Migrations.ApiDb.BillSourceDocumentType().TargetModel;
            var targetModel = db.GetService<IModelRuntimeInitializer>()
                .Initialize(((Microsoft.EntityFrameworkCore.Metadata.IMutableModel)rawTargetModel).FinalizeModel(), designTime: true);
            var operations = up ? migration.UpOperations : migration.DownOperations;
            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel);
            var executor = db.GetService<IMigrationCommandExecutor>();
            var relationalConnection = db.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalConnection>();
            executor.ExecuteNonQuery(commands, relationalConnection);
        }

        public void SeedCompany(int id, string name, string slug)
        {
            var boolFalse = IsPostgres ? "false" : "0";
            ExecuteSql($"""
                INSERT INTO "Company" ("Id", "Name", "ShortName", "Slug", "PublicId", "ActivationState", "AccessBootstrapAvailable", "CancelAtPeriodEnd", "BillingManagedManually", "CreatedAt")
                VALUES ({id}, '{name}', '{slug}', '{slug}', 'ws_01testcompany00000000000{id}', 'Active', {boolFalse}, {boolFalse}, {boolFalse}, '2026-11-01T00:00:00');
                """);
        }

        public void SeedCapturePreMigration(int id, int companyId, string publicId, string kind, string status,
            string acceptedAt, string acceptedBy, bool withBlob)
        {
            ExecuteSql($"""
                INSERT INTO "DocumentCaptures" ("Id", "CompanyId", "PublicId", "Kind", "SchemaVersion", "Status", "SourceHash", "ContentHash", "NormalizationVersion", "ContentType", "ByteCount", "PageCount", "TextPageCount", "RequestFingerprint", "ExtractionGeneration", "CreatedBy", "CreatedAt", "AcceptedBy", "AcceptedAt", "ExpiresAt")
                VALUES ({id}, {companyId}, '{publicId}', '{kind}', 1, '{status}', '{new string('a', 64)}', '{new string('b', 64)}', 'image-normalization-v2', 'image/png', 4, 1, 0, '{new string('c', 64)}', 1, '{acceptedBy}', '2026-11-01T00:00:00', '{acceptedBy}', '{acceptedAt}', '2026-11-02T00:00:00');
                """);
            if (withBlob)
            {
                var bytesLiteral = IsPostgres ? "'\\x01020304'::bytea" : "X'01020304'";
                ExecuteSql($"""
                    INSERT INTO "DocumentCaptureBlobs" ("CaptureId", "CompanyId", "DocumentBytes")
                    VALUES ({id}, {companyId}, {bytesLiteral});
                    """);
            }
        }

        public void SeedLegacyLink(int companyId, int captureId, string targetType, int targetId)
        {
            ExecuteSql($"""
                INSERT INTO "DocumentCaptureLinks" ("CompanyId", "CaptureId", "TargetType", "TargetId", "LinkedBy", "LinkedAt")
                VALUES ({companyId}, {captureId}, '{targetType}', {targetId}, 'linker', '2026-11-10T12:05:00');
                """);
        }

        public void ExecuteSql(string sql)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public int CountRows(string table)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public bool TableExists(string table)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = IsPostgres
                ? $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}';"
                : $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public bool ColumnExists(string table, string column)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = IsPostgres
                ? $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}';"
                : $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public bool IndexExists(string indexName)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = IsPostgres
                ? $"SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{indexName}';"
                : $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{indexName}';";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public void Dispose()
        {
            Connection.Dispose();
            _postgres?.Dispose();
        }
    }
}
