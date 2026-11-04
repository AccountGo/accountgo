// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

public class WorkspaceOperatingPolicyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();
    private readonly string _ownerUserId = "owner-1";

    public WorkspaceOperatingPolicyServiceTests()
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

    private (Company company, CompanyMember owner) SeedSoloInitializedCompany()
    {
        var company = new Company
        {
            Name = "Solo Co",
            Slug = "solo-co-" + Guid.NewGuid().ToString("N")[..8],
            CreatedByUserId = _ownerUserId,
            AccessBootstrapAvailable = false
        };
        _db.Companies.Add(company);
        _db.SaveChanges();

        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var owner = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = _ownerUserId,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            _db.CompanyMembers.Add(owner);
            _db.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
            {
                CompanyId = company.Id,
                CreatedAtUtc = DateTime.UtcNow
            });
            _db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
            {
                CompanyId = company.Id,
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                CollaborationState = WorkspaceCollaborationState.SoloActive,
                InitializationSource = WorkspaceInitializationSource.NewWorkspace,
                InitializedAtUtc = DateTime.UtcNow,
                InitializedByUserId = _ownerUserId,
                ConfirmedOwnerMemberId = null, // set after save once owner.Id known
                PolicyRevision = 1,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
            });
            _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
            {
                CompanyId = company.Id,
                OperationKey = Core.Authorization.AccessOperationCatalog.FinancialsJournalPost,
                RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion,
                ConfiguredAtUtc = DateTime.UtcNow,
                ConfiguredAtPolicyRevision = 1,
                ConfiguredByUserId = _ownerUserId
            });
            _db.SaveChanges();

            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.ConfirmedOwnerMemberId = owner.Id;
            _db.SaveChanges();
            return (company, owner);
        }
    }

    [Fact]
    public void ReturnToSolo_RevokesOutstandingInvites_PreservesJournalRuleAndGrants()
    {
        var (company, owner) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            _db.CompanyInvitations.Add(new CompanyInvitation
            {
                CompanyId = company.Id,
                Email = "bookkeeper@example.com",
                Role = CompanyMemberRole.Member,
                TokenHash = "hash",
                InvitedByUserId = _ownerUserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                SecurityRoleIdsJson = "[]"
            });
            // Simulate prior team activation history without other active humans.
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
            settings.EverTeamActivatedAtUtc = DateTime.UtcNow.AddDays(-1);
            settings.PolicyRevision = 2;
            _db.SaveChanges();

            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(_db, soloSetupEnabled: true, tenant: _tenant);
            var result = policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 2, revokeOutstandingInvitations: true);

            Assert.True(result.Applied);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, result.CollaborationState);
            Assert.NotNull(settings.EverTeamActivatedAtUtc);
            Assert.All(_db.CompanyInvitations.IgnoreQueryFilters().Where(i => i.CompanyId == company.Id),
                i => Assert.NotNull(i.RevokedAt));
            Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed,
                _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id).RuleKind);
            Assert.Equal(owner.Id, settings.ConfirmedOwnerMemberId);
        }
    }

    [Fact]
    public void ReturnToSolo_UnderSoloSetupEnabled_DoesNotNeedTheLegacyConfirmationFlag()
    {
        var (company, owner) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
            settings.PolicyRevision = 6;
            _db.SaveChanges();

            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(
                _db, soloSetupEnabled: true, tenant: _tenant, legacySoloConfirmationEnabled: false);

            Assert.Contains("return_to_solo", policy.GetSummary(company.Id, owner.Id).AllowedNextActions);
            var result = policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 6, revokeOutstandingInvitations: false);

            Assert.True(result.Applied, result.Message);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, result.CollaborationState);
        }
    }

    [Fact]
    public void ReturnToSolo_IsAdmittedForCurrentModelWorkspace_WhenSetupFlagIsOff_ButNotForLegacyOnes()
    {
        var (company, owner) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
            settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
            settings.PolicyRevision = 8;
            _db.SaveChanges();

            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(
                _db, soloSetupEnabled: false, tenant: _tenant, legacySoloConfirmationEnabled: false);
            Assert.True(policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 8, revokeOutstandingInvitations: false).Applied);

            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
            settings.InitializationSource = WorkspaceInitializationSource.VerifiedLegacySetup;
            settings.PolicyRevision = 9;
            _db.SaveChanges();

            var legacy = policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 9, revokeOutstandingInvitations: false);
            Assert.False(legacy.Applied);
            Assert.Equal(Core.Authorization.WorkspacePolicyReasonCodes.SoloSetupDisabled, legacy.ReasonCode);
            Assert.DoesNotContain("return_to_solo", policy.GetSummary(company.Id, owner.Id).AllowedNextActions);
        }
    }

    [Fact]
    public void ReturnToSolo_RejectsWhenOutstandingInvitesNotRevoked()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            _db.CompanyInvitations.Add(new CompanyInvitation
            {
                CompanyId = company.Id,
                Email = "pending@example.com",
                Role = CompanyMemberRole.Member,
                TokenHash = "hash2",
                InvitedByUserId = _ownerUserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                SecurityRoleIdsJson = "[]"
            });
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.PolicyRevision = 3;
            _db.SaveChanges();

            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(_db, soloSetupEnabled: true, tenant: _tenant);
            var result = policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 3, revokeOutstandingInvitations: false);

            Assert.False(result.Applied);
            Assert.Equal(Core.Authorization.WorkspacePolicyReasonCodes.ApprovalPolicyChangeRequired, result.ReasonCode);
            Assert.Null(_db.CompanyInvitations.IgnoreQueryFilters().Single(i => i.CompanyId == company.Id).RevokedAt);
        }
    }

    [Fact]
    public void UpdateConfirmedOwnerMember_AdvancesRevision_WithoutAutoGrant()
    {
        var (company, owner) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var newOwner = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = "new-owner",
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            _db.CompanyMembers.Add(newOwner);
            _db.SaveChanges();

            var before = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id).PolicyRevision;
            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(_db, soloSetupEnabled: true, tenant: _tenant);
            policy.UpdateConfirmedOwnerMember(company.Id, newOwner.Id, actorUserId: _ownerUserId);

            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            Assert.Equal(newOwner.Id, settings.ConfirmedOwnerMemberId);
            Assert.True(settings.PolicyRevision > before);
            Assert.False(_db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Any(r => r.CompanyMemberId == newOwner.Id));
            Assert.Equal(owner.UserId, _ownerUserId);
        }
    }

    [Fact]
    public void EnsureCreated_IncludesOperatingPolicyTables_AndUpgradeIsIdempotent()
    {
        foreach (var table in new[] { "WorkspaceOperatingSettings", "WorkspaceApprovalRule", "WorkspacePolicyCoordination" })
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        // Repeat upgrade against an already-current schema must not throw or drop policy rows.
        var (company, _) = SeedSoloInitializedCompany();
        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(_connection);
        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(_connection);

        Assert.True(_db.WorkspaceOperatingSettings.IgnoreQueryFilters().Any(s => s.CompanyId == company.Id));
        Assert.True(_db.WorkspacePolicyCoordinations.IgnoreQueryFilters().Any(c => c.CompanyId == company.Id));
        Assert.True(_db.WorkspaceApprovalRules.IgnoreQueryFilters().Any(r => r.CompanyId == company.Id));
    }

    [Fact]
    public void FlagOff_RejectsNewSoloConfirm_ButInitializedJournalPolicyStillResolves()
    {
        var (company, owner) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var disabled = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(_db, soloSetupEnabled: false, tenant: _tenant);
            var confirm = disabled.ConfirmLegacySoloSetup(company.Id, _ownerUserId, expectedRevision: 1);
            Assert.False(confirm.Applied);
            Assert.Equal(Core.Authorization.WorkspacePolicyReasonCodes.SoloSetupDisabled, confirm.ReasonCode);

            var summary = disabled.GetSummary(company.Id, owner.Id);
            Assert.Equal(WorkspaceCollaborationState.SoloActive, summary.CollaborationState);
            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.SingleActorAllowed, summary.NormalJournalWorkflow.Mode);
            Assert.False(summary.SoloSetupEnabled);
        }
    }

    [Fact]
    public void ReturnToSolo_DoesNotMutateAccountingIdentity()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            _db.CompanySettings.Add(new CompanySetting
            {
                CompanyId = company.Id,
                Currency = "USD",
                EntityType = EntityType.Corporation
            });
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
            settings.EverTeamActivatedAtUtc = DateTime.UtcNow.AddDays(-2);
            settings.PolicyRevision = 4;
            _db.SaveChanges();

            var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(_db, soloSetupEnabled: true, tenant: _tenant);
            Assert.True(policy.ReturnToSolo(company.Id, _ownerUserId, expectedRevision: 4, revokeOutstandingInvitations: true).Applied);

            var companySetting = _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            Assert.Equal(EntityType.Corporation, companySetting.EntityType);
            Assert.Equal("USD", companySetting.Currency);
        }
    }

    [Theory]
    [InlineData("__WorkspaceOrigin")]
    [InlineData("__InvitationTransition")]
    [InlineData("__JournalPostingAuthorization")]
    [InlineData("__WorkspaceCollaborationEvidence")]
    public void ImmutableAuditTables_CannotBeModifiedOrDeleted(string tableName)
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var log = new Core.Domain.Auditing.AuditLog
            {
                CompanyId = company.Id,
                UserName = _ownerUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                TableName = tableName,
                RecordId = "1",
                FieldName = "Test",
                NewValue = "Original"
            };
            _db.AuditLogs.Add(log);
            _db.SaveChanges();

            // Attempt modify
            log.NewValue = "Tampered";
            var exModify = Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
            Assert.Contains("append-only evidence", exModify.Message);

            // Revert modification in change tracker
            _db.Entry(log).State = EntityState.Unchanged;

            // Attempt delete
            _db.AuditLogs.Remove(log);
            var exDelete = Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
            Assert.Contains("append-only evidence", exDelete.Message);
        }
    }

    [Fact]
    public void ApprovalResolver_MissingRule_DefaultsToCatalogDefault()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var existingRules = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Where(r => r.CompanyId == company.Id).ToList();
            _db.WorkspaceApprovalRules.RemoveRange(existingRules);
            _db.SaveChanges();

            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var result = resolver.Resolve(company.Id, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost);

            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.CatalogDefault, result.Mode);
            Assert.Null(result.Provenance);
        }
    }

    [Fact]
    public void ApprovalResolver_SchemaMismatch_FailsClosedToIndependentReviewer()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
            rule.PolicySchemaVersion = 999;
            _db.SaveChanges();

            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var result = resolver.Resolve(company.Id, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost);

            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.IndependentReviewerRequired, result.Mode);
        }
    }

    [Fact]
    public void ApprovalResolver_SingleActorAllowed_ResolvesCorrectly()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var result = resolver.Resolve(company.Id, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost);

            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.SingleActorAllowed, result.Mode);
        }
    }

    [Fact]
    public void ApprovalResolver_IndependentReviewerRequired_ResolvesCorrectly()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
            rule.RuleKind = WorkspaceApprovalRuleKind.IndependentReviewerRequired;
            _db.SaveChanges();

            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var result = resolver.Resolve(company.Id, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost);

            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.IndependentReviewerRequired, result.Mode);
        }
    }

    [Fact]
    public void PolicyTransaction_SchemaMismatch_ThrowsIncompatiblePolicySchemaException()
    {
        var (company, _) = SeedSoloInitializedCompany();
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            settings.PolicySchemaVersion = 99;
            _db.SaveChanges();

            var tx = new WorkspacePolicyTransaction(_db, _tenant);
            var ex = Assert.Throws<Core.Authorization.IncompatiblePolicySchemaException>(() =>
            {
                tx.Execute(company.Id, guard =>
                {
                    guard.AdvanceRevision();
                    return true;
                });
            });

            Assert.Equal(company.Id, ex.CompanyId);
            Assert.Equal(99, ex.RequestedSchemaVersion);
            Assert.Equal(WorkspaceOperatingPolicySchema.CurrentVersion, ex.SupportedSchemaVersion);
        }
    }
}
