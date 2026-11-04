// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>A fact that needs a disposable PostgreSQL server (ACCOUNTGO_TEST_POSTGRES) and the psql/pg_dump/pg_restore client tools.</summary>
public sealed class PostgresClientToolsFactAttribute : FactAttribute
{
    public PostgresClientToolsFactAttribute()
    {
        if (!PostgresRaceDatabase.IsConfigured)
            Skip = "Needs ACCOUNTGO_TEST_POSTGRES.";
        else if (new[] { "psql", "pg_dump", "pg_restore" }.Any(tool => !PersonalWorkspaceConversionScriptTests.IsOnPath(tool)))
            Skip = "Needs psql, pg_dump and pg_restore on PATH.";
    }
}

/// <summary>
/// Rehearses scripts/personal-workspace-conversion.sh against a disposable PostgreSQL database holding a synthetic,
/// representative workspace. No production data or access is involved; every amount below is invented.
/// </summary>
public sealed class PersonalWorkspaceConversionScriptTests
{
    private const int CompanyMemberCount = 2;

    internal static bool IsOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, tool)));
    }

    private sealed record ScriptResult(int ExitCode, string Output);

    private static string ScriptPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "scripts", "personal-workspace-conversion.sh");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("scripts/personal-workspace-conversion.sh was not found above the test output directory.");
    }

    private static string ToLibpqUri(string npgsqlConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(npgsqlConnectionString);
        return $"postgresql://{Uri.EscapeDataString(builder.Username!)}:{Uri.EscapeDataString(builder.Password!)}@{builder.Host}:{builder.Port}/{builder.Database}";
    }

    private static ScriptResult RunScript(string databaseUri, params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(ScriptPath());
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["DATABASE_URL"] = databaseUri;

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, standardOutput.Result + standardError.Result);
    }

    private sealed class Fixture : IDisposable
    {
        public PolicyRaceDatabase Database { get; }
        public string DatabaseUri { get; }
        public string Slug { get; } = "conv-" + Guid.NewGuid().ToString("N")[..8];
        public string OutputDirectory { get; } = Path.Combine(Path.GetTempPath(), "conversion-test-" + Guid.NewGuid().ToString("N"));
        public int CompanyId { get; private set; }

        public Fixture(EntityType entityType)
        {
            Database = PolicyRaceDatabase.Create(PolicyRaceDatabase.Postgres);
            DatabaseUri = ToLibpqUri(Database.PostgresConnectionString!);
            Directory.CreateDirectory(OutputDirectory);
            Seed(entityType);
        }

        private void Seed(EntityType entityType)
        {
            var tenant = new TenantContext();
            using var context = Database.CreateContext(tenant);
            var db = context.Db;
            var company = new Company { Name = "Conversion Rehearsal", ShortName = "CR", Slug = Slug, CreatedByUserId = "owner-1", Plan = "free" };
            db.Companies.Add(company);
            db.SaveChanges();
            CompanyId = company.Id;

            new CompanyProvisioningService(db, tenant).Provision(company, new CompanyProvisionOptions { EntityType = entityType });

            using (tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
            {
                for (var index = 0; index < CompanyMemberCount; index++)
                {
                    db.CompanyMembers.Add(new CompanyMember
                    {
                        CompanyId = company.Id,
                        UserId = "member-" + index,
                        Role = index == 0 ? CompanyMemberRole.Owner : CompanyMemberRole.Member,
                        Status = CompanyMemberStatus.Active,
                        JoinedAt = DateTime.UtcNow
                    });
                }
                db.SaveChanges();

                var accounts = db.Accounts.ToDictionary(a => a.AccountCode);
                Post(db, company.Id, accounts, "10111", "30700", 5000m);
                Post(db, company.Id, accounts, "30700", "30100", 5000m);
                Post(db, company.Id, accounts, "10111", "40100", 2000m);
                Post(db, company.Id, accounts, "50100", "10111", 700m);
                Post(db, company.Id, accounts, "30800", "10111", 300m);
                db.SaveChanges();
            }
        }

        private static void Post(ApiDbContext db, int companyId, System.Collections.Generic.Dictionary<string, Account> accounts, string debitCode, string creditCode, decimal amount)
        {
            var header = new GeneralLedgerHeader
            {
                CompanyId = companyId,
                Date = new DateTime(DateTime.UtcNow.Year, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                DocumentType = DocumentTypes.JournalEntry,
                Description = "synthetic rehearsal entry"
            };
            header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = accounts[debitCode].Id, DrCr = DrOrCrSide.Dr, Amount = amount });
            header.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = companyId, AccountId = accounts[creditCode].Id, DrCr = DrOrCrSide.Cr, Amount = amount });
            db.GeneralLedgerHeaders.Add(header);
        }

        public static void PostForDrift(ApiDbContext db, int companyId, System.Collections.Generic.Dictionary<string, Account> accounts) =>
            Post(db, companyId, accounts, "10111", "40100", 1m);

        public (EntityType EntityType, int? PermanentAccountId, string CapitalName, string DrawingsName, int LedgerLines, int PolicyRows) Read()
        {
            using var context = Database.CreateContext(new TenantContext());
            var db = context.Db;
            var setting = db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == CompanyId);
            var glSetting = db.GeneralLedgerSettings.IgnoreQueryFilters().Single(s => s.CompanyId == CompanyId);
            var accounts = db.Accounts.IgnoreQueryFilters().Where(a => a.CompanyId == CompanyId).ToList();
            return (
                setting.EntityType,
                glSetting.PermanentAccountId,
                accounts.Single(a => a.AccountCode == "30100").AccountName,
                accounts.Single(a => a.AccountCode == "30800").AccountName,
                db.GeneralLedgerLines.IgnoreQueryFilters().Count(l => l.CompanyId == CompanyId),
                db.AuditLogs.IgnoreQueryFilters().Count(a => a.CompanyId == CompanyId && a.TableName == "__PersonalWorkspaceConversion"));
        }

        public string Snapshot(string fileName)
        {
            var file = Path.Combine(OutputDirectory, fileName);
            var result = RunScript(DatabaseUri, "snapshot", "--slug", Slug, "--out-dir", OutputDirectory, "--snapshot", file);
            Assert.True(result.ExitCode == 0, result.Output);
            return file;
        }

        public void Dispose()
        {
            Database.Dispose();
            if (Directory.Exists(OutputDirectory))
                Directory.Delete(OutputDirectory, recursive: true);
        }
    }

    private static JsonNode Section(string snapshotFile, string name) =>
        JsonNode.Parse(File.ReadAllText(snapshotFile))![name]!;

    [PostgresClientToolsFact]
    public void Conversion_dry_run_apply_verify_restore_and_backup_restore_are_reconciled_and_reversible()
    {
        using var fixture = new Fixture(EntityType.SoleProprietor);
        var before = fixture.Read();
        Assert.Equal(EntityType.SoleProprietor, before.EntityType);
        Assert.Equal("Owner's Capital", before.CapitalName);

        // Read-only steps.
        var inspect = RunScript(fixture.DatabaseUri, "inspect", "--slug", fixture.Slug);
        Assert.True(inspect.ExitCode == 0, inspect.Output);
        var dryRun = RunScript(fixture.DatabaseUri, "dry-run", "--slug", fixture.Slug);
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Contains("CHANGE CompanySetting.EntityType: 1 -> 6", dryRun.Output);
        Assert.Contains("CHANGE Account.30100.AccountName: Owner's Capital -> Accumulated net worth", dryRun.Output);
        Assert.DoesNotContain("CHANGE Account.30800", dryRun.Output);
        Assert.Contains("WARNING account 30800 is left unchanged", dryRun.Output);
        Assert.Contains("WARNING workspace purpose is not Personal", dryRun.Output);
        Assert.Contains("DRYRUN_RESULT needs-accept-warnings", dryRun.Output);
        Assert.Equal(before, fixture.Read());

        // Backup and snapshot before any write.
        var backup = RunScript(fixture.DatabaseUri, "backup", "--slug", fixture.Slug, "--out-dir", fixture.OutputDirectory);
        Assert.True(backup.ExitCode == 0, backup.Output);
        var backupFile = Directory.GetFiles(fixture.OutputDirectory, "*.dump").Single();
        var snapshotFile = fixture.Snapshot("original.json");

        // Apply is refused without each explicit safeguard, and refusals leave the database untouched.
        string[] common = { "apply", "--slug", fixture.Slug, "--snapshot", snapshotFile };
        Assert.Equal(2, RunScript(fixture.DatabaseUri, common.Concat(new[] { "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings" }).ToArray()).ExitCode);
        Assert.Equal(2, RunScript(fixture.DatabaseUri, common.Concat(new[] { "--confirm-write", "--backup-file", backupFile, "--accept-warnings" }).ToArray()).ExitCode);
        Assert.Equal(3, RunScript(fixture.DatabaseUri, common.Concat(new[] { "--confirm-write", "--approval-ref", "FIN-1", "--accept-warnings" }).ToArray()).ExitCode);
        var warningsRefused = RunScript(fixture.DatabaseUri, common.Concat(new[] { "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile }).ToArray());
        Assert.NotEqual(0, warningsRefused.ExitCode);
        Assert.Contains("warnings present", warningsRefused.Output);
        Assert.Equal(before, fixture.Read());

        // Apply.
        var applied = RunScript(fixture.DatabaseUri, common.Concat(new[] { "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings" }).ToArray());
        Assert.True(applied.ExitCode == 0, applied.Output);
        Assert.Contains("APPLY_RESULT converted", applied.Output);
        var converted = fixture.Read();
        Assert.Equal(EntityType.Individual, converted.EntityType);
        Assert.Equal("Accumulated net worth", converted.CapitalName);
        Assert.Equal(before.DrawingsName, converted.DrawingsName);
        Assert.Equal(before.LedgerLines, converted.LedgerLines);
        Assert.Equal(before.PermanentAccountId, converted.PermanentAccountId);
        Assert.Equal(before.PolicyRows + 1, converted.PolicyRows);

        // Reconciliation and idempotence.
        var verified = RunScript(fixture.DatabaseUri, "verify", "--slug", fixture.Slug, "--snapshot", snapshotFile);
        Assert.True(verified.ExitCode == 0, verified.Output);
        Assert.Contains("VERIFY_RESULT pass", verified.Output);
        Assert.Contains("DRYRUN_RESULT already-converted", RunScript(fixture.DatabaseUri, "dry-run", "--slug", fixture.Slug).Output);
        var staleReapply = RunScript(fixture.DatabaseUri, common.Concat(new[] { "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings" }).ToArray());
        Assert.NotEqual(0, staleReapply.ExitCode);
        Assert.Contains("state changed since the snapshot", staleReapply.Output);
        var convertedSnapshot = fixture.Snapshot("converted.json");
        var noOp = RunScript(fixture.DatabaseUri, "apply", "--slug", fixture.Slug, "--snapshot", convertedSnapshot,
            "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings");
        Assert.True(noOp.ExitCode == 0, noOp.Output);
        Assert.Contains("APPLY_RESULT already-converted", noOp.Output);
        Assert.Equal(converted, fixture.Read());

        // Reversal.
        Assert.Equal(2, RunScript(fixture.DatabaseUri, "restore", "--slug", fixture.Slug, "--snapshot", snapshotFile, "--reason", "rehearsal").ExitCode);
        var restored = RunScript(fixture.DatabaseUri, "restore", "--slug", fixture.Slug, "--snapshot", snapshotFile, "--confirm-write", "--reason", "rehearsal");
        Assert.True(restored.ExitCode == 0, restored.Output);
        var afterRestore = fixture.Read();
        Assert.Equal(before.EntityType, afterRestore.EntityType);
        Assert.Equal(before.PermanentAccountId, afterRestore.PermanentAccountId);
        Assert.Equal(before.CapitalName, afterRestore.CapitalName);
        Assert.Equal(before.DrawingsName, afterRestore.DrawingsName);
        Assert.Equal(before.LedgerLines, afterRestore.LedgerLines);
        var restoredSnapshot = fixture.Snapshot("restored.json");
        Assert.True(JsonNode.DeepEquals(Section(snapshotFile, "state"), Section(restoredSnapshot, "state")));
        Assert.True(JsonNode.DeepEquals(Section(snapshotFile, "fingerprint"), Section(restoredSnapshot, "fingerprint")));

        // Backup restore rehearsal: the pre-conversion dump restores into a fresh disposable database and matches the snapshot.
        var serverConnection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresRaceDatabase.EnvironmentVariable)!);
        var restoreName = "accountgo_restore_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(serverConnection.ConnectionString) { Database = "postgres", Pooling = false };
        void Admin(string sql)
        {
            using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        Admin($"CREATE DATABASE \"{restoreName}\"");
        try
        {
            var restoreUri = ToLibpqUri(new NpgsqlConnectionStringBuilder(serverConnection.ConnectionString) { Database = restoreName }.ConnectionString);
            var restore = new ProcessStartInfo("pg_restore") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in new[] { "--no-owner", "--no-privileges", "--dbname", restoreUri, backupFile })
                restore.ArgumentList.Add(argument);
            using (var process = Process.Start(restore)!)
            {
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error);
            }
            var restoredCopy = RunScript(restoreUri, "snapshot", "--slug", fixture.Slug, "--out-dir", fixture.OutputDirectory, "--snapshot", Path.Combine(fixture.OutputDirectory, "backup-copy.json"));
            Assert.True(restoredCopy.ExitCode == 0, restoredCopy.Output);
            var copyFile = Path.Combine(fixture.OutputDirectory, "backup-copy.json");
            Assert.True(JsonNode.DeepEquals(Section(snapshotFile, "fingerprint"), Section(copyFile, "fingerprint")));
            Assert.Equal(Section(snapshotFile, "state")["entityType"]!.ToString(), Section(copyFile, "state")["entityType"]!.ToString());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            Admin($"DROP DATABASE IF EXISTS \"{restoreName}\" WITH (FORCE)");
        }
    }

    [PostgresClientToolsFact]
    public void Conversion_is_blocked_when_a_closed_year_was_closed_into_another_permanent_account()
    {
        using var fixture = new Fixture(EntityType.Corporation);
        var tenant = new TenantContext();
        using (var context = fixture.Database.CreateContext(tenant))
        using (tenant.Impersonate(fixture.CompanyId, fixture.Slug, CompanyMemberRole.Owner))
        {
            var year = context.Db.FinancialYears.First();
            year.ClosedAt = DateTime.UtcNow;
            year.ClosedBy = "rehearsal";
            context.Db.SaveChanges();
        }
        var before = fixture.Read();
        var snapshotFile = fixture.Snapshot("blocked.json");
        var backupFile = Path.Combine(fixture.OutputDirectory, "placeholder.dump");
        File.WriteAllText(backupFile, "rehearsal placeholder");

        var dryRun = RunScript(fixture.DatabaseUri, "dry-run", "--slug", fixture.Slug);
        Assert.Equal(3, dryRun.ExitCode);
        Assert.Contains("DRYRUN_RESULT blocked", dryRun.Output);
        Assert.Contains("closed into a different permanent account", dryRun.Output);

        var apply = RunScript(fixture.DatabaseUri, "apply", "--slug", fixture.Slug, "--snapshot", snapshotFile,
            "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings");
        Assert.NotEqual(0, apply.ExitCode);
        Assert.Contains("blocked", apply.Output);
        Assert.Equal(before, fixture.Read());
    }

    [PostgresClientToolsFact]
    public void Apply_aborts_without_writing_when_the_ledger_changed_after_the_snapshot()
    {
        using var fixture = new Fixture(EntityType.SoleProprietor);
        var before = fixture.Read();
        var snapshotFile = fixture.Snapshot("drift.json");
        var backupFile = Path.Combine(fixture.OutputDirectory, "placeholder.dump");
        File.WriteAllText(backupFile, "rehearsal placeholder");

        var tenant = new TenantContext();
        using (var context = fixture.Database.CreateContext(tenant))
        using (tenant.Impersonate(fixture.CompanyId, fixture.Slug, CompanyMemberRole.Owner))
        {
            var accounts = context.Db.Accounts.ToDictionary(a => a.AccountCode);
            Fixture.PostForDrift(context.Db, fixture.CompanyId, accounts);
            context.Db.SaveChanges();
        }

        var apply = RunScript(fixture.DatabaseUri, "apply", "--slug", fixture.Slug, "--snapshot", snapshotFile,
            "--confirm-write", "--approval-ref", "FIN-1", "--backup-file", backupFile, "--accept-warnings");
        Assert.NotEqual(0, apply.ExitCode);
        Assert.Contains("ledger changed since the snapshot", apply.Output);
        var after = fixture.Read();
        Assert.Equal(before.EntityType, after.EntityType);
        Assert.Equal(before.CapitalName, after.CapitalName);
        Assert.Equal(before.PolicyRows, after.PolicyRows);
    }
}
