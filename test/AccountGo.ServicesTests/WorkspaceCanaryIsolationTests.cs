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

            // Durable, server-issued creation evidence exactly as WorkspaceService.Create writes it: the
            // immutable origin row bound to the creating owner plus the initial MemberAdded event. The
            // initialization eligibility proof requires this, so the seed must mirror it to be faithful.
            WorkspaceOriginEvidence.Record(_db, company.Id, ownerUserId);
            WorkspaceCollaborationEvidence.Record(_db, company.Id, WorkspaceCollaborationEvidence.EventMemberAdded, ownerUserId, ownerUserId, new { role = CompanyMemberRole.Owner.ToString() });
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
    public void ApplyNewWorkspace_EnrolledOwner_TeamWorkspace_IsRejected_NotPendingSignup()
    {
        // An enrolled owner of an uninitialized workspace that has already become a TEAM (a second active
        // member and a historical team-activation marker) must NOT be initializable through the
        // continuation path. Being uninitialized is not sufficient; the eligibility proof runs in the
        // service, not only the controller.
        const string ownerId = "owner-team-workspace";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            _db.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = company.Id,
                UserId = "second-member",
                Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            });
            var seeded = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
            seeded.EverTeamActivatedAtUtc = DateTime.UtcNow;
            _db.SaveChanges();
        }

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledUserIds: new[] { ownerId });

        var stateBefore = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id).CollaborationState;

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

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.NotPendingSignupWorkspace, result.ReasonCode);

        // Nothing granted; state, initialization, and rule unchanged by the rejected attempt.
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(stateBefore, settings.CollaborationState);
        Assert.NotEqual(WorkspaceCollaborationState.SoloActive, settings.CollaborationState);
        Assert.Null(settings.InitializedAtUtc);
        Assert.Null(settings.ConfirmedOwnerMemberId);
        var teamRule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, teamRule.RuleKind);
        Assert.Empty(_db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Where(r => r.CompanyMember.CompanyId == company.Id));
    }

    [Fact]
    public void ApplyNewWorkspace_EmailOnlyEnrollment_IsNotSufficient_AtInitialization()
    {
        // The initialization boundary binds enrollment to the owner's authoritative user id, not to a
        // request-supplied email. A workspace whose owner id is not enrolled must be rejected even if some
        // email happens to be enrolled — the applicator must not trust a caller-supplied address.
        const string ownerId = "owner-not-enrolled-by-id";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledEmails: new[] { "owner@freelancer.example" });

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

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.CanaryNotEnrolled, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
    }

    [Fact]
    public void ApplyNewWorkspace_EnrolledOwner_TeamIntent_IsRejected_OutOfScope()
    {
        // Enrolled, but requesting team (With-others) setup: out of the approved canary scope.
        const string ownerId = "owner-team";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledUserIds: new[] { ownerId });

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.WithOthers,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess,
                InitialJournalWorkflow = WorkspaceApprovalRuleKind.SingleActorAllowed
            });
        }

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.CanaryScopeNotAllowed, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        // The seeded independent-review rule is untouched; no single-actor grant leaked in.
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
    }

    [Fact]
    public void ApplyNewWorkspace_EnrolledOwner_PersonalPurpose_IsRejected_OutOfScope()
    {
        const string ownerId = "owner-personal";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true, enrolledUserIds: new[] { ownerId });

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Personal,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            });
        }

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.CanaryScopeNotAllowed, result.ReasonCode);
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
