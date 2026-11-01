// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AccountGo.BackendTests.Architecture;

/// <summary>
/// SQL Server installs run <c>Database.Migrate()</c>, which needs the model snapshot to match the model.
/// Hand-written migrations must update the snapshot; otherwise the next generated migration re-creates
/// tables and columns they already added. No database connection is opened.
/// </summary>
public class MigrationSnapshotTests
{
    [Fact]
    public void SqlServer_migrations_generate_a_complete_install_script()
    {
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options);

        Assert.NotEmpty(db.GetService<IMigrator>().GenerateScript());
    }

    [Fact]
    public void SqlServer_migrations_never_create_multiple_company_cascade_paths_to_access_requests()
    {
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var foreignKeys = new Dictionary<(string Table, string Name), AddForeignKeyOperation>();
        foreach (var migration in assembly.Migrations.OrderBy(m => m.Key))
        {
            foreach (var operation in assembly.CreateMigration(migration.Value, db.Database.ProviderName!).UpOperations)
            {
                if (operation is DropForeignKeyOperation drop)
                    foreignKeys.Remove((drop.Table, drop.Name));
                var additions = operation is CreateTableOperation table ? table.ForeignKeys
                    : operation is AddForeignKeyOperation fk ? new[] { fk }.ToList()
                    : new List<AddForeignKeyOperation>();
                foreach (var addition in additions)
                    foreignKeys[(addition.Table, addition.Name)] = addition;
            }

            int CountPaths(string table, HashSet<string> visited)
            {
                if (table == "Company") return 1;
                if (!visited.Add(table)) return 0;
                return foreignKeys.Values.Where(fk => fk.Table == table && fk.OnDelete == ReferentialAction.Cascade)
                    .Sum(fk => CountPaths(fk.PrincipalTable, new HashSet<string>(visited)));
            }
            Assert.True(CountPaths("WorkspaceAccessRequest", new HashSet<string>()) <= 1,
                $"Migration {migration.Key} creates multiple cascade paths from Company to WorkspaceAccessRequest.");
        }
    }

    [Fact]
    public void ApiDb_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var db = new ApiDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void IdentityDb_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var db = new ApplicationIdentityDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    // Postgres has its own migrations (Data/Migrations/Postgres): a schema change needs one there too.
    [Fact]
    public void Postgres_ApiDb_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options;
        using var db = new PostgresApiDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Postgres_IdentityDb_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options;
        using var db = new PostgresIdentityDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
