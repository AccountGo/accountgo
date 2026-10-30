// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore;
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
