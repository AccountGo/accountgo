// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// SQLite Serializable coordination smoke: two writers advance PolicyRevision without losing updates.
/// Cross-provider FOR UPDATE / UPDLOCK suites remain for dedicated integration hosts.
/// </summary>
public class WorkspacePolicyCoordinationRaceTests
{
    private const string SharedMemoryConnectionString =
        "DataSource=file:workspace-policy-race?mode=memory&cache=shared";

    [Fact]
    public async Task ConcurrentPolicyAdvances_DoNotLoseRevisions_OnSqlite()
    {
        // Keep one connection open so the shared in-memory database stays alive.
        await using var keepAlive = new SqliteConnection(SharedMemoryConnectionString);
        await keepAlive.OpenAsync();

        int companyId;
        var bootstrapTenant = new TenantContext();
        using (var bootstrapConn = OpenConnection())
        using (var bootstrap = CreateDb(bootstrapConn, bootstrapTenant))
        {
            bootstrap.Database.EnsureCreated();
            var company = new Company { Name = "Race Co", Slug = "race-co" };
            bootstrap.Companies.Add(company);
            bootstrap.SaveChanges();
            using (bootstrapTenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
            {
                bootstrap.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = company.Id,
                    CreatedAtUtc = DateTime.UtcNow
                });
                bootstrap.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = company.Id,
                    Purpose = WorkspacePurpose.Business,
                    CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                    CollaborationState = WorkspaceCollaborationState.SoloActive,
                    InitializationSource = WorkspaceInitializationSource.NewWorkspace,
                    InitializedAtUtc = DateTime.UtcNow,
                    PolicyRevision = 0,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                bootstrap.SaveChanges();
            }

            companyId = company.Id;
        }

        var barrier = new Barrier(2);
        Exception? fault = null;

        Task Advance() => Task.Run(() =>
        {
            try
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        var localTenant = new TenantContext();
                        using var conn = OpenConnection();
                        using var db = CreateDb(conn, localTenant);
                        new WorkspacePolicyTransaction(db, localTenant).Execute(companyId, guard =>
                        {
                            Thread.Sleep(10);
                            guard.AdvanceRevision();
                        });
                        return;
                    }
                    catch (SqliteException) when (attempt < 9)
                    {
                        Thread.Sleep(15 * (attempt + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref fault, ex, null);
            }
        });

        await Task.WhenAll(Advance(), Advance());
        if (fault != null)
            throw new Xunit.Sdk.XunitException("Concurrent policy writer failed: " + fault);

        using var verifyConn = OpenConnection();
        using var verify = CreateDb(verifyConn, new TenantContext());
        var revision = verify.WorkspaceOperatingSettings.IgnoreQueryFilters()
            .Single(s => s.CompanyId == companyId).PolicyRevision;
        Assert.Equal(2, revision);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(SharedMemoryConnectionString);
        connection.Open();
        return connection;
    }

    private static ApiDbContext CreateDb(SqliteConnection connection, TenantContext tenant) =>
        new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options, tenant);
}
