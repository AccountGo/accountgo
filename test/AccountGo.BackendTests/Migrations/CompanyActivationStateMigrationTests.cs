// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Core.Domain;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

/// <summary>ActivationState column applies on SQLite upgrader and Postgres migrations when configured.</summary>
public sealed class CompanyActivationStateMigrationTests
{
    private const string SqliteProvider = "Sqlite";
    private const string PostgresProvider = "Postgres";

    public static IEnumerable<object[]> Providers()
    {
        yield return new object[] { SqliteProvider };
        if (PostgresTestDatabase.IsConfigured)
            yield return new object[] { PostgresProvider };
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ActivationState_Column_Exists_WithActiveDefault(string provider)
    {
        using var harness = new Harness(provider);
        harness.EnsureCurrentSchema();

        using var connection = harness.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = harness.IsPostgres
            ? @"SELECT column_default FROM information_schema.columns
                WHERE table_name = 'Company' AND column_name = 'ActivationState'"
            : @"SELECT sql FROM sqlite_master WHERE type='table' AND name='Company'";
        var result = cmd.ExecuteScalar()?.ToString() ?? "";
        Assert.Contains("active", result, StringComparison.OrdinalIgnoreCase);

        if (!harness.IsPostgres)
        {
            Assert.Contains("ActivationState", result, StringComparison.Ordinal);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly PostgresTestDatabase? _postgres;
        private readonly string _sqlitePath;

        public Harness(string provider)
        {
            Assert.True(provider is SqliteProvider or PostgresProvider, "Unknown provider " + provider);
            if (provider == PostgresProvider)
            {
                _postgres = PostgresTestDatabase.CreateIfConfigured()
                    ?? throw new InvalidOperationException(PostgresTestDatabase.EnvironmentVariable + " is not set.");
                _sqlitePath = "";
            }
            else
            {
                _sqlitePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "actstate_" + Guid.NewGuid().ToString("N") + ".db");
            }
        }

        public bool IsPostgres => _postgres != null;

        public void EnsureCurrentSchema()
        {
            if (IsPostgres)
            {
                var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                    .UseNpgsql(_postgres!.ConnectionString)
                    .Options;
                using var db = new PostgresApiDbContext(options, new Mock<ITenantContext>().Object);
                db.Database.Migrate();
                return;
            }

            var sqliteOptions = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite($"Data Source={_sqlitePath}")
                .Options;
            using var sqlite = new ApiDbContext(sqliteOptions, new Mock<ITenantContext>().Object);
            sqlite.Database.EnsureCreated();
            using var conn = sqlite.Database.GetDbConnection();
            conn.Open();
            SqliteSchemaUpgrader.Upgrade(conn);
        }

        public System.Data.Common.DbConnection OpenConnection()
        {
            if (IsPostgres)
            {
                var conn = new Npgsql.NpgsqlConnection(_postgres!.ConnectionString);
                conn.Open();
                return conn;
            }

            var sqlite = new SqliteConnection($"Data Source={_sqlitePath}");
            sqlite.Open();
            return sqlite;
        }

        public void Dispose()
        {
            _postgres?.Dispose();
            if (!string.IsNullOrEmpty(_sqlitePath) && System.IO.File.Exists(_sqlitePath))
            {
                try { System.IO.File.Delete(_sqlitePath); } catch { /* best effort */ }
            }
        }
    }
}
