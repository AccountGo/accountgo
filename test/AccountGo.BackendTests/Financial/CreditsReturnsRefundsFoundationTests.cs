// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Schema;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class CreditsReturnsRefundsFoundationTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId) => CompanyId = companyId;
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId => "test-user";
            public bool IsResolved => CompanyId.HasValue;
            public System.Collections.Generic.IReadOnlySet<string> Permissions => new System.Collections.Generic.HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, System.Collections.Generic.IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        [Fact]
        public void CurrencyPrecisionRules_ValidatesAndRejectsUnsupportedCurrencies()
        {
            Assert.Equal(2, CurrencyPrecisionRules.RequireSupportedPrecision("usd"));
            Assert.Equal(0, CurrencyPrecisionRules.RequireSupportedPrecision("JPY"));
            Assert.Equal(3, CurrencyPrecisionRules.RequireSupportedPrecision("KWD"));
            Assert.True(CurrencyPrecisionRules.HasValidPrecision(12.34m, 2));
            Assert.False(CurrencyPrecisionRules.HasValidPrecision(12.345m, 2));
            Assert.True(CurrencyPrecisionRules.HasValidPrecision(100m, 0));
            Assert.False(CurrencyPrecisionRules.HasValidPrecision(100.5m, 0));

            Assert.Throws<InvalidOperationException>(() => CurrencyPrecisionRules.RequireSupportedPrecision("XYZ"));
        }

        [Fact]
        public void SqliteMigration_And_Coordinator_FeatureGate_Idempotency_Rollback_Work()
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
            using var db = new ApiDbContext(options, new FixedTenantContext(1));
            db.Database.EnsureCreated();
            SqliteSchemaUpgrader.Upgrade(conn);

            RunCoordinatorAssertions(db);
        }

        [Fact]
        public void PostgresMigration_And_Coordinator_FeatureGate_Idempotency_Rollback_Work()
        {
            using var pg = PostgresTestDatabase.CreateIfConfigured();
            Assert.NotNull(pg);

            var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                .UseNpgsql(pg!.ConnectionString)
                .Options;
            using var db = new PostgresApiDbContext(options, new FixedTenantContext(1));
            db.Database.Migrate();

            RunCoordinatorAssertions(db);
        }

        private static void RunCoordinatorAssertions(ApiDbContext db)
        {
            var company = db.Companies.FirstOrDefault(c => c.Id == 1);
            if (company == null)
            {
                company = new Company { Name = "Test Co", CompanyCode = "TST", Slug = "tst-" + Guid.NewGuid().ToString("N")[..8] };
                db.Companies.Add(company);
                db.SaveChanges();
            }

            var tenant = new FixedTenantContext(company.Id);
            var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id);
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = company.Id, Currency = "USD" };
                db.CompanySettings.Add(setting);
                db.SaveChanges();
            }

            Assert.False(setting.CreditsReturnsRefundsEnabled);

            var coordinator = new SettlementCommandCoordinator(db, tenant);
            var gated = Assert.Throws<SettlementCommandException>(() =>
                coordinator.Execute("test.op", "key-1", new { Amount = 10 }, (_, _) => 1, id => id, "Test"));
            Assert.Equal(SettlementErrorCodes.FeatureDisabled, gated.Code);

            setting.CreditsReturnsRefundsEnabled = true;
            db.SaveChanges();

            // Idempotency: first run executes, second run with same key+payload returns cached without re-executing
            int executions = 0;
            var r1 = coordinator.Execute("test.op", "key-1", new { Amount = 10 }, (_, _) => { executions++; return 42; }, id => id, "Test");
            var r2 = coordinator.Execute("test.op", "key-1", new { Amount = 10 }, (_, _) => { executions++; return 99; }, id => id, "Test");
            Assert.Equal(42, r1);
            Assert.Equal(42, r2);
            Assert.Equal(1, executions);

            // Idempotency conflict on altered payload
            var conflict = Assert.Throws<SettlementCommandException>(() =>
                coordinator.Execute("test.op", "key-1", new { Amount = 20 }, (_, _) => 100, id => id, "Test"));
            Assert.Equal(SettlementErrorCodes.IdempotencyConflict, conflict.Code);

            // Rollback on failure leaves no command receipt
            Assert.Throws<SettlementCommandException>(() =>
                coordinator.Execute<object, int>("test.op", "key-rollback", new { Amount = 30 }, (_, _) => throw new InvalidOperationException("boom"), id => id, "Test"));
            Assert.False(db.SettlementCommandReceipts.Any(r => r.CompanyId == company.Id && r.IdempotencyKey == "key-rollback"));
        }
    }
}
