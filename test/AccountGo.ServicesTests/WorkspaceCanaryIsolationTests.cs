// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// Slice 2 — server-side canary isolation. Verifies that the new business/freelance solo audience is
/// admitted only from trusted enrollment, that admission is re-enforced at initialization (so a direct
/// caller cannot bypass the controller check), that the global setup switch alone is insufficient, and
/// that legacy confirmation stays independently closed when only the new-setup switch is enabled.
/// </summary>
public sealed class WorkspaceCanaryIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();

    public WorkspaceCanaryIsolationTests()
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

    /// <summary>Seeds an uninitialized (LegacyUnclassified) workspace with a single active owner,
    /// mirroring the baseline that WorkspaceService.Create lays down before any initialization.</summary>
    private (Company company, CompanyMember owner) SeedUninitializedCompany(string ownerUserId)
    {
        var company = new Company
        {
            Name = "New Co",
            Slug = "new-co-" + Guid.NewGuid().ToString("N")[..8],
            CreatedByUserId = ownerUserId,
            AccessBootstrapAvailable = false
        };
        _db.Companies.Add(company);
        _db.SaveChanges();

        CompanyMember owner;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            owner = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = ownerUserId,
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
                Purpose = WorkspacePurpose.Unspecified,
                CollaborationIntent = WorkspaceCollaborationIntent.Unspecified,
                CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                InitializationSource = WorkspaceInitializationSource.Unspecified,
                PolicyRevision = 0,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
            });
            _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
            {
                CompanyId = company.Id,
                OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                RuleKind = WorkspaceApprovalRuleKind.IndependentReviewerRequired,
                Provenance = WorkspaceApprovalRuleProvenance.LegacyEnforced,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion,
                ConfiguredAtUtc = DateTime.UtcNow,
                ConfiguredAtPolicyRevision = 0
            });
            // Managed Full-access profile role so AssignFullAccess can resolve it on success.
            if (!_db.SecurityRoles.Any(r => r.Name == Core.Domain.Security.WorkspaceAccessProfileKeys.FullWorkspaceAccessV1))
            {
                _db.SecurityRoles.Add(new Core.Domain.Security.SecurityRole
                {
                    Name = Core.Domain.Security.WorkspaceAccessProfileKeys.FullWorkspaceAccessV1,
                    DisplayName = Core.Domain.Security.WorkspaceAccessProfileKeys.FullWorkspaceAccessDisplayName,
                    System = true,
                    SysAdmin = false
                });
            }
            _db.SaveChanges();
        }
        return (company, owner);
    }

    // ---- Enrollment decision unit behavior ----------------------------------------------------

    [Fact]
    public void Enrollment_AdmitsOnlyEnrolledSubjects_WhenSetupEnabled()
    {
        var canary = WorkspaceOperatingPolicyTestSupport.CreateEnrollment(
            soloSetupEnabled: true,
            enrolledUserIds: new[] { "user-in" },
            enrolledEmails: new[] { "in@example.com" });

        Assert.True(canary.IsEnrolled(email: null, userId: "user-in"));
        Assert.True(canary.IsEnrolled(email: "IN@example.com", userId: null)); // case-insensitive
        Assert.True(canary.IsNewBusinessSoloAudienceAdmitted(null, "user-in"));

        Assert.False(canary.IsEnrolled(email: null, userId: "user-out"));
        Assert.False(canary.IsEnrolled(email: "out@example.com", userId: null));
        Assert.False(canary.IsNewBusinessSoloAudienceAdmitted("out@example.com", "user-out"));
    }

    [Fact]
    public void Enrollment_SetupDisabled_AdmitsNobody_EvenWhenEnrolled()
    {
        var canary = WorkspaceOperatingPolicyTestSupport.CreateEnrollment(
            soloSetupEnabled: false,
            enrolledUserIds: new[] { "user-in" });

        // Still "enrolled" as a subject, but not admitted because the global switch is off.
        Assert.True(canary.IsEnrolled(null, "user-in"));
        Assert.False(canary.IsNewBusinessSoloAudienceAdmitted(null, "user-in"));
    }

    [Fact]
    public void Enrollment_EmptyLists_AdmitNobody_ByDefault()
    {
        var canary = WorkspaceOperatingPolicyTestSupport.CreateEnrollment(soloSetupEnabled: true);
        Assert.False(canary.IsEnrolled("anyone@example.com", "anyone"));
        Assert.False(canary.IsNewBusinessSoloAudienceAdmitted("anyone@example.com", "anyone"));
    }

    // ---- Initialization re-enforcement (direct-call bypass protection) ------------------------

    [Fact]
    public void ApplyNewWorkspace_UnenrolledOwner_IsRejected_AndNothingInitialized()
    {
        const string ownerId = "owner-unenrolled";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        // Setup enabled, but this owner is NOT enrolled (another user is). A direct call must refuse.
        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledUserIds: new[] { "someone-else" });

        var result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
        {
            Purpose = WorkspacePurpose.Business,
            CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
            OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
        });

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.CanaryNotEnrolled, result.ReasonCode);

        // Nothing was initialized: still unclassified, no confirmed owner, rule unchanged.
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
        Assert.Null(settings.InitializedAtUtc);
        Assert.Null(settings.ConfirmedOwnerMemberId);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
    }

    [Fact]
    public void ApplyNewWorkspace_EnrolledOwner_InitializesSoloWithSingleActorRule()
    {
        const string ownerId = "owner-enrolled";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledUserIds: new[] { ownerId });

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            });
        }

        Assert.True(result.Applied, $"init rejected: {result.ReasonCode} :: {result.Message}");
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.SoloActive, settings.CollaborationState);
        Assert.NotNull(settings.InitializedAtUtc);
        Assert.Equal(owner.Id, settings.ConfirmedOwnerMemberId);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, rule.RuleKind);
    }

    [Fact]
    public void ApplyNewWorkspace_EnrolledByEmail_IsAdmitted()
    {
        const string ownerId = "owner-email-enrolled";
        const string ownerEmail = "owner@freelancer.example";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        // Enrolled by signup email only (the anonymous-signup subject), not by user id.
        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledEmails: new[] { ownerEmail });

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess,
                OwnerEmail = ownerEmail
            });
        }

        Assert.True(result.Applied);
    }

    [Fact]
    public void ApplyNewWorkspace_SetupDisabled_ReportsSoloSetupDisabled_NotCanary()
    {
        const string ownerId = "owner-x";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        // Setup off: the disabled reason takes precedence over enrollment, and nothing initializes.
        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: false, enrolledUserIds: new[] { ownerId });

        var result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
        {
            Purpose = WorkspacePurpose.Business,
            CollaborationIntent = WorkspaceCollaborationIntent.JustMe
        });

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.SoloSetupDisabled, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
    }

    // ---- Legacy confirmation stays independently closed ---------------------------------------

    [Fact]
    public void LegacyConfirmation_StaysClosed_WhenOnlyNewSetupEnabled()
    {
        const string ownerId = "legacy-owner";
        var (company, _) = SeedUninitializedCompany(ownerId);

        // New-setup switch on, but legacy confirmation flag off: the legacy flow must remain closed.
        var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(
            _db, soloSetupEnabled: true, tenant: _tenant, legacySoloConfirmationEnabled: false);

        var eligibility = policy.EvaluateLegacySoloEligibility(company.Id, ownerId);
        Assert.Equal(SoloSetupEligibilityStatus.Ineligible, eligibility.Status);
        Assert.Equal(WorkspacePolicyReasonCodes.SoloSetupDisabled, eligibility.ReasonCode);
    }

    [Fact]
    public void LegacyConfirmation_Available_WhenLegacyFlagEnabled()
    {
        const string ownerId = "legacy-owner-2";
        var (company, _) = SeedUninitializedCompany(ownerId);

        var policy = WorkspaceOperatingPolicyTestSupport.CreateOperatingPolicy(
            _db, soloSetupEnabled: true, tenant: _tenant, legacySoloConfirmationEnabled: true);

        var eligibility = policy.EvaluateLegacySoloEligibility(company.Id, ownerId);
        // Not blocked by the disabled gate; a genuine always-solo workspace is eligible.
        Assert.NotEqual(WorkspacePolicyReasonCodes.SoloSetupDisabled, eligibility.ReasonCode);
        Assert.Equal(SoloSetupEligibilityStatus.Eligible, eligibility.Status);
    }
}
