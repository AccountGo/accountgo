// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Api.Data;
using Api.Service.Ai;
using Api.Tenancy;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AccountGo.BackendTests.Ai;

public sealed class AssistantPseudonymServiceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_pseudonym_svc_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly IAssistantPseudonymKeyProvider _keyProvider;

    public AssistantPseudonymServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            })
            .Build();
        _keyProvider = new AssistantPseudonymKeyProvider(config);
    }

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
    public async Task SameSessionAndEntity_ProducesStableToken_AndRefreshesTtlOnReuse()
    {
        var t0 = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(t0);

        string token1;
        await using (var db = CreateDbContext(companyId: 10))
        {
            var svc = new AssistantPseudonymService(db, _keyProvider, clock);
            token1 = await svc.PseudonymizeAsync(10, "user-1", "sess-A", AssistantPseudonymKind.Customer, "cus_100");
        }

        Assert.Matches(@"^C-[0-9A-F]{8}$", token1);

        // Advance 5 days and call again in a fresh request scope -> same token, LastUsedAt/ExpiresAt refreshed
        clock.SetUtcNow(t0.AddDays(5));
        string token2;
        await using (var db = CreateDbContext(companyId: 10))
        {
            var svc = new AssistantPseudonymService(db, _keyProvider, clock);
            token2 = await svc.PseudonymizeAsync(10, "user-1", "sess-A", AssistantPseudonymKind.Customer, "cus_100");

            var row = await db.AssistantPseudonyms.SingleAsync(r => r.Token == token1 && r.SessionId == "sess-A");
            Assert.Equal(t0.UtcDateTime, row.CreatedAt);
            Assert.Equal(t0.AddDays(5).UtcDateTime, row.LastUsedAt);
            Assert.Equal(t0.AddDays(35).UtcDateTime, row.ExpiresAt);
        }

        Assert.Equal(token1, token2);
    }

    [Fact]
    public async Task DifferentSessionsUsersOrCompanies_ProduceUnlinkableTokens()
    {
        string tokA, tokDiffSession, tokDiffUser, tokDiffCompany;
        await using (var db = CreateDbContext(companyId: 20))
        {
            var svc = new AssistantPseudonymService(db, _keyProvider);
            tokA = await svc.PseudonymizeAsync(20, "user-1", "sess-1", AssistantPseudonymKind.Vendor, "ven_42");
            tokDiffSession = await svc.PseudonymizeAsync(20, "user-1", "sess-2", AssistantPseudonymKind.Vendor, "ven_42");
            tokDiffUser = await svc.PseudonymizeAsync(20, "user-2", "sess-1", AssistantPseudonymKind.Vendor, "ven_42");
        }

        await using (var db2 = CreateDbContext(companyId: 21))
        {
            var svc2 = new AssistantPseudonymService(db2, _keyProvider);
            tokDiffCompany = await svc2.PseudonymizeAsync(21, "user-1", "sess-1", AssistantPseudonymKind.Vendor, "ven_42");
        }

        Assert.StartsWith("V-", tokA);
        Assert.NotEqual(tokA, tokDiffSession);
        Assert.NotEqual(tokA, tokDiffUser);
        Assert.NotEqual(tokA, tokDiffCompany);
        Assert.NotEqual(tokDiffSession, tokDiffUser);
    }

    [Fact]
    public async Task AbsentSessionId_UsesPerRequestEphemeralKey_AndDoesNotPersist()
    {
        string req1a, req1b, req2;
        await using (var db = CreateDbContext(companyId: 30))
        {
            var svc1 = new AssistantPseudonymService(db, _keyProvider);
            req1a = await svc1.PseudonymizeAsync(30, "user-1", null, AssistantPseudonymKind.BankAccount, "bnk_7");
            req1b = await svc1.PseudonymizeAsync(30, "user-1", null, AssistantPseudonymKind.BankAccount, "bnk_7");
        }

        await using (var db = CreateDbContext(companyId: 30))
        {
            var svc2 = new AssistantPseudonymService(db, _keyProvider);
            req2 = await svc2.PseudonymizeAsync(30, "user-1", "", AssistantPseudonymKind.BankAccount, "bnk_7");

            var count = await db.AssistantPseudonyms.CountAsync();
            Assert.Equal(0, count);
        }

        Assert.Matches(@"^B-[0-9A-F]{8}$", req1a);
        Assert.Equal(req1a, req1b);
        Assert.NotEqual(req1a, req2);
    }

    [Fact]
    public async Task EightHexCollisionWithinSession_ExtendsToTwelveUppercaseHex()
    {
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var clock = new MutableTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));

        // Pre-seed the exact 8-hex token for cus_target under a DIFFERENT entity so a collision is forced.
        string expected8;
        string expected12;
        using (var hmac = new System.Security.Cryptography.HMACSHA256(_keyProvider.AppDerivedKey))
        {
            var sessionKey = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes("40|user-1|sess-col"));
            (expected8, expected12) = AssistantPseudonymService.ComputeCandidateTokens(sessionKey, AssistantPseudonymKind.Customer, "cus_target");
        }

        await using (var db = CreateDbContext(companyId: 40))
        {
            db.AssistantPseudonyms.Add(new AssistantPseudonym
            {
                CompanyId = 40,
                UserId = "user-1",
                SessionId = "sess-col",
                Token = expected8,
                Kind = AssistantPseudonymKind.Customer,
                PublicId = "cus_other_occupant",
                CreatedAt = now,
                LastUsedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext(companyId: 40))
        {
            var svc = new AssistantPseudonymService(db, _keyProvider, clock);
            var token = await svc.PseudonymizeAsync(40, "user-1", "sess-col", AssistantPseudonymKind.Customer, "cus_target");

            Assert.Equal(expected12, token);
            Assert.Matches(@"^C-[0-9A-F]{12}$", token);

            var resolved = await svc.ResolveTokenAsync(40, "user-1", "sess-col", token);
            Assert.NotNull(resolved);
            Assert.Equal("cus_target", resolved!.PublicId);
        }
    }

    [Fact]
    public async Task ResolveToken_EnforcesScopeAndExpiry()
    {
        var t0 = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(t0);

        string token;
        await using (var db = CreateDbContext(companyId: 50))
        {
            var svc = new AssistantPseudonymService(db, _keyProvider, clock);
            token = await svc.PseudonymizeAsync(50, "user-1", "sess-scope", AssistantPseudonymKind.Person, "mem_9");

            var ok = await svc.ResolveTokenAsync(50, "user-1", "sess-scope", token);
            Assert.NotNull(ok);
            Assert.Equal("person", ok!.Kind);
            Assert.Equal("mem_9", ok.PublicId);

            // Wrong session, wrong user, null session -> null
            Assert.Null(await svc.ResolveTokenAsync(50, "user-1", "sess-other", token));
            Assert.Null(await svc.ResolveTokenAsync(50, "user-2", "sess-scope", token));
            Assert.Null(await svc.ResolveTokenAsync(50, "user-1", null, token));

            // Expired -> null
            clock.SetUtcNow(t0.AddDays(31));
            Assert.Null(await svc.ResolveTokenAsync(50, "user-1", "sess-scope", token));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
