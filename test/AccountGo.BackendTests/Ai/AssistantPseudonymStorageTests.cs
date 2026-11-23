// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Schema;
using Api.Service.Ai;
using Api.Tenancy;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Ai;

public sealed class AssistantPseudonymStorageTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_pseudonym_store_" + Guid.NewGuid().ToString("N") + ".db");

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext(companyId: 1);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
        return Task.CompletedTask;
    }

    private ApiDbContext CreateDbContext(int companyId)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(companyId, $"org-{companyId}", CompanyMemberRole.Owner, Array.Empty<string>(), "user-1");
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        return new ApiDbContext(options, tenant);
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateTokenWithinSameScope_AllowsAcrossSessionsOrUsers()
    {
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateDbContext(companyId: 10))
        {
            db.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 10,
                UserId = "user-a",
                SessionId = "sess-1",
                Token = "C-A1B2C3D4",
                Kind = "customer",
                PublicId = "cus_1001",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await db.SaveChangesAsync();

            // Same token in different session is allowed
            db.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 10,
                UserId = "user-a",
                SessionId = "sess-2",
                Token = "C-A1B2C3D4",
                Kind = "customer",
                PublicId = "cus_1002",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(companyId: 10))
        {
            db.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 10,
                UserId = "user-a",
                SessionId = "sess-1",
                Token = "C-A1B2C3D4",
                Kind = "customer",
                PublicId = "cus_9999",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task TenantQueryFilter_IsolatesPseudonymsByCompany()
    {
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        await using (var db1 = CreateDbContext(companyId: 101))
        {
            db1.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 101,
                UserId = "user-1",
                SessionId = "sess-1",
                Token = "V-11223344",
                Kind = "vendor",
                PublicId = "ven_101",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await db1.SaveChangesAsync();
        }

        await using (var db2 = CreateDbContext(companyId: 202))
        {
            db2.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 202,
                UserId = "user-1",
                SessionId = "sess-1",
                Token = "V-55667788",
                Kind = "vendor",
                PublicId = "ven_202",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await db2.SaveChangesAsync();

            var visibleTo202 = await db2.AssistantPseudonyms.ToListAsync();
            Assert.Single(visibleTo202);
            Assert.Equal("V-55667788", visibleTo202[0].Token);
        }
    }

    [Fact]
    public async Task RetentionService_PurgesOnlyExpiredRowsAcrossCompanies()
    {
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        await using (var db1 = CreateDbContext(companyId: 301))
        {
            db1.AssistantPseudonyms.AddRange(
                new AssistantPseudonym
                {
                    CompanyId = 301,
                    UserId = "u1",
                    SessionId = "s-expired",
                    Token = "C-00000001",
                    Kind = "customer",
                    PublicId = "cus_old1",
                    CreatedAt = now.AddDays(-35),
                    LastUsedAt = now.AddDays(-31),
                    ExpiresAt = now.AddDays(-1),
                },
                new AssistantPseudonym
                {
                    CompanyId = 301,
                    UserId = "u1",
                    SessionId = "s-active",
                    Token = "C-00000002",
                    Kind = "customer",
                    PublicId = "cus_live",
                    CreatedAt = now.AddDays(-2),
                    LastUsedAt = now.AddDays(-1),
                    ExpiresAt = now.AddDays(29),
                });
            await db1.SaveChangesAsync();
        }

        await using (var db2 = CreateDbContext(companyId: 302))
        {
            db2.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 302,
                UserId = "u2",
                SessionId = "s-expired-2",
                Token = "B-00000003",
                Kind = "bank_account",
                PublicId = "bnk_old2",
                CreatedAt = now.AddDays(-40),
                LastUsedAt = now.AddDays(-30),
                ExpiresAt = now,
            });
            await db2.SaveChangesAsync();
        }

        await using (var purgeDb = CreateDbContext(companyId: 999))
        {
            var retention = new AssistantPseudonymRetentionService(purgeDb);
            var deleted = await retention.PurgeExpiredAsync(now, CancellationToken.None);
            Assert.Equal(2, deleted);

            var remaining = await purgeDb.AssistantPseudonyms.IgnoreQueryFilters().ToListAsync();
            Assert.Single(remaining);
            Assert.Equal("C-00000002", remaining[0].Token);
        }
    }

    [Fact]
    public async Task SqliteSchemaUpgrader_CreatesAssistantPseudonymsTableAndIndexes()
    {
        var rawPath = Path.Combine(Path.GetTempPath(), "accountgo_pseudonym_upgrade_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite("Data Source=" + rawPath)
                .Options;
            await using (var setupDb = new ApiDbContext(options))
            {
                await setupDb.Database.EnsureCreatedAsync();
                await setupDb.Database.ExecuteSqlRawAsync(@"DROP TABLE IF EXISTS ""AssistantPseudonyms"";");
            }

            await using var conn = new SqliteConnection("Data Source=" + rawPath);
            await conn.OpenAsync();

            SqliteSchemaUpgrader.Upgrade(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'index') AND name LIKE '%AssistantPseudonyms%' ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync();
            var names = new System.Collections.Generic.List<string>();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            Assert.Contains("AssistantPseudonyms", names);
            Assert.Contains("IX_AssistantPseudonyms_CompanyId_UserId_SessionId_Token", names);
            Assert.Contains("IX_AssistantPseudonyms_ExpiresAt", names);
        }
        finally
        {
            foreach (var file in new[] { rawPath, rawPath + "-wal", rawPath + "-shm" })
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
