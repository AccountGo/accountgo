// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

public class WorkspaceInvitationTransitionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();

    public WorkspaceInvitationTransitionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void ComputeGrantFingerprint_IsStableForSameRoleSet()
    {
        var a = WorkspaceInvitationTransition.ComputeGrantFingerprint(CompanyMemberRole.Member, new[] { 3, 1, 2 });
        var b = WorkspaceInvitationTransition.ComputeGrantFingerprint(CompanyMemberRole.Member, new[] { 1, 2, 3 });
        Assert.Equal(a, b);
        Assert.NotEqual(
            a,
            WorkspaceInvitationTransition.ComputeGrantFingerprint(CompanyMemberRole.Admin, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void ValidateOnAccept_RejectsWhenPolicyAdvancesWithJournalIntent()
    {
        var company = new Company { Name = "Invite Co", Slug = "invite-co" };
        _db.Companies.Add(company);
        _db.SaveChanges();

        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var invitation = new CompanyInvitation
            {
                CompanyId = company.Id,
                Email = "a@example.com",
                Role = CompanyMemberRole.Member,
                TokenHash = "t",
                InvitedByUserId = "owner",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                SecurityRoleIdsJson = "[1]"
            };
            _db.CompanyInvitations.Add(invitation);
            _db.SaveChanges();

            WorkspaceInvitationTransition.Record(
                _db, company.Id, invitation.Id, "owner", CompanyMemberRole.Member, new[] { 1 },
                issuingPolicyRevision: 1, intendedJournalMode: "SingleActorAllowed");
            _db.SaveChanges();

            var ok = WorkspaceInvitationTransition.ValidateOnAccept(_db, invitation, new[] { 1 }, currentPolicyRevision: 1);
            Assert.True(ok.Ok);

            var stale = WorkspaceInvitationTransition.ValidateOnAccept(_db, invitation, new[] { 1 }, currentPolicyRevision: 2);
            Assert.False(stale.Ok);
            Assert.Equal("Workspace setup changed", stale.Title);
        }
    }
}
