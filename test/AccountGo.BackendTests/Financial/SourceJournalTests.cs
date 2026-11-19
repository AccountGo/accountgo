// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Api.Data.Schema;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public sealed class SourceJournalTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _user = null!;
    private int _companyId;
    private int _otherCompanyId;
    private int _cashAccountId;
    private int _revenueAccountId;
    private int _otherCompanyAccountId;
    private const string Org = "source-journal-svc";
    private const string OtherOrg = "source-journal-other";
    private static readonly DateTime BooksStart = new(2025, 1, 1);

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _user = await _host.CreateUser("sj-svc@example.test");
        _companyId = await _host.CreateWorkspace(_user.Token, "Source Journal Svc", Org);
        _otherCompanyId = await _host.CreateWorkspace(_user.Token, "Other Workspace", OtherOrg);
        await _host.SetFunctionalRoles(_companyId, _user.Id, "Accountant");

        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        using (tenant.Impersonate(_companyId, Org, CompanyMemberRole.Owner))
        {
            var setting = db.CompanySettings.Single();
            setting.BooksStartDate = BooksStart;
            _cashAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("1")).Id;
            _revenueAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("4")).Id;
            db.SaveChanges();
        }

        using (tenant.Impersonate(_otherCompanyId, OtherOrg, CompanyMemberRole.Owner))
        {
            _otherCompanyAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost()).Id;
        }
    }

    public Task DisposeAsync() => _host.DisposeAsync();

    private SourceJournalDraftRequest MakeRequest(
        string sourceSystem = "example-feed",
        string sourceKey = "item:1:v1",
        decimal amount = 100m,
        string? memo = "Imported entry",
        string preparerId = "system:example-importer") =>
        new(
            CompanyId: _companyId,
            SourceSystem: sourceSystem,
            SourceKey: sourceKey,
            PreparerId: preparerId,
            Date: new DateTime(2026, 11, 10),
            Memo: memo,
            ReferenceNo: "EXT-1",
            Lines: new[]
            {
                new SourceJournalDraftLine(_cashAccountId, DrOrCrSide.Dr, amount, "Debit"),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, amount, "Credit")
            });

    [Fact]
    public void CreateDraft_NewKey_CreatesOneDraftWithSystemPreparerAuditRow()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var result = svc.CreateDraft(MakeRequest(sourceKey: "test1:v1"));

        Assert.True(result.Created);
        Assert.Equal(SourceJournalDraftState.Created, result.State);
        Assert.True(result.JournalId > 0);

        var header = db.JournalEntryHeaders.Include(j => j.JournalEntryLines).Single(j => j.Id == result.JournalId);
        Assert.False(header.Posted == true);
        Assert.Null(header.GeneralLedgerHeaderId);
        Assert.Equal("example-feed", header.SourceSystem);
        Assert.Equal("test1:v1", header.SourceKey);
        Assert.Equal(2, header.JournalEntryLines.Count);

        var preparerIds = JournalEvidence.PreparerIds(db.AuditLogs.AsNoTracking(), _companyId, result.JournalId);
        Assert.Contains("system:example-importer", preparerIds);
    }

    [Fact]
    public void CreateDraft_SameKeySameContent_ReturnsExistingWithoutSecondRow()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var first = svc.CreateDraft(MakeRequest(sourceKey: "test2:v1"));
        var second = svc.CreateDraft(MakeRequest(sourceKey: "test2:v1"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(SourceJournalDraftState.Existing, second.State);
        Assert.Equal(first.JournalId, second.JournalId);
        Assert.Equal(1, db.JournalEntryHeaders.Count(j => j.SourceSystem == "example-feed" && j.SourceKey == "test2:v1"));
    }

    [Fact]
    public void CreateDraft_SameKeyDifferentContent_ReturnsConflictAndLeavesOriginalUntouched()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var first = svc.CreateDraft(MakeRequest(sourceKey: "test3:v1", amount: 100m, memo: "Original"));
        var conflict = svc.CreateDraft(MakeRequest(sourceKey: "test3:v1", amount: 250m, memo: "Changed"));

        Assert.False(conflict.Created);
        Assert.Equal(SourceJournalDraftState.Conflict, conflict.State);
        Assert.Equal("source_key_conflict", conflict.Code);
        Assert.Equal(first.JournalId, conflict.JournalId);

        var stored = db.JournalEntryHeaders.Include(j => j.JournalEntryLines).Single(j => j.Id == first.JournalId);
        Assert.Equal("Original", stored.Memo);
        Assert.All(stored.JournalEntryLines, l => Assert.Equal(100m, l.Amount));

        // Same content/fingerprint but different ReferenceNo must also conflict (R8)
        var refConflict = svc.CreateDraft(new SourceJournalDraftRequest(
            _companyId,
            "example-feed",
            "test3:v1",
            "system:example-importer",
            new DateTime(2026, 11, 10),
            "Original",
            "REF-CHANGED",
            new[]
            {
                new SourceJournalDraftLine(_cashAccountId, DrOrCrSide.Dr, 100m, "Dr"),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, 100m, "Cr")
            }));
        Assert.Equal(SourceJournalDraftState.Conflict, refConflict.State);
        Assert.Equal("source_key_conflict", refConflict.Code);

        // Null vs empty ReferenceNo is treated as equal
        var nullRefDraft = svc.CreateDraft(new SourceJournalDraftRequest(
            _companyId,
            "example-feed",
            "test3:nullref",
            "system:example-importer",
            new DateTime(2026, 11, 10),
            "Original",
            null,
            new[]
            {
                new SourceJournalDraftLine(_cashAccountId, DrOrCrSide.Dr, 100m, "Dr"),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, 100m, "Cr")
            }));
        var emptyRefRetry = svc.CreateDraft(new SourceJournalDraftRequest(
            _companyId,
            "example-feed",
            "test3:nullref",
            "system:example-importer",
            new DateTime(2026, 11, 10),
            "Original",
            string.Empty,
            new[]
            {
                new SourceJournalDraftLine(_cashAccountId, DrOrCrSide.Dr, 100m, "Dr"),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, 100m, "Cr")
            }));
        Assert.Equal(SourceJournalDraftState.Existing, emptyRefRetry.State);
        Assert.Equal(nullRefDraft.JournalId, emptyRefRetry.JournalId);
    }

    [Fact]
    public async Task CreateDraft_ConcurrentDuplicateCreation_EndsWithExactlyOneJournal()
    {
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var scope = _host.Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var __ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
            var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
            return svc.CreateDraft(MakeRequest(sourceKey: "test4:concurrent:v1"));
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(2, results.Length);
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(1, results.Count(r => !r.Created && r.State == SourceJournalDraftState.Existing));
        Assert.Equal(results[0].JournalId, results[1].JournalId);

        using var verifyScope = _host.Factory.Services.CreateScope();
        var verifyTenant = verifyScope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var ___ = verifyTenant.Impersonate(_companyId, Org, CompanyMemberRole.Owner);
        var db = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(1, db.JournalEntryHeaders.Count(j => j.SourceSystem == "example-feed" && j.SourceKey == "test4:concurrent:v1"));
    }

    [Fact]
    public void CreateDraft_InvalidInputs_AreRefused()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();

        // Unbalanced lines
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(new SourceJournalDraftRequest(
            _companyId, "example-feed", "bad:unbalanced", "system:imp", new DateTime(2026, 11, 10), "m", "r",
            new[]
            {
                new SourceJournalDraftLine(_cashAccountId, DrOrCrSide.Dr, 100m),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, 90m)
            })));

        // Zero amount
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(MakeRequest(sourceKey: "bad:zero", amount: 0m)));

        // Another company's account
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(new SourceJournalDraftRequest(
            _companyId, "example-feed", "bad:other-acct", "system:imp", new DateTime(2026, 11, 10), "m", "r",
            new[]
            {
                new SourceJournalDraftLine(_otherCompanyAccountId, DrOrCrSide.Dr, 50m),
                new SourceJournalDraftLine(_revenueAccountId, DrOrCrSide.Cr, 50m)
            })));

        // Uppercase source system
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(MakeRequest(sourceSystem: "ExampleFeed", sourceKey: "bad:upper")));

        // Too-long source system (> 32 chars)
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(MakeRequest(sourceSystem: new string('a', 33), sourceKey: "bad:long")));

        // Whitespace in source key
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(MakeRequest(sourceKey: "bad key with space")));

        // Preparer without system:
        Assert.Throws<ArgumentException>(() => svc.CreateDraft(MakeRequest(sourceKey: "bad:preparer", preparerId: "user-123")));
    }

    [Fact]
    public void UpdateJournalEntry_UnpostedImportedDraft_ThrowsSourceJournalLocked()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var financial = scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();

        var created = svc.CreateDraft(MakeRequest(sourceKey: "test6:lock:v1"));
        var je = financial.GetJournalEntry(created.JournalId);
        Assert.NotNull(je);
        je.Memo = "Attempted edit";

        var ex = Assert.Throws<SourceJournalLockedException>(() => financial.UpdateJournalEntry(je, posted: false));
        Assert.Equal("source_journal_locked", ex.Code);
    }

    [Fact]
    public void Discard_DraftDeletesAndFreesKey_ManualJournalRefused()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var financial = scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var created = svc.CreateDraft(MakeRequest(sourceKey: "test10:discard:v1"));
        var lineIds = db.JournalEntryLines
            .AsNoTracking()
            .Where(l => l.JournalEntryHeaderId == created.JournalId)
            .Select(l => l.Id)
            .ToList();
        Assert.Equal(2, lineIds.Count);

        svc.Discard(_companyId, created.JournalId, "user-actor");

        Assert.Null(db.JournalEntryHeaders.FirstOrDefault(j => j.Id == created.JournalId));
        var createdIdStr = created.JournalId.ToString(CultureInfo.InvariantCulture);
        var audit = db.AuditLogs.Single(a => a.TableName == "__SourceJournalDiscarded" && a.RecordId == createdIdStr);
        Assert.Equal("example-feed:test10:discard:v1", audit.OriginalValue);
        Assert.Equal("user-actor", audit.UserName);

        var deletedHeaderChange = db.RecordChanges.AsNoTracking().Single(r =>
            r.EntityType == nameof(JournalEntryHeader)
            && r.EntityId == created.JournalId
            && r.Action == "Deleted");
        using (var doc = System.Text.Json.JsonDocument.Parse(deletedHeaderChange.ChangesJson))
        {
            Assert.Equal("test10:discard:v1", doc.RootElement.GetProperty(nameof(JournalEntryHeader.SourceKey)).GetString());
        }

        foreach (var lineId in lineIds)
        {
            Assert.True(db.RecordChanges.AsNoTracking().Any(r =>
                r.EntityType == nameof(JournalEntryLine)
                && r.EntityId == lineId
                && r.Action == "Deleted"));
        }

        // Key is freed; creating again succeeds
        var recreated = svc.CreateDraft(MakeRequest(sourceKey: "test10:discard:v1"));
        Assert.True(recreated.Created);
        Assert.NotEqual(created.JournalId, recreated.JournalId);

        // Manual journal cannot be discarded
        var manual = new JournalEntryHeader
        {
            CompanyId = _companyId,
            Date = new DateTime(2026, 11, 10),
            Memo = "Manual",
            VoucherType = JournalVoucherTypes.Other,
            JournalEntryLines = new List<JournalEntryLine>
            {
                new() { CompanyId = _companyId, AccountId = _cashAccountId, DrCr = DrOrCrSide.Dr, Amount = 10m },
                new() { CompanyId = _companyId, AccountId = _revenueAccountId, DrCr = DrOrCrSide.Cr, Amount = 10m }
            }
        };
        financial.AddJournalEntry(manual);

        var ex = Assert.Throws<SourceJournalOperationException>(() => svc.Discard(_companyId, manual.Id, "user-actor"));
        Assert.Equal("not_a_source_journal", ex.Code);
    }

    [Fact]
    public void AccessJournalFingerprint_NullSource_IsByteIdenticalToLegacyFormat()
    {
        var date = new DateTime(2026, 11, 10);
        var lines = new (int AccountId, DrOrCrSide DrCr, decimal Amount, string? Memo)[]
        {
            (10, DrOrCrSide.Dr, 123.45m, "Debit memo"),
            (20, DrOrCrSide.Cr, 123.45m, "Credit memo")
        };

        var withoutSourceArgs = AccessJournalFingerprint.Compute(date, "Test memo", (int)JournalVoucherTypes.Other, lines);
        var withNullSourceArgs = AccessJournalFingerprint.Compute(date, "Test memo", (int)JournalVoucherTypes.Other, lines, null, null);
        var withSourceArgs = AccessJournalFingerprint.Compute(date, "Test memo", (int)JournalVoucherTypes.Other, lines, "example-feed", "k1");

        // Compute expected legacy SHA-256 directly to prove byte-identity
        var legacyRaw = $"{date:yyyy-MM-dd}:Test memo:6:10:Dr:123.4500:Debit memo|20:Cr:123.4500:Credit memo";
        var legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyRaw)));

        Assert.Equal(legacyHash, withoutSourceArgs);
        Assert.Equal(legacyHash, withNullSourceArgs);
        Assert.NotEqual(legacyHash, withSourceArgs);
    }

    [Fact]
    public void SqliteSchemaUpgrader_AddsSourceColumnsAndFilteredUniqueIndex_ToLegacyDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        using (var db = new ApiDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE \"__SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL);");
            db.Database.ExecuteSqlRaw("INSERT INTO \"__SchemaVersion\" (\"Version\", \"AppliedAt\") VALUES (1, 'legacy');");
            db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_JournalEntryHeader_CompanyId_SourceSystem_SourceKey\";");
            db.Database.ExecuteSqlRaw("ALTER TABLE \"JournalEntryHeader\" DROP COLUMN \"SourceSystem\";");
            db.Database.ExecuteSqlRaw("ALTER TABLE \"JournalEntryHeader\" DROP COLUMN \"SourceKey\";");
        }

        SqliteSchemaUpgrader.Upgrade(connection);
        SqliteSchemaUpgrader.Upgrade(connection); // idempotent

        using var cols = connection.CreateCommand();
        cols.CommandText = @"PRAGMA table_info(""JournalEntryHeader"");";
        var colNames = new List<string>();
        using (var reader = cols.ExecuteReader())
        {
            while (reader.Read())
                colNames.Add(reader.GetString(1));
        }

        Assert.Contains("SourceSystem", colNames);
        Assert.Contains("SourceKey", colNames);

        using var idxs = connection.CreateCommand();
        idxs.CommandText = @"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'JournalEntryHeader';";
        var idxNames = new List<string>();
        using (var reader = idxs.ExecuteReader())
        {
            while (reader.Read())
                idxNames.Add(reader.GetString(0));
        }

        Assert.Contains("IX_JournalEntryHeader_CompanyId_SourceSystem_SourceKey", idxNames);
    }

    [Fact]
    public void Discard_WhenPostedConcurrentlyBetweenReadAndDelete_ReturnsConflictAndPreservesLedger()
    {
        int draftId;
        using (var setupScope = _host.Factory.Services.CreateScope())
        {
            var tenant = setupScope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
            draftId = setupScope.ServiceProvider.GetRequiredService<SourceJournalService>()
                .CreateDraft(MakeRequest(sourceKey: "test:discard-race:v1"))
                .JournalId;
        }

        try
        {
            SourceJournalTestGate.BeforeDiscardDelete = (cid, jid) =>
            {
                if (cid != _companyId || jid != draftId) return;
                using var raceScope = _host.Factory.Services.CreateScope();
                var raceTenant = raceScope.ServiceProvider.GetRequiredService<ITenantContext>();
                using var __ = raceTenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
                var fin = raceScope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();
                var je = fin.GetJournalEntry(jid, false);
                fin.UpdateJournalEntry(je!, posted: true);
            };

            using var discardScope = _host.Factory.Services.CreateScope();
            var discardTenant = discardScope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var ___ = discardTenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
            var svc = discardScope.ServiceProvider.GetRequiredService<SourceJournalService>();

            var ex = Assert.Throws<SourceJournalOperationException>(() => svc.Discard(_companyId, draftId, _user.Id));
            Assert.Equal("source_journal_posted", ex.Code);

            var db = discardScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var header = db.JournalEntryHeaders.AsNoTracking().SingleOrDefault(j => j.Id == draftId);
            Assert.NotNull(header);
            Assert.True(header!.Posted);
            Assert.NotNull(header.GeneralLedgerHeaderId);
            Assert.True(db.GeneralLedgerHeaders.AsNoTracking().Any(g => g.Id == header.GeneralLedgerHeaderId));
            Assert.False(db.AuditLogs.AsNoTracking().Any(a => a.TableName == "__SourceJournalDiscarded" && a.RecordId == draftId.ToString()));
        }
        finally
        {
            SourceJournalTestGate.Reset();
        }
    }

    [Fact]
    public void CreateDraft_UniqueViolationRace_HandlesExistingAndConflictAndKeepsContextUsable()
    {
        try
        {
            var raceExistingId = 0;
            var firedKeys = new HashSet<string>(StringComparer.Ordinal);
            SourceJournalTestGate.BeforeCreateInsert = (cid, sys, key) =>
            {
                if (cid != _companyId || (key != "test:race-same:v1" && key != "test:race-diff:v1")) return;
                if (!firedKeys.Add(key)) return;
                using var raceScope = _host.Factory.Services.CreateScope();
                var raceTenant = raceScope.ServiceProvider.GetRequiredService<ITenantContext>();
                using var __ = raceTenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
                raceExistingId = raceScope.ServiceProvider.GetRequiredService<SourceJournalService>()
                    .CreateDraft(MakeRequest(sourceKey: key, amount: 100m, memo: "Winner"))
                    .JournalId;
            };

            using var scope = _host.Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
            var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();

            // Same content after unique constraint violation -> Existing with winner's id
            var sameResult = svc.CreateDraft(MakeRequest(sourceKey: "test:race-same:v1", amount: 100m, memo: "Winner"));
            Assert.False(sameResult.Created);
            Assert.Equal(SourceJournalDraftState.Existing, sameResult.State);
            Assert.Equal(raceExistingId, sameResult.JournalId);

            // Different content after unique constraint violation -> Conflict
            var diffResult = svc.CreateDraft(MakeRequest(sourceKey: "test:race-diff:v1", amount: 200m, memo: "Loser"));
            Assert.False(diffResult.Created);
            Assert.Equal(SourceJournalDraftState.Conflict, diffResult.State);
            Assert.Equal("source_key_conflict", diffResult.Code);
            Assert.Equal(raceExistingId, diffResult.JournalId);

            // Service's DbContext must still be usable for a subsequent CreateDraft
            SourceJournalTestGate.Reset();
            var followup = svc.CreateDraft(MakeRequest(sourceKey: "test:race-followup:v1"));
            Assert.True(followup.Created);

            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.Equal(1, db.JournalEntryHeaders.AsNoTracking().Count(j => j.SourceSystem == "example-feed" && j.SourceKey == "test:race-same:v1"));
            Assert.Equal(1, db.JournalEntryHeaders.AsNoTracking().Count(j => j.SourceSystem == "example-feed" && j.SourceKey == "test:race-diff:v1"));
        }
        finally
        {
            SourceJournalTestGate.Reset();
        }
    }

    [Fact]
    public void CreateDraft_RecordsSourceKeyInRecordChangeAudit()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(_companyId, _user.Id, Org, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var result = svc.CreateDraft(MakeRequest(sourceKey: "test:audit-source-key:v1"));
        var recordChange = db.RecordChanges
            .AsNoTracking()
            .Single(r => r.EntityType == "JournalEntryHeader" && r.EntityId == result.JournalId && r.Action == "Added");

        using var doc = System.Text.Json.JsonDocument.Parse(recordChange.ChangesJson);
        Assert.Equal("example-feed", doc.RootElement.GetProperty("SourceSystem").GetString());
        Assert.Equal("test:audit-source-key:v1", doc.RootElement.GetProperty("SourceKey").GetString());
    }
}
