// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class PublicIdMigrationTests
{
    [Fact]
    public void EfMigrations_HaveOnlyAdditiveOperationsAndNoTableRebuilds()
    {
        var apiMigration = new Api.Data.Migrations.ApiDb.AddPublicIds();
        var idMigration = new Api.Data.Migrations.IdentityDb.AddPublicIds();
        var pgApiMigration = new Api.Data.Migrations.Postgres.ApiDb.AddPublicIds();
        var pgIdMigration = new Api.Data.Migrations.Postgres.IdentityDb.AddPublicIds();

        foreach (var (name, ops) in new (string, IReadOnlyList<MigrationOperation>)[]
        {
            ("ApiDb", apiMigration.UpOperations),
            ("IdentityDb", idMigration.UpOperations),
            ("PostgresApiDb", pgApiMigration.UpOperations),
            ("PostgresIdentityDb", pgIdMigration.UpOperations),
        })
        {
            Assert.NotEmpty(ops);
            foreach (var op in ops)
            {
                Assert.True(
                    op is AddColumnOperation or CreateIndexOperation or SqlOperation or UpdateDataOperation,
                    $"Migration '{name}' contains unexpected operation {op.GetType().Name} (possible table rebuild).");
            }
        }
    }

    [Fact]
    public async Task Sqlite_AssignsUlidOnInsert_BlocksModification_AllowsEmptyRollbackAndRepairs()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var apiOpts = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
        var idOpts = new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(conn).Options;

        await using var apiDb = new ApiDbContext(apiOpts);
        apiDb.SuppressTenantSecurityCheck = true;
        var apiCreator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)apiDb.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await apiCreator.CreateTablesAsync();

        await using var idDb = new ApplicationIdentityDbContext(idOpts);
        var idCreator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)idDb.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        await idCreator.CreateTablesAsync();

        // 1. Insert assigns valid ULID
        var company = new Company { Name = "Acme", Slug = "acme-test" };
        apiDb.Companies.Add(company);
        await apiDb.SaveChangesAsync();
        Assert.StartsWith("ws_", company.PublicId, StringComparison.Ordinal);
        Assert.True(PublicIdGenerator.IsValidFormat(company.PublicId));

        var session = new RefreshTokenSession
        {
            UserId = "u1",
            FamilyId = "f1",
            TokenHash = "h1",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        idDb.RefreshTokens.Add(session);
        await idDb.SaveChangesAsync();
        Assert.StartsWith("sess_", session.PublicId, StringComparison.Ordinal);
        Assert.True(PublicIdGenerator.IsValidFormat(session.PublicId));

        // 2. Modifying PublicId throws
        company.PublicId = PublicIdGenerator.New("ws");
        await Assert.ThrowsAsync<InvalidOperationException>(() => apiDb.SaveChangesAsync());
        apiDb.Entry(company).Reload();

        session.PublicId = PublicIdGenerator.New("sess");
        await Assert.ThrowsAsync<InvalidOperationException>(() => idDb.SaveChangesAsync());
        idDb.Entry(session).Reload();

        // 3. Rollback simulation: two raw SQL inserts with empty PublicId succeed under filtered unique index
        await apiDb.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""Company"" (""Name"", ""ShortName"", ""Slug"", ""PublicId"", ""ActivationState"", ""AccessBootstrapAvailable"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
            VALUES ('Rollback1', 'RB1', 'rb-1', '', 'Active', 0, 0, 0, '2026-01-01T00:00:00Z');
            INSERT INTO ""Company"" (""Name"", ""ShortName"", ""Slug"", ""PublicId"", ""ActivationState"", ""AccessBootstrapAvailable"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
            VALUES ('Rollback2', 'RB2', 'rb-2', '', 'Active', 0, 0, 0, '2026-01-01T00:00:00Z');");

        // 4. Repair command repairs both empty rows
        var repaired = PublicIdBackfillSql.RepairContext(apiDb, isIdentity: false);
        Assert.True(repaired >= 2);

        var companies = await apiDb.Companies.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(companies.Count, companies.Select(c => c.PublicId).Distinct().Count());
        foreach (var c in companies)
        {
            Assert.StartsWith("ws_", c.PublicId, StringComparison.Ordinal);
            Assert.True(PublicIdGenerator.IsValidFormat(c.PublicId));
        }

        // 5. SqliteSchemaUpgrader upgrades an existing table without PublicId column
        await using var legacyConn = new SqliteConnection("Data Source=:memory:");
        await legacyConn.OpenAsync();
        await using (var cmd = legacyConn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Company"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL);
                INSERT INTO ""Company"" (""Name"") VALUES ('LegacyCo1'), ('LegacyCo2');";
            await cmd.ExecuteNonQueryAsync();
        }
        SqliteSchemaUpgrader.EnsurePublicIdForTablePublic(legacyConn, "Company", "ws");
        await using (var cmd = legacyConn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""PublicId"" FROM ""Company"";";
            await using var reader = await cmd.ExecuteReaderAsync();
            var legacyIds = new List<string>();
            while (await reader.ReadAsync()) legacyIds.Add(reader.GetString(0));
            Assert.Equal(2, legacyIds.Count);
            Assert.Equal(2, legacyIds.Distinct().Count());
            Assert.All(legacyIds, id => Assert.True(PublicIdGenerator.IsValidFormat(id) && id.StartsWith("ws_", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task Postgres_MigratesExistingRows_AssignsUlid_BlocksModification_AndRepairsEmptyRows()
    {
        var cs = Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_POSTGRES_CS");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        // Create an isolated database for this test run
        var builder = new NpgsqlConnectionStringBuilder(cs);
        var dbName = $"accountgo_pubid_test_{Guid.NewGuid():N}"[..36];
        var adminCs = new NpgsqlConnectionStringBuilder(cs) { Database = "postgres" }.ConnectionString;

        await using (var admin = new NpgsqlConnection(adminCs))
        {
            await admin.OpenAsync();
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\";", admin);
            await createCmd.ExecuteNonQueryAsync();
        }

        var testCs = new NpgsqlConnectionStringBuilder(cs) { Database = dbName }.ConnectionString;
        try
        {
            var apiOpts = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(testCs).Options;
            var idOpts = new DbContextOptionsBuilder<PostgresIdentityDbContext>().UseNpgsql(testCs).Options;

            // Migrate to the migration BEFORE AddPublicIds
            await using (var apiDb = new PostgresApiDbContext(apiOpts))
            await using (var idDb = new PostgresIdentityDbContext(idOpts))
            {
                var apiMigrator = apiDb.GetService<IMigrator>();
                var apiMigrations = apiDb.Database.GetMigrations().ToList();
                var prevApi = apiMigrations[^2];
                await apiMigrator.MigrateAsync(prevApi);

                var idMigrator = idDb.GetService<IMigrator>();
                var idMigrations = idDb.Database.GetMigrations().ToList();
                var prevId = idMigrations[^2];
                await idMigrator.MigrateAsync(prevId);

                // Seed pre-migration rows via raw SQL
                await apiDb.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""Company"" (""Name"", ""ShortName"", ""Slug"", ""ActivationState"", ""AccessBootstrapAvailable"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                    VALUES ('PreMigCo', 'PMC', 'premig-co', 'Active', FALSE, FALSE, FALSE, TIMESTAMP '2026-01-01 00:00:00');");
                await idDb.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""RefreshTokens"" (""UserId"", ""FamilyId"", ""TokenHash"", ""ExpiresAt"", ""CreatedAt"", ""GraceReuseCount"", ""Channel"")
                    VALUES ('u1', 'f1', 'h1', TIMESTAMP '2026-12-01 00:00:00', TIMESTAMP '2026-01-01 00:00:00', 0, 'web');");
            }

            // Now migrate to latest (AddPublicIds)
            await using (var apiDb = new PostgresApiDbContext(apiOpts))
            await using (var idDb = new PostgresIdentityDbContext(idOpts))
            {
                apiDb.SuppressTenantSecurityCheck = true;
                await apiDb.Database.MigrateAsync();
                await idDb.Database.MigrateAsync();

                // Verify backfilled rows
                var preMigCo = await apiDb.Companies.SingleAsync(c => c.Slug == "premig-co");
                Assert.StartsWith("ws_", preMigCo.PublicId, StringComparison.Ordinal);
                Assert.True(PublicIdGenerator.IsValidFormat(preMigCo.PublicId));

                var preMigSess = await idDb.RefreshTokens.SingleAsync(r => r.FamilyId == "f1");
                Assert.StartsWith("sess_", preMigSess.PublicId, StringComparison.Ordinal);
                Assert.True(PublicIdGenerator.IsValidFormat(preMigSess.PublicId));

                // Insert after migration gets ULID
                var newCo = new Company { Name = "PostMigCo", Slug = "postmig-co" };
                apiDb.Companies.Add(newCo);
                await apiDb.SaveChangesAsync();
                Assert.StartsWith("ws_", newCo.PublicId, StringComparison.Ordinal);
                Assert.True(PublicIdGenerator.IsValidFormat(newCo.PublicId));

                // Modifying PublicId throws
                newCo.PublicId = PublicIdGenerator.New("ws");
                await Assert.ThrowsAsync<InvalidOperationException>(() => apiDb.SaveChangesAsync());
                await apiDb.Entry(newCo).ReloadAsync();

                // Rollback simulation: two raw SQL inserts with empty PublicId succeed
                await apiDb.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""Company"" (""Name"", ""ShortName"", ""Slug"", ""PublicId"", ""ActivationState"", ""AccessBootstrapAvailable"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                    VALUES ('PgRb1', 'PR1', 'pgrb-1', '', 'Active', FALSE, FALSE, FALSE, TIMESTAMP '2026-01-01 00:00:00');
                    INSERT INTO ""Company"" (""Name"", ""ShortName"", ""Slug"", ""PublicId"", ""ActivationState"", ""AccessBootstrapAvailable"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                    VALUES ('PgRb2', 'PR2', 'pgrb-2', '', 'Active', FALSE, FALSE, FALSE, TIMESTAMP '2026-01-01 00:00:00');");

                var repaired = PublicIdBackfillSql.RepairContext(apiDb, isIdentity: false);
                Assert.Equal(2, repaired);

                var allCos = await apiDb.Companies.AsNoTracking().ToListAsync();
                Assert.Equal(allCos.Count, allCos.Select(c => c.PublicId).Distinct().Count());
                Assert.All(allCos, c => Assert.True(PublicIdGenerator.IsValidFormat(c.PublicId)));
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminCs);
            await admin.OpenAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);", admin);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }
}
