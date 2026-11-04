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
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// Coordination-row race tests for <see cref="WorkspacePolicyTransaction"/>: concurrent writers advance PolicyRevision
/// without losing updates, and policy-sensitive work stays serialized per workspace.
/// Each test is a Theory over <see cref="PolicyRaceDatabase.ConfiguredProviders"/>. SQLite (shared-cache in-memory) always
/// runs; when ACCOUNTGO_TEST_POSTGRES is set the same scenarios also run on a disposable PostgreSQL database, with the
/// production write-lock interceptor and again with only the FOR UPDATE row lock plus SERIALIZABLE isolation.
/// Every concurrent writer opens its own connection and retries the provider's transient errors
/// (SQLite lock errors; PostgreSQL 40001 / 40P01 / 55P03, including when wrapped in a DbUpdateException).
/// </summary>
public class WorkspacePolicyCoordinationRaceTests
{
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(5);
    private const int ConcurrentWriterCount = 2;
    private const int PolicyAdvanceRetryDelayMilliseconds = 15;
    private const int InvitationRetryDelayMilliseconds = 20;
    private const int HoldLockMilliseconds = 10;

    public static System.Collections.Generic.IEnumerable<object[]> Providers() => PolicyRaceDatabase.ConfiguredProviders();

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ConcurrentPolicyAdvances_DoNotLoseRevisions_OnConfiguredProvider(string provider)
    {
        using var database = PolicyRaceDatabase.Create(provider);
        var companyId = SeedWorkspace(database, "Race Co", "race-co", policyRevision: 0, customizeSettings: settings =>
        {
            settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
            settings.InitializedAtUtc = DateTime.UtcNow;
        });

        var barrier = new Barrier(ConcurrentWriterCount);

        Task Advance() => Task.Run(() =>
        {
            barrier.SignalAndWait(BarrierTimeout);
            RunWithRetry(database, PolicyAdvanceRetryDelayMilliseconds, () =>
            {
                var localTenant = new TenantContext();
                using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                using var context = database.CreateContext(localTenant);
                new WorkspacePolicyTransaction(context.Db, localTenant).Execute(companyId, guard =>
                {
                    Thread.Sleep(HoldLockMilliseconds);
                    guard.AdvanceRevision();
                });
            });
        });

        await Task.WhenAll(Advance(), Advance());

        using var verify = database.CreateContext(new TenantContext());
        var revision = verify.Db.WorkspaceOperatingSettings.IgnoreQueryFilters()
            .Single(s => s.CompanyId == companyId).PolicyRevision;
        Assert.Equal(ConcurrentWriterCount, revision);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task DuplicateInvitationAcceptance_Serializes_OnlyOneSucceeds_OnConfiguredProvider(string provider)
    {
        using var database = PolicyRaceDatabase.Create(provider);

        var inviteId = 0;
        var tokenHash = "hash-" + Guid.NewGuid().ToString("N");
        var companyId = SeedWorkspace(database, "Invite Race Co", "invite-race-co", policyRevision: 1, seedMore: (bootstrap, seededCompanyId) =>
        {
            var invite = new CompanyInvitation
            {
                CompanyId = seededCompanyId,
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
        });
        Assert.True(inviteId > 0);

        var barrier = new Barrier(ConcurrentWriterCount);
        var successCount = 0;
        var conflictCount = 0;

        Task TryAccept(string userId) => Task.Run(() =>
        {
            barrier.SignalAndWait(BarrierTimeout);
            RunWithRetry(database, InvitationRetryDelayMilliseconds, () =>
            {
                var localTenant = new TenantContext();
                using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                using var context = database.CreateContext(localTenant);
                var db = context.Db;
                var transaction = new WorkspacePolicyTransaction(db, localTenant);

                var accepted = transaction.Execute<bool>(companyId, guard =>
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

                if (accepted)
                    Interlocked.Increment(ref successCount);
                else
                    Interlocked.Increment(ref conflictCount);
            });
        });

        await Task.WhenAll(TryAccept("user-A"), TryAccept("user-B"));

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        using var verify = database.CreateContext(new TenantContext());
        var members = verify.Db.CompanyMembers.IgnoreQueryFilters().Where(m => m.CompanyId == companyId).ToList();
        Assert.Single(members);
        var settings = verify.Db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
        Assert.Equal(WorkspaceCollaborationState.TeamActive, settings.CollaborationState);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ConcurrentInvitationAccept_AndJournalPost_NeverLeavesSoloWithTwoActiveHumans_OnConfiguredProvider(string provider)
    {
        using var database = PolicyRaceDatabase.Create(provider);

        var inviteId = 0;
        var companyId = SeedWorkspace(database, "Race Co 2", "race-co-2", policyRevision: 1, seedMore: (bootstrap, seededCompanyId) =>
        {
            bootstrap.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = seededCompanyId,
                UserId = "owner-user",
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            });
            var invite = new CompanyInvitation
            {
                CompanyId = seededCompanyId,
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
        });
        Assert.True(inviteId > 0);

        var barrier = new Barrier(ConcurrentWriterCount);

        Task AcceptInvite() => Task.Run(() =>
        {
            barrier.SignalAndWait(BarrierTimeout);
            RunWithRetry(database, InvitationRetryDelayMilliseconds, () =>
            {
                var localTenant = new TenantContext();
                using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                using var context = database.CreateContext(localTenant);
                var db = context.Db;
                new WorkspacePolicyTransaction(db, localTenant).Execute(companyId, guard =>
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
                    var activeHumans = db.CompanyMembers.IgnoreQueryFilters()
                        .Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);
                    if (activeHumans >= 2)
                    {
                        guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                        guard.Settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                    }
                    guard.AdvanceRevision();
                    db.SaveChanges();
                });
            });
        });

        Task PostJournal() => Task.Run(() =>
        {
            barrier.SignalAndWait(BarrierTimeout);
            RunWithRetry(database, InvitationRetryDelayMilliseconds, () =>
            {
                var localTenant = new TenantContext();
                using var impersonation = localTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                using var context = database.CreateContext(localTenant);
                var db = context.Db;
                new WorkspacePolicyTransaction(db, localTenant).Execute(companyId, guard =>
                {
                    Thread.Sleep(HoldLockMilliseconds);
                    guard.AdvanceRevision();
                    db.SaveChanges();
                });
            });
        });

        await Task.WhenAll(AcceptInvite(), PostJournal());

        using var verify = database.CreateContext(new TenantContext());
        var activeMembers = verify.Db.CompanyMembers.IgnoreQueryFilters()
            .Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);
        var finalSettings = verify.Db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);

        Assert.Equal(2, activeMembers);
        Assert.Equal(WorkspaceCollaborationState.TeamActive, finalSettings.CollaborationState);
        Assert.NotNull(finalSettings.EverTeamActivatedAtUtc);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void WorkspacePolicyTestGate_CoordinatesRoleRevocation_RollsBackJournalPost_OnConfiguredProvider(string provider)
    {
        using var database = PolicyRaceDatabase.Create(provider);
        var companyId = SeedWorkspace(database, "Gate Co", "gate-co", policyRevision: 1);

        var gateTriggered = false;
        try
        {
            Core.Authorization.WorkspacePolicyTestGate.AfterAuthorizationRead = _ =>
            {
                gateTriggered = true;
                throw new InvalidOperationException("Role revoked concurrently.");
            };

            var postTenant = new TenantContext();
            using var impersonationPost = postTenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            using var postContext = database.CreateContext(postTenant);
            var postDb = postContext.Db;
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

            using var verify = database.CreateContext(new TenantContext());
            var glHeaders = verify.Db.GeneralLedgerHeaders.IgnoreQueryFilters().Where(h => h.CompanyId == companyId).ToList();
            Assert.Empty(glHeaders);

            var settings = verify.Db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == companyId);
            Assert.Equal(1, settings.PolicyRevision);
        }
        finally
        {
            Core.Authorization.WorkspacePolicyTestGate.Reset();
        }
    }

    /// <summary>
    /// Creates a company with its coordination row and operating settings. <paramref name="customizeSettings"/> adjusts the
    /// settings row before it is saved; <paramref name="seedMore"/> runs under the company's tenant scope and must save.
    /// </summary>
    private static int SeedWorkspace(
        PolicyRaceDatabase database,
        string name,
        string slug,
        long policyRevision,
        Action<WorkspaceOperatingSettings>? customizeSettings = null,
        Action<ApiDbContext, int>? seedMore = null)
    {
        var bootstrapTenant = new TenantContext();
        using var context = database.CreateContext(bootstrapTenant);
        var bootstrap = context.Db;

        var company = new Company { Name = name, Slug = slug };
        bootstrap.Companies.Add(company);
        bootstrap.SaveChanges();
        var companyId = company.Id;

        using (bootstrapTenant.Impersonate(companyId, company.Slug, CompanyMemberRole.Owner))
        {
            bootstrap.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
            {
                CompanyId = companyId,
                CreatedAtUtc = DateTime.UtcNow
            });
            var settings = new WorkspaceOperatingSettings
            {
                CompanyId = companyId,
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                CollaborationState = WorkspaceCollaborationState.SoloActive,
                PolicyRevision = policyRevision,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
            };
            customizeSettings?.Invoke(settings);
            bootstrap.WorkspaceOperatingSettings.Add(settings);
            bootstrap.SaveChanges();
            seedMore?.Invoke(bootstrap, companyId);
        }

        return companyId;
    }

    /// <summary>
    /// Runs <paramref name="writer"/> and retries the provider's transient errors with a linear back-off. The last attempt
    /// lets any exception propagate so a persistent failure fails the test.
    /// </summary>
    private static void RunWithRetry(PolicyRaceDatabase database, int retryDelayMilliseconds, Action writer)
    {
        for (var attempt = 0; attempt < PolicyRaceDatabase.MaxWriterAttempts; attempt++)
        {
            try
            {
                writer();
                return;
            }
            catch (Exception ex) when (attempt < PolicyRaceDatabase.MaxWriterAttempts - 1 && database.IsTransient(ex))
            {
                Thread.Sleep(retryDelayMilliseconds * (attempt + 1));
            }
        }
    }
}
