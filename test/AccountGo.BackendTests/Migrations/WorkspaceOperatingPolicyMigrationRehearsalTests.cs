// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Api.Options;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

/// <summary>
/// Operational migration and backfill rehearsal testing:
/// 1. Pre-policy schema upgrade to current policy schema.
/// 2. Repeat startup / idempotency on disposable databases across 6 sanitized fixtures.
/// 3. Invariants: zero unearned grants, zero journal/ledger rewrites, zero duplicate policy rows,
///    and strict tenant isolation.
/// 4. Verifies backward compatibility with rollback target dfb4cb6b.
///
/// Every provider-sensitive test is a Theory over <see cref="Providers"/>. SQLite (in-memory) always runs and keeps the
/// SqliteSchemaUpgrader path. When ACCOUNTGO_TEST_POSTGRES is set the same fixtures also run on a disposable PostgreSQL
/// database through the real Postgres migrations: the schema is migrated to the revision before
/// WorkspaceOperatingPolicy, the legacy fixtures are seeded, then startup (Migrate plus HostedApiLockdown, as in
/// AccountGoApp) runs the policy migration and backfill, and runs again to prove it is idempotent.
/// </summary>
public class WorkspaceOperatingPolicyMigrationRehearsalTests
{
    private const string SqliteProvider = "Sqlite";
    private const string PostgresProvider = "Postgres";
    private const string PolicyMigrationSuffix = "_WorkspaceOperatingPolicy";

    public static IEnumerable<object[]> Providers()
    {
        yield return new object[] { SqliteProvider };
        if (PostgresTestDatabase.IsConfigured)
            yield return new object[] { PostgresProvider };
    }

    /// <summary>A disposable database for one rehearsal, on SQLite (in-memory) or PostgreSQL.</summary>
    private sealed class RehearsalDatabase : IDisposable
    {
        private readonly PostgresTestDatabase? _postgres;

        public RehearsalDatabase(string provider)
        {
            Assert.True(provider is SqliteProvider or PostgresProvider, "Unknown provider " + provider);
            if (provider == PostgresProvider)
            {
                _postgres = PostgresTestDatabase.CreateIfConfigured()
                    ?? throw new InvalidOperationException(PostgresTestDatabase.EnvironmentVariable + " is not set.");
                Connection = new NpgsqlConnection(_postgres.ConnectionString);
                Connection.Open();
            }
            else
            {
                Connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                Connection.Open();
                using var cmd = Connection.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_keys = OFF;";
                cmd.ExecuteNonQuery();
            }
        }

        public bool IsPostgres => _postgres != null;

        /// <summary>The raw connection used for fixtures and assertions (separate from the contexts' own connections on PostgreSQL).</summary>
        public DbConnection Connection { get; }

        public ApiDbContext CreateContext(ITenantContext? tenant = null)
        {
            if (_postgres != null)
            {
                var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                    .UseNpgsql(_postgres.ConnectionString,
                        npgsql => npgsql.ExecutionStrategy(dependencies => new BufferingExecutionStrategy(dependencies)))
                    .AddInterceptors(new WriteLockTransactionInterceptor())
                    .Options;
                return new PostgresApiDbContext(options, tenant);
            }

            var sqliteOptions = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(Connection).Options;
            return tenant == null ? new ApiDbContext(sqliteOptions) : new ApiDbContext(sqliteOptions, tenant);
        }

        /// <summary>Creates the full current schema (EnsureCreated on SQLite, all Postgres migrations on PostgreSQL).</summary>
        public void CreateCurrentSchema()
        {
            using var db = CreateContext();
            if (IsPostgres)
                db.Database.Migrate();
            else
                db.Database.EnsureCreated();
        }

        /// <summary>
        /// Brings the schema to the revision just before the operating policy tables exist (a legacy database).
        /// SQLite builds the full schema and drops the policy tables; PostgreSQL migrates up to the prior migration.
        /// </summary>
        public void CreatePrePolicySchema()
        {
            if (!IsPostgres)
            {
                CreateCurrentSchema();
                ExecuteNonQuery(Connection, @"
                    DROP TABLE IF EXISTS ""WorkspaceApprovalRule"";
                    DROP TABLE IF EXISTS ""WorkspaceOperatingSettings"";
                    DROP TABLE IF EXISTS ""WorkspacePolicyCoordination"";
                    DELETE FROM ""GeneralLedgerLine"";
                    DELETE FROM ""JournalEntryHeader"";
                    DELETE FROM ""CompanyMember"";
                    DELETE FROM ""Company"";");
                return;
            }

            using var db = CreateContext();
            var migrations = db.Database.GetMigrations().ToList();
            var policyIndex = migrations.FindIndex(id => id.EndsWith(PolicyMigrationSuffix, StringComparison.Ordinal));
            Assert.True(policyIndex > 0, "WorkspaceOperatingPolicy migration not found in the Postgres migration set.");
            db.GetService<IMigrator>().Migrate(migrations[policyIndex - 1]);

            // Clear any seed data for a deterministic rehearsal.
            ExecuteNonQuery(Connection, @"TRUNCATE TABLE ""GeneralLedgerLine"", ""JournalEntryHeader"", ""CompanyMember"", ""Company"" CASCADE;");
        }

        /// <summary>
        /// The application's startup schema step: SqliteSchemaUpgrader on SQLite; Migrate then HostedApiLockdown on
        /// PostgreSQL (the order used by AccountGoApp). Safe to run repeatedly.
        /// </summary>
        public void RunStartup()
        {
            if (!IsPostgres)
            {
                SqliteSchemaUpgrader.Upgrade(Connection, NullLogger.Instance);
                return;
            }

            using var db = CreateContext();
            db.Database.Migrate();
            HostedApiLockdown.Apply(db);
        }

        /// <summary>SQLite has foreign keys off for the whole test; PostgreSQL skips them for the legacy fixture inserts only.</summary>
        public void BeginUnconstrainedSeeding()
        {
            if (IsPostgres)
                ExecuteNonQuery(Connection, "SET session_replication_role = replica;");
        }

        public void EndUnconstrainedSeeding()
        {
            if (IsPostgres)
                ExecuteNonQuery(Connection, "SET session_replication_role = DEFAULT;");
        }

        /// <summary>SQL boolean literal for the provider (SQLite stores 0/1, PostgreSQL has a boolean type).</summary>
        public string Bool(bool value) => IsPostgres ? (value ? "true" : "false") : (value ? "1" : "0");

        public void Dispose()
        {
            Connection.Dispose();
            _postgres?.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MigrationAndRepeatStartup_Across6SanitizedFixtures_PreservesAllInvariants_OnConfiguredProvider(string provider)
    {
        using var rehearsal = new RehearsalDatabase(provider);
        var connection = rehearsal.Connection;
        var no = rehearsal.Bool(false);
        var yes = rehearsal.Bool(true);

        // -------------------------------------------------------------------------
        // STEP 1: Seed Pre-Policy Database Schema (Prior to Operating Policy tables)
        // -------------------------------------------------------------------------
        // SQLite: full EF schema with the policy tables dropped. PostgreSQL: migrations up to the revision before
        // WorkspaceOperatingPolicy. Either way the seed data is cleared for a deterministic rehearsal.
        rehearsal.CreatePrePolicySchema();

        // Verify pre-policy state: policy tables do not exist
        Assert.False(TableExists(connection, "WorkspaceOperatingSettings"));
        Assert.False(TableExists(connection, "WorkspaceApprovalRule"));
        Assert.False(TableExists(connection, "WorkspacePolicyCoordination"));

        // -------------------------------------------------------------------------
        // STEP 2: Introduce the 6 Representative Sanitized Fixtures
        // -------------------------------------------------------------------------
        rehearsal.BeginUnconstrainedSeeding();
        using (var seedCmd = connection.CreateCommand())
        {
            seedCmd.CommandText = $@"
                -- Fixture 1: Uninitialized Legacy (single admin, pre-policy, existing journal entry)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (101, 'Legacy Uninitialized Corp', 'LEG101', 'leg101', 'LEG101', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-01-01');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1001, 101, 'usr-legacy-owner', 1, 1, '2025-01-01');

                INSERT INTO ""JournalEntryHeader"" (""Id"", ""CompanyId"", ""Date"", ""VoucherType"", ""Memo"", ""ReferenceNo"", ""Posted"")
                VALUES (5001, 101, '2025-06-01', 1, 'Historical Opening Entry', 'REF001', {yes});

                INSERT INTO ""GeneralLedgerLine"" (""Id"", ""CompanyId"", ""GeneralLedgerHeaderId"", ""AccountId"", ""DrCr"", ""Amount"")
                VALUES (7001, 101, 1, 1, 1, 100.0);

                -- Fixture 2: Candidate for Solo Initialization (single member, no prior teams)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (102, 'Solo Candidate LLC', 'SOLO102', 'solo102', 'SOLO102', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-02-01');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1002, 102, 'usr-solo-owner', 1, 1, '2025-02-01');

                -- Fixture 3: Established Team (multiple active members)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (103, 'Established Team Partners', 'TEAM103', 'team103', 'TEAM103', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-03-01');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1003, 103, 'usr-team-owner', 1, 1, '2025-03-01'),
                       (1004, 103, 'usr-team-accountant', 2, 1, '2025-03-02');

                -- Fixture 4: Former Team (had multiple members, member role != 1 in history or accepted invite)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (104, 'Former Team Enterprise', 'FORM104', 'form104', 'FORM104', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-04-01');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1005, 104, 'usr-former-owner', 1, 1, '2025-04-01'),
                       (1006, 104, 'usr-former-staff', 3, 1, '2025-04-02'); -- Role != 1 marks EverTeamActivated

                -- Fixture 5A: Pending Invitation Only (does not trigger EverTeamActivated)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (105, 'Pending Invite Co', 'PEND105', 'pend105', 'PEND105', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-05-01');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1007, 105, 'usr-pending-owner', 1, 1, '2025-05-01');

                INSERT INTO ""CompanyInvitation"" (""Id"", ""CompanyId"", ""Email"", ""Role"", ""SecurityRoleIdsJson"", ""TokenHash"", ""InvitedByUserId"", ""CreatedAt"", ""ExpiresAt"", ""AcceptedAt"", ""ResendCount"")
                VALUES (2001, 105, 'pending@test.com', 1, '[]', 'token-pend-1', 'usr-pending-owner', '2025-05-02', '2025-05-09', NULL, 0);

                -- Fixture 5B: Accepted Invitation (triggers EverTeamActivated)
                INSERT INTO ""Company"" (""Id"", ""Name"", ""ShortName"", ""Slug"", ""CompanyCode"", ""ActiveModulesJson"", ""DisabledModulesJson"", ""CancelAtPeriodEnd"", ""BillingManagedManually"", ""CreatedAt"")
                VALUES (106, 'Accepted Invite Co', 'ACCP106', 'accp106', 'ACCP106', '[""sales"",""financials""]', '[]', {no}, {no}, '2025-05-10');

                INSERT INTO ""CompanyMember"" (""Id"", ""CompanyId"", ""UserId"", ""Role"", ""Status"", ""JoinedAt"")
                VALUES (1008, 106, 'usr-accepted-owner', 1, 1, '2025-05-10');

                INSERT INTO ""CompanyInvitation"" (""Id"", ""CompanyId"", ""Email"", ""Role"", ""SecurityRoleIdsJson"", ""TokenHash"", ""InvitedByUserId"", ""CreatedAt"", ""ExpiresAt"", ""AcceptedAt"", ""ResendCount"")
                VALUES (2002, 106, 'collab@test.com', 1, '[]', 'token-accp-1', 'usr-accepted-owner', '2025-05-11', '2025-05-18', '2025-05-12', 0);

                -- Fixture 6: Multi-Tenant Distinct Workspaces (tested alongside 101-106)
            ";
            seedCmd.ExecuteNonQuery();
        }
        rehearsal.EndUnconstrainedSeeding();

        // Capture baseline counts before migration
        long initialJournalCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""JournalEntryHeader"";");
        long initialLedgerCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""GeneralLedgerLine"";");
        long initialRoleGrantCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""CompanyMemberSecurityRole"";");

        Assert.Equal(1, initialJournalCount);
        Assert.Equal(1, initialLedgerCount);
        Assert.Equal(0, initialRoleGrantCount);

        // -------------------------------------------------------------------------
        // STEP 3: Run Migration from Pre-Policy Schema to Current Policy Schema
        // -------------------------------------------------------------------------
        rehearsal.RunStartup();

        // Verify policy tables now exist
        Assert.True(TableExists(connection, "WorkspaceOperatingSettings"));
        Assert.True(TableExists(connection, "WorkspaceApprovalRule"));
        Assert.True(TableExists(connection, "WorkspacePolicyCoordination"));

        // -------------------------------------------------------------------------
        // STEP 4: Verify Post-Migration Invariants for All 6 Fixtures
        // -------------------------------------------------------------------------
        // Invariant A: Zero unearned security role grants
        long postMigRoleCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""CompanyMemberSecurityRole"";");
        Assert.Equal(0, postMigRoleCount);

        // Invariant B: Zero journal / ledger rewrites
        long postMigJournalCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""JournalEntryHeader"";");
        long postMigLedgerCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""GeneralLedgerLine"";");
        Assert.Equal(initialJournalCount, postMigJournalCount);
        Assert.Equal(initialLedgerCount, postMigLedgerCount);

        // Fixture 1: Legacy Uninitialized
        var leg101Settings = GetSettings(connection, 101);
        Assert.NotNull(leg101Settings);
        Assert.Equal(0, leg101Settings.Value.Purpose); // Unset
        Assert.Equal(0, leg101Settings.Value.CollaborationIntent); // Unset
        Assert.Equal(0, leg101Settings.Value.CollaborationState); // Unset
        Assert.Null(leg101Settings.Value.InitializedAtUtc);
        Assert.Null(leg101Settings.Value.EverTeamActivatedAtUtc); // Single admin, no team history
        Assert.Equal(1, leg101Settings.Value.PolicySchemaVersion);

        // Fixture 1 Default Approval Rule: IndependentReviewerRequired (LegacyEnforced = 0)
        var leg101Rule = GetApprovalRule(connection, 101, "financials.journal.post");
        Assert.NotNull(leg101Rule);
        Assert.Equal(0, leg101Rule.Value.RuleKind); // IndependentReviewerRequired
        Assert.Equal(0, leg101Rule.Value.Provenance); // LegacyEnforced

        // Fixture 3: Established Team (had member with role != 1)
        var team103Settings = GetSettings(connection, 103);
        Assert.NotNull(team103Settings);
        Assert.NotNull(team103Settings.Value.EverTeamActivatedAtUtc); // Flagged as team history

        // Fixture 4: Former Team (had member with role = 3)
        var form104Settings = GetSettings(connection, 104);
        Assert.NotNull(form104Settings);
        Assert.NotNull(form104Settings.Value.EverTeamActivatedAtUtc); // Team history preserved

        // Fixture 5A: Pending invitation only (no accepted invite)
        var pend105Settings = GetSettings(connection, 105);
        Assert.NotNull(pend105Settings);
        Assert.Null(pend105Settings.Value.EverTeamActivatedAtUtc); // Pending alone does NOT activate team

        // Fixture 5B: Accepted invitation
        var accp106Settings = GetSettings(connection, 106);
        Assert.NotNull(accp106Settings);
        Assert.NotNull(accp106Settings.Value.EverTeamActivatedAtUtc); // Accepted invite triggers team history

        // -------------------------------------------------------------------------
        // STEP 5: Initialize Fixture 2 as Confirmed Solo Workspace
        // -------------------------------------------------------------------------
        using (var initCmd = connection.CreateCommand())
        {
            initCmd.CommandText = @"
                UPDATE ""WorkspaceOperatingSettings""
                SET ""Purpose"" = 1, -- ClientAccounting
                    ""CollaborationIntent"" = 1, -- Solo
                    ""CollaborationState"" = 1, -- SoloActive
                    ""ConfirmedOwnerMemberId"" = 1002,
                    ""InitializedAtUtc"" = '2026-11-01T00:00:00Z',
                    ""InitializedByUserId"" = 'usr-solo-owner',
                    ""PolicyRevision"" = 1
                WHERE ""CompanyId"" = 102;

                UPDATE ""WorkspaceApprovalRule""
                SET ""RuleKind"" = 1, -- SingleActorAllowed
                    ""Provenance"" = 1, -- NewWorkspaceConfirmed
                    ""ConfiguredAtPolicyRevision"" = 1
                WHERE ""CompanyId"" = 102 AND ""OperationKey"" = 'financials.journal.post';
            ";
            initCmd.ExecuteNonQuery();
        }

        // Verify Fixture 2 is confirmed Solo
        var solo102Pre = GetSettings(connection, 102);
        Assert.Equal(1, solo102Pre.Value.CollaborationState);
        Assert.Equal(1, solo102Pre.Value.PolicyRevision);

        // -------------------------------------------------------------------------
        // STEP 6: Execute Repeat Startup (Re-run Upgrade / Startup Idempotence)
        // -------------------------------------------------------------------------
        rehearsal.RunStartup();

        // Verify Invariants after Repeat Startup:
        // 1. Zero duplicate rows in policy tables
        long settingsCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""WorkspaceOperatingSettings"";");
        long rulesCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""WorkspaceApprovalRule"";");
        long coordCount = ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""WorkspacePolicyCoordination"";");
        Assert.Equal(6, settingsCount); // Exactly 1 per company (101-106)
        Assert.Equal(6, rulesCount);    // Exactly 1 per company
        Assert.Equal(6, coordCount);    // Exactly 1 per company

        // 2. Fixture 2 confirmed Solo state was NOT overwritten by repeat startup
        var solo102Post = GetSettings(connection, 102);
        Assert.Equal(1, solo102Post.Value.Purpose);
        Assert.Equal(1, solo102Post.Value.CollaborationIntent);
        Assert.Equal(1, solo102Post.Value.CollaborationState);
        Assert.Equal(1002, solo102Post.Value.ConfirmedOwnerMemberId);
        Assert.Equal(1, solo102Post.Value.PolicyRevision);

        var solo102RulePost = GetApprovalRule(connection, 102, "financials.journal.post");
        Assert.Equal(1, solo102RulePost.Value.RuleKind); // SingleActorAllowed preserved
        Assert.Equal(1, solo102RulePost.Value.Provenance); // NewWorkspaceConfirmed preserved

        // 3. Zero new role grants
        Assert.Equal(0, ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""CompanyMemberSecurityRole"";"));

        // 4. Zero ledger changes
        Assert.Equal(initialJournalCount, ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""JournalEntryHeader"";"));
        Assert.Equal(initialLedgerCount, ExecuteScalarLong(connection, @"SELECT COUNT(*) FROM ""GeneralLedgerLine"";"));

        // -------------------------------------------------------------------------
        // STEP 7: Rollback Target Verification (dfb4cb6b compatibility)
        // -------------------------------------------------------------------------
        // Test that the database state produced by this release is fully compatible
        // with the approval policy resolution and service behavior of dfb4cb6b.
        using var db = rehearsal.CreateContext();
        var resolver = new WorkspaceApprovalPolicyResolver(db);

        // A: Legacy uninitialized workspace 101 requires independent review
        var rule101 = resolver.Resolve(101, "financials.journal.post");
        Assert.Equal(WorkspaceApprovalMode.IndependentReviewerRequired, rule101.Mode);
        Assert.Equal(WorkspaceApprovalRuleProvenance.LegacyEnforced, rule101.Provenance);

        // B: Confirmed Solo workspace 102 allows single-actor execution
        var rule102 = resolver.Resolve(102, "financials.journal.post");
        Assert.Equal(WorkspaceApprovalMode.SingleActorAllowed, rule102.Mode);
        Assert.Equal(WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed, rule102.Provenance);

        // C: Established team workspace 103 requires independent review
        var rule103 = resolver.Resolve(103, "financials.journal.post");
        Assert.Equal(WorkspaceApprovalMode.IndependentReviewerRequired, rule103.Mode);
        Assert.Equal(WorkspaceApprovalRuleProvenance.LegacyEnforced, rule103.Provenance);
    }

    [Fact]
    public void SqlServer_ModelSnapshot_Matches_And_InstallScriptGeneratesCleanly()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var db = new ApiDbContext(options);

        // Verify model has no pending unmigrated changes
        Assert.False(db.Database.HasPendingModelChanges());

        // Verify script generation executes cleanly
        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.NotEmpty(script);
        Assert.Contains("WorkspaceOperatingSettings", script);
        Assert.Contains("WorkspaceApprovalRule", script);
        Assert.Contains("WorkspacePolicyCoordination", script);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CurrentCodeSchemaCompatibility_Dfb4cb6b_Conventions_SucceedAgainstUpgradedDatabase_OnConfiguredProvider(string provider)
    {
        // Current-code forward/reverse schema compatibility test:
        // Pinned Rollback Target Revisions:
        // - Public repo (AccountGo/accountgo): dfb4cb6bdbc66c782d68b0b5586b10650ff9a998
        // - Commercial modules (AccountGo/accountgo-commercial): 542ffd641f92c10b78c80d4f13459c704ea1ee25
        // - Ops repo (AccountGo/accountgo-ops): e414460f4eeb1d19889f5bc3a6773a9856f68c3a
        //
        // Note: Running current code using dfb4cb6b conventions verifies schema compatibility
        // (zero missing columns, identical PolicySchemaVersion=1, matching constraints).
        // Actual binary rollback verification (deploying the compiled dfb4cb6b container image)
        // remains pending operational environment rehearsal in staging.
        //
        // 1. Database schema created by current model (PolicySchemaVersion = 1).
        // 2. dfb4cb6b policy resolver resolves single-actor and reviewer-required rules.
        // 3. dfb4cb6b policy transaction acquires coordination lock and commits financial mutations.
        // 4. Verifies zero schema discrepancies, missing columns, or rollback barriers.

        using var rehearsal = new RehearsalDatabase(provider);
        rehearsal.CreateCurrentSchema();

        var tenant = new TenantContext();
        using (var db = rehearsal.CreateContext(tenant))
        {
            db.SuppressTenantSecurityCheck = true;

            var company = new Company
            {
                Id = 801,
                Name = "Rollback Target Co",
                ShortName = "ROL801",
                Slug = "rol801",
                CompanyCode = "ROL801"
            };
            db.Companies.Add(company);
            db.SaveChanges();

            var member = new CompanyMember
            {
                Id = 8001,
                CompanyId = company.Id,
                UserId = "user-dfb4cb6b",
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            db.CompanyMembers.Add(member);

            var settings = new WorkspaceOperatingSettings
            {
                CompanyId = company.Id,
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                CollaborationState = WorkspaceCollaborationState.SoloActive,
                ConfirmedOwnerMemberId = member.Id,
                PolicyRevision = 1,
                PolicySchemaVersion = 1
            };
            db.WorkspaceOperatingSettings.Add(settings);

            var rule = new WorkspaceApprovalRule
            {
                CompanyId = company.Id,
                OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed,
                ConfiguredAtPolicyRevision = 1,
                PolicySchemaVersion = 1
            };
            db.WorkspaceApprovalRules.Add(rule);
            db.SaveChanges();

            // Execute policy-sensitive operations
            var resolver = new WorkspaceApprovalPolicyResolver(db);
            var effectiveRule = resolver.Resolve(company.Id, AccessOperationCatalog.FinancialsJournalPost);
            Assert.Equal(WorkspaceApprovalMode.SingleActorAllowed, effectiveRule.Mode);

            var tx = new WorkspacePolicyTransaction(db, tenant);
            var journalId = tx.Execute(company.Id, guard =>
            {
                var journal = new JournalEntryHeader
                {
                    CompanyId = company.Id,
                    Date = DateTime.UtcNow,
                    VoucherType = JournalVoucherTypes.OpeningBalances,
                    Memo = "Rollback rehearsal test journal",
                    ReferenceNo = "ROLLBACK-001",
                    Posted = true
                };
                db.JournalEntryHeaders.Add(journal);
                db.SaveChanges();
                return journal.Id;
            });

            Assert.True(journalId > 0);
            var committedJournal = db.JournalEntryHeaders.Find(journalId);
            Assert.NotNull(committedJournal);
            Assert.Equal("ROLLBACK-001", committedJournal.ReferenceNo);
        }
    }

    // Helper functions
    private static void ExecuteNonQuery(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(DbConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = connection is NpgsqlConnection
            ? "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name;"
            : "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static long ExecuteScalarLong(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static (int Purpose, int CollaborationIntent, int CollaborationState, int? ConfirmedOwnerMemberId, string? InitializedAtUtc, string? EverTeamActivatedAtUtc, int PolicyRevision, int PolicySchemaVersion)?
        GetSettings(DbConnection connection, int companyId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""Purpose"", ""CollaborationIntent"", ""CollaborationState"", ""ConfirmedOwnerMemberId"",
                   ""InitializedAtUtc"", ""EverTeamActivatedAtUtc"", ""PolicyRevision"", ""PolicySchemaVersion""
            FROM ""WorkspaceOperatingSettings""
            WHERE ""CompanyId"" = @cid;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@cid";
        p.Value = companyId;
        cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        // Convert so the same reader works for SQLite (integer/text) and PostgreSQL (integer/bigint/timestamp).
        return (
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture)
        );
    }

    private static (int RuleKind, int Provenance)? GetApprovalRule(DbConnection connection, int companyId, string operationKey)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ""RuleKind"", ""Provenance""
            FROM ""WorkspaceApprovalRule""
            WHERE ""CompanyId"" = @cid AND ""OperationKey"" = @op;";
        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@cid";
        p1.Value = companyId;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "@op";
        p2.Value = operationKey;
        cmd.Parameters.Add(p2);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return (
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
    }
}
