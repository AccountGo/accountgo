// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Api.Data;
using Api.Data.Postgres;
using Core.Domain.Financials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public sealed class YearEndCloseEvidenceTests
{
    [Fact]
    public void Evidence_rows_cannot_be_changed_or_removed()
    {
        using var db = Create();
        db.YearEndCloseEvidence.Add(new YearEndCloseEvidence
        {
            CompanyId = 1,
            YearEndCloseRunId = 1,
            SchemaVersion = 1,
            ContentDigest = new string('a', 64),
            Payload = "schema=1"
        });
        db.SaveChanges();

        db.YearEndCloseEvidence.IgnoreQueryFilters().Single().Payload = "changed";
        var update = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", update.Message, StringComparison.OrdinalIgnoreCase);

        db.ChangeTracker.Clear();
        db.YearEndCloseEvidence.Remove(db.YearEndCloseEvidence.IgnoreQueryFilters().Single());
        var delete = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", delete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protected_writes_advance_the_close_revision_once_per_save()
    {
        using var db = Create();
        db.FinancialYears.Add(Year("FY2099"));
        db.SaveChanges();
        Assert.Equal(1, db.CompanyCloseStates.IgnoreQueryFilters().Single().AccountingRevision);

        var year = db.FinancialYears.IgnoreQueryFilters().Single(y => y.FiscalYearCode == "FY2099");
        year.FiscalYearName = "Renamed";
        db.FinancialYears.Add(Year("FY2100"));
        db.SaveChanges();
        db.ChangeTracker.Clear();
        Assert.Equal(2, db.CompanyCloseStates.IgnoreQueryFilters().Single().AccountingRevision);
    }

    [Fact]
    public void Postgres_rejects_a_direct_evidence_update_when_configured()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        using var db = new PostgresApiDbContext(options);
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw(
            """
            INSERT INTO "YearEndCloseEvidence" ("CompanyId", "YearEndCloseRunId", "SchemaVersion", "ContentDigest", "Payload")
            VALUES (1, 1, 1, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'schema=1')
            """);
        var rejected = Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw(
            "UPDATE \"YearEndCloseEvidence\" SET \"Payload\" = 'changed' WHERE \"CompanyId\" = 1"));
        Assert.Contains("append-only", rejected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static ApiDbContext Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=OFF";
        command.ExecuteNonQuery();
        return db;
    }

    private static FinancialYear Year(string code) => new()
    {
        CompanyId = 1,
        FiscalYearCode = code,
        FiscalYearName = code,
        StartDate = new DateTime(2099, 1, 1),
        EndDate = new DateTime(2099, 12, 31),
        IsActive = false
    };
}
