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
using Core.Domain.Financials;
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
                        using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
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

    [Fact]
    public async Task DuplicateInvitationAcceptance_Serializes_OnlyOneSucceeds()
    {
        var connStr = "DataSource=file:dup-invite-race-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();

        int companyId;
        int inviteId;
        string tokenHash = "hash-" + Guid.NewGuid().ToString("N");
        var bootstrapTenant = new TenantContext();
        using (var bootstrapConn = new SqliteConnection(connStr))
        {
            bootstrapConn.Open();
            using var bootstrap = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(bootstrapConn).Options, bootstrapTenant);
            bootstrap.Database.EnsureCreated();
            var company = new Company { Name = "Invite Race Co", Slug = "invite-race-co" };
            bootstrap.Companies.Add(company);
            bootstrap.SaveChanges();
            companyId = company.Id;

            using (bootstrapTenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
            {
                bootstrap.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = companyId,
                    CreatedAtUtc = DateTime.UtcNow
                });
                bootstrap.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    Purpose = WorkspacePurpose.Business,
                    CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                    CollaborationState = WorkspaceCollaborationState.SoloActive,
                    PolicyRevision = 1,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                var invite = new CompanyInvitation
                {
                    CompanyId = companyId,
                    Email = "invitee@example.com",
                    Role = CompanyMemberRole.Member,
                    TokenHash = tokenHash,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    SecurityRoleIdsJson = "[]"
                };
                bootstrap.CompanyInvitations.Add(invite);
                bootstrap.SaveChanges();
                inviteId = invite.Id;
            }
        }

        var barrier = new Barrier(2);
        int successCount = 0;
        int conflictCount = 0;

        Task TryAccept(string userId) => Task.Run(() =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var localTenant = new TenantContext();
                    using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                    using var conn = new SqliteConnection(connStr);
                    conn.Open();
                    using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options, localTenant);
                    var transaction = new WorkspacePolicyTransaction(db, localTenant);

                    var res = transaction.Execute<bool>(companyId, guard =>
                    {
                        var invitation = db.CompanyInvitations.IgnoreQueryFilters().Single(i => i.Id == inviteId);
                        if (invitation.AcceptedAt.HasValue)
                            return false;

                        var transition = WorkspaceInvitationTransition.ValidateOnAccept(db, invitation, Array.Empty<int>(), guard.Settings.PolicyRevision);
                        if (!transition.Ok)
                            return false;

                        invitation.AcceptedAt = DateTime.UtcNow;
                        invitation.AcceptedByUserId = userId;
                        db.CompanyMembers.Add(new CompanyMember
                        {
                            CompanyId = companyId,
                            UserId = userId,
                            Role = invitation.Role,
                            Status = CompanyMemberStatus.Active,
                            JoinedAt = DateTime.UtcNow
                        });
                        guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                        guard.Settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                        guard.AdvanceRevision();
                        db.SaveChanges();
                        return true;
                    });

                    if (res)
                        Interlocked.Increment(ref successCount);
                    else
                        Interlocked.Increment(ref conflictCount);
                    return;
                }
                catch (SqliteException) when (attempt < 9)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        });

        await Task.WhenAll(TryAccept("user-A"), TryAccept("user-B"));

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        using var verifyConn = new SqliteConnection(connStr);
        verifyConn.Open();
        using var verify = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(verifyConn).Options, new TenantContext());
        var members = verify.CompanyMembers.IgnoreQueryFilters().Where(m => m.CompanyId == companyId).ToList();
        Assert.Single(members);
        var settings = verify.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
        Assert.Equal(WorkspaceCollaborationState.TeamActive, settings.CollaborationState);
    }

    [Fact]
    public async Task ConcurrentInvitationAccept_AndJournalPost_NeverLeavesSoloWithTwoActiveHumans()
    {
        var connStr = "DataSource=file:inv-post-race-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();

        int companyId;
        int inviteId;
        var bootstrapTenant = new TenantContext();
        using (var bootstrapConn = new SqliteConnection(connStr))
        {
            bootstrapConn.Open();
            using var bootstrap = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(bootstrapConn).Options, bootstrapTenant);
            bootstrap.Database.EnsureCreated();
            var company = new Company { Name = "Race Co 2", Slug = "race-co-2" };
            bootstrap.Companies.Add(company);
            bootstrap.SaveChanges();
            companyId = company.Id;

            using (bootstrapTenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
            {
                bootstrap.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = companyId,
                    CreatedAtUtc = DateTime.UtcNow
                });
                bootstrap.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    Purpose = WorkspacePurpose.Business,
                    CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                    CollaborationState = WorkspaceCollaborationState.SoloActive,
                    PolicyRevision = 1,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                bootstrap.CompanyMembers.Add(new CompanyMember
                {
                    CompanyId = companyId,
                    UserId = "owner-user",
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                });
                var invite = new CompanyInvitation
                {
                    CompanyId = companyId,
                    Email = "newmember@example.com",
                    Role = CompanyMemberRole.Member,
                    TokenHash = "racehash-" + Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    SecurityRoleIdsJson = "[]"
                };
                bootstrap.CompanyInvitations.Add(invite);
                bootstrap.SaveChanges();
                inviteId = invite.Id;
            }
        }

        var barrier = new Barrier(2);

        Task AcceptInvite() => Task.Run(() =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var localTenant = new TenantContext();
                    using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                    using var conn = new SqliteConnection(connStr);
                    conn.Open();
                    using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options, localTenant);
                    var tx = new WorkspacePolicyTransaction(db, localTenant);
                    tx.Execute(companyId, guard =>
                    {
                        var invitation = db.CompanyInvitations.IgnoreQueryFilters().Single(i => i.Id == inviteId);
                        invitation.AcceptedAt = DateTime.UtcNow;
                        invitation.AcceptedByUserId = "member-user";
                        db.CompanyMembers.Add(new CompanyMember
                        {
                            CompanyId = companyId,
                            UserId = "member-user",
                            Role = invitation.Role,
                            Status = CompanyMemberStatus.Active,
                            JoinedAt = DateTime.UtcNow
                        });
                        db.SaveChanges();
                        var activeHumans = db.CompanyMembers.IgnoreQueryFilters().Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);
                        if (activeHumans >= 2)
                        {
                            guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                            guard.Settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                        }
                        guard.AdvanceRevision();
                        db.SaveChanges();
                    });
                    return;
                }
                catch (SqliteException) when (attempt < 9)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        });

        Task PostJournal() => Task.Run(() =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var localTenant = new TenantContext();
                    using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                    using var conn = new SqliteConnection(connStr);
                    conn.Open();
                    using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options, localTenant);
                    var tx = new WorkspacePolicyTransaction(db, localTenant);
                    tx.Execute(companyId, guard =>
                    {
                        Thread.Sleep(10);
                        guard.AdvanceRevision();
                        db.SaveChanges();
                    });
                    return;
                }
                catch (SqliteException) when (attempt < 9)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        });

        await Task.WhenAll(AcceptInvite(), PostJournal());

        using var verifyConn = new SqliteConnection(connStr);
        verifyConn.Open();
        using var verify = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(verifyConn).Options, new TenantContext());
        var activeMembers = verify.CompanyMembers.IgnoreQueryFilters().Where(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active).Count();
        var finalSettings = verify.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
        
        Assert.Equal(2, activeMembers);
        Assert.Equal(WorkspaceCollaborationState.TeamActive, finalSettings.CollaborationState);
        Assert.NotNull(finalSettings.EverTeamActivatedAtUtc);
    }

    [Fact]
    public void WorkspacePolicyTestGate_CoordinatesRoleRevocation_RollsBackJournalPost()
    {
        var connStr = "DataSource=file:gate-race-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        using var keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        int companyId;
        var bootstrapTenant = new TenantContext();
        using (var bootstrapConn = new SqliteConnection(connStr))
        {
            bootstrapConn.Open();
            using var bootstrap = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(bootstrapConn).Options, bootstrapTenant);
            bootstrap.Database.EnsureCreated();
            var company = new Company { Name = "Gate Co", Slug = "gate-co" };
            bootstrap.Companies.Add(company);
            bootstrap.SaveChanges();
            companyId = company.Id;

            using (bootstrapTenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
            {
                bootstrap.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = companyId,
                    CreatedAtUtc = DateTime.UtcNow
                });
                bootstrap.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    Purpose = WorkspacePurpose.Business,
                    CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                    CollaborationState = WorkspaceCollaborationState.SoloActive,
                    PolicyRevision = 1,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                bootstrap.SaveChanges();
            }
        }

        bool gateTriggered = false;
        try
        {
            Core.Authorization.WorkspacePolicyTestGate.AfterAuthorizationRead = (cid) =>
            {
                gateTriggered = true;
                throw new InvalidOperationException("Role revoked concurrently.");
            };

            var postTenant = new TenantContext();
            using var impersonationPost = postTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            using var postConn = new SqliteConnection(connStr);
            postConn.Open();
            using var postDb = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(postConn).Options, postTenant);
            var tx = new WorkspacePolicyTransaction(postDb, postTenant);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                tx.Execute(companyId, guard =>
                {
                    postDb.GeneralLedgerHeaders.Add(new GeneralLedgerHeader
                    {
                        CompanyId = companyId,
                        Date = DateTime.UtcNow,
                        DocumentType = DocumentTypes.JournalEntry,
                        Description = "Test GL"
                    });
                    postDb.SaveChanges();

                    Core.Authorization.WorkspacePolicyTestGate.AfterAuthorizationRead?.Invoke(companyId);

                    guard.AdvanceRevision();
                });
            });

            Assert.True(gateTriggered);
            Assert.Contains("Role revoked", ex.Message);

            using var verifyConn = new SqliteConnection(connStr);
            verifyConn.Open();
            using var verifyDb = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(verifyConn).Options, new TenantContext());
            var glHeaders = verifyDb.GeneralLedgerHeaders.IgnoreQueryFilters().Where(h => h.CompanyId == companyId).ToList();
            Assert.Empty(glHeaders);

            var settings = verifyDb.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
            Assert.Equal(1, settings.PolicyRevision);
        }
        finally
        {
            Core.Authorization.WorkspacePolicyTestGate.Reset();
        }
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
