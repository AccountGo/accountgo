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
/// New-workspace setup scope. Verifies that initialization is re-enforced at the initializer (so a direct
/// caller cannot bypass the controller checks), that only the supported purpose/collaboration journeys
/// initialize, and that legacy confirmation stays independently closed when only the new-setup switch is enabled.
/// </summary>
public sealed class WorkspaceSetupScopeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();

    public WorkspaceSetupScopeTests()
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
    private (Company company, CompanyMember owner) SeedUninitializedCompany(string ownerUserId, bool writeSignupContinuationMarker = true)
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
            // Signup-specific pending-continuation marker: only the signup flow writes this, and the
            // continuation eligibility proof requires it. Seed it so this stands in for a signup workspace.
            if (writeSignupContinuationMarker)
                WorkspaceSignupContinuationEvidence.Record(_db, company.Id, ownerUserId);
            _db.SaveChanges();
        }
        return (company, owner);
    }

    // ---- Initialization re-enforcement (direct-call bypass protection) ------------------------

    [Fact]
    public void ApplyNewWorkspace_Owner_InitializesSoloWithSingleActorRule()
    {
        const string ownerId = "owner-1";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

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
    public void ApplyNewWorkspace_Owner_TeamWorkspace_IsRejected_NotPendingSignup()
    {
        // The owner of an uninitialized workspace that has already become a TEAM (a second active
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
            _db, _tenant, soloSetupEnabled: true);

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
    public void ApplyNewWorkspace_Continuation_WithoutSignupMarker_IsRejected()
    {
        // A pristine, sole-owner, uninitialized workspace that has generic origin evidence but NOT the
        // signup pending-continuation marker must be rejected by the CONTINUATION path. Generic creation
        // evidence does not prove the workspace is awaiting the approved signup continuation.
        const string ownerId = "owner-no-marker";
        var (company, owner) = SeedUninitializedCompany(ownerId, writeSignupContinuationMarker: false);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            }, requireSignupContinuationEvidence: true);
        }

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.NotPendingSignupWorkspace, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
        Assert.Null(settings.InitializedAtUtc);
        Assert.Empty(_db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Where(r => r.CompanyMember.CompanyId == company.Id));
    }

    [Fact]
    public void ApplyNewWorkspace_SynchronousCreate_WithoutSignupMarker_Succeeds()
    {
        // The synchronous create path (requireSignupContinuationEvidence: false) creates and initializes a
        // fresh workspace in one request. It must NOT require the signup marker — only the pre-existing
        // workspace continuation does. The structural pristine proof still applies.
        const string ownerId = "owner-sync-create";
        var (company, owner) = SeedUninitializedCompany(ownerId, writeSignupContinuationMarker: false);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            }); // requireSignupContinuationEvidence defaults to false
        }

        Assert.True(result.Applied, $"sync create rejected: {result.ReasonCode} :: {result.Message}");
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.SoloActive, settings.CollaborationState);
        Assert.NotNull(settings.InitializedAtUtc);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, rule.RuleKind);
    }

    [Fact]
    public void ApplyNewWorkspace_Owner_TeamIntent_IsAdmitted_AsTeamSetupPending()
    {
        // Business + With-others: admitted as the team journey. Independent review is the default
        // and the owner keeps the managed Full access profile.
        const string ownerId = "owner-team";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.WithOthers,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            });
        }

        Assert.True(result.Applied, result.Message);
        Assert.Equal(WorkspaceApprovalMode.IndependentReviewerRequired, result.JournalMode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, settings.CollaborationState);
        Assert.Equal(WorkspaceCollaborationIntent.WithOthers, settings.CollaborationIntent);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
        Assert.NotNull(result.AccessProfileKey);
    }

    [Fact]
    public void ApplyNewWorkspace_Owner_PersonalTeam_IsRejected_OutOfScope()
    {
        const string ownerId = "owner-personal-team";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

        WorkspaceInitializationResult result;
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            result = applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Personal,
                CollaborationIntent = WorkspaceCollaborationIntent.WithOthers,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            });
        }

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.SetupScopeNotAllowed, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
    }

    [Fact]
    public void ApplyNewWorkspace_Owner_PersonalPurpose_IsRejected_OutOfScope()
    {
        const string ownerId = "owner-personal";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);

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
        Assert.Equal(WorkspacePolicyReasonCodes.SetupScopeNotAllowed, result.ReasonCode);
    }

    [Fact]
    public void ApplyNewWorkspace_SetupDisabled_ReportsSoloSetupDisabled()
    {
        const string ownerId = "owner-x";
        var (company, owner) = SeedUninitializedCompany(ownerId);

        // Setup off: the disabled reason is reported, and nothing initializes.
        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: false);

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

    // ---- Personal purpose: PROPOSED and gated. The fixture seam stands in for "approved"; production stays closed.

    private void SeedCompanySetting(Company company, EntityType entityType)
    {
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            _db.CompanySettings.Add(new CompanySetting { CompanyId = company.Id, EntityType = entityType });
            _db.SaveChanges();
        }
    }

    private WorkspaceInitializationResult ApplyPersonal(
        string ownerId,
        Company company,
        CompanyMember owner,
        WorkspaceCollaborationIntent intent,
        bool personalReleasedForDisposableFixtures)
    {
        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true,
            personalReleasedForDisposableFixtures: personalReleasedForDisposableFixtures);
        using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
        {
            return applicator.ApplyNewWorkspace(company.Id, ownerId, owner.Id, new WorkspaceInitializationRequest
            {
                Purpose = WorkspacePurpose.Personal,
                CollaborationIntent = intent,
                OwnerAccessPreset = OwnerAccessPreset.FullWorkspaceAccess
            });
        }
    }

    [Fact]
    public void PersonalScope_requires_both_rollout_switch_and_finance_approval()
    {
        Assert.True(EntityEquityMapping.IsIndividualMappingApproved);
        Assert.True(Api.Options.WorkspaceExperienceOptions.FromEnvironment(
            new Api.Options.WorkspaceExperienceOptions { PersonalPurposeEnabled = true }).PersonalPurposeEnabled);
        Assert.True(WorkspaceInitializationScope.IsPersonalReleased(
            new Api.Options.WorkspaceExperienceOptions { PersonalPurposeEnabled = true }));
        Assert.False(WorkspaceInitializationScope.IsPersonalReleased(
            new Api.Options.WorkspaceExperienceOptions { PersonalPurposeEnabled = false }));

        var previous = EntityEquityMapping.TestApprovalOverride;
        try
        {
            EntityEquityMapping.TestApprovalOverride = false;
            Assert.False(Api.Options.WorkspaceExperienceOptions.FromEnvironment(
                new Api.Options.WorkspaceExperienceOptions { PersonalPurposeEnabled = true }).PersonalPurposeEnabled);
        }
        finally
        {
            EntityEquityMapping.TestApprovalOverride = previous;
        }

        Assert.False(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Personal, WorkspaceCollaborationIntent.JustMe));
        Assert.False(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Personal, WorkspaceCollaborationIntent.WithOthers));
        Assert.False(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Personal, WorkspaceCollaborationIntent.JustMe, personalReleased: false));
        Assert.True(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Personal, WorkspaceCollaborationIntent.JustMe, personalReleased: true));
        Assert.True(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Personal, WorkspaceCollaborationIntent.WithOthers, personalReleased: true));
        Assert.False(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Unspecified, WorkspaceCollaborationIntent.JustMe, personalReleased: true));
        Assert.True(WorkspaceInitializationScope.IsSupported(WorkspacePurpose.Business, WorkspaceCollaborationIntent.JustMe));
    }

    [Fact]
    public void Personal_solo_initializes_behind_the_released_gate_with_single_actor_journal_rule()
    {
        const string ownerId = "owner-personal-solo-released";
        var (company, owner) = SeedUninitializedCompany(ownerId);
        SeedCompanySetting(company, EntityType.Individual);

        var result = ApplyPersonal(ownerId, company, owner, WorkspaceCollaborationIntent.JustMe, personalReleasedForDisposableFixtures: true);

        Assert.True(result.Applied, result.Message);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspacePurpose.Personal, settings.Purpose);
        Assert.Equal(WorkspaceCollaborationState.SoloActive, settings.CollaborationState);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.SingleActorAllowed, rule.RuleKind);
    }

    [Fact]
    public void Personal_household_reuses_team_controls_when_the_gate_is_released()
    {
        const string ownerId = "owner-personal-household-released";
        var (company, owner) = SeedUninitializedCompany(ownerId);
        SeedCompanySetting(company, EntityType.Individual);

        var result = ApplyPersonal(ownerId, company, owner, WorkspaceCollaborationIntent.WithOthers, personalReleasedForDisposableFixtures: true);

        Assert.True(result.Applied, result.Message);
        Assert.Equal(WorkspaceApprovalMode.IndependentReviewerRequired, result.JournalMode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspacePurpose.Personal, settings.Purpose);
        Assert.Equal(WorkspaceCollaborationIntent.WithOthers, settings.CollaborationIntent);
        Assert.Equal(WorkspaceCollaborationState.TeamSetupPending, settings.CollaborationState);
        var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters().Single(r => r.CompanyId == company.Id);
        Assert.Equal(WorkspaceApprovalRuleKind.IndependentReviewerRequired, rule.RuleKind);
    }

    [Fact]
    public void Personal_purpose_is_rejected_when_the_accounting_profile_is_not_individual()
    {
        const string ownerId = "owner-personal-entity-mismatch";
        var (company, owner) = SeedUninitializedCompany(ownerId);
        SeedCompanySetting(company, EntityType.SoleProprietor);

        var result = ApplyPersonal(ownerId, company, owner, WorkspaceCollaborationIntent.JustMe, personalReleasedForDisposableFixtures: true);

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.EntityPurposeMismatch, result.ReasonCode);
        var settings = _db.WorkspaceOperatingSettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(WorkspaceCollaborationState.LegacyUnclassified, settings.CollaborationState);
        Assert.Equal(WorkspacePurpose.Unspecified, settings.Purpose);
    }

    [Fact]
    public void Business_purpose_is_rejected_when_the_accounting_profile_is_individual()
    {
        const string ownerId = "owner-business-individual-profile";
        var (company, owner) = SeedUninitializedCompany(ownerId);
        SeedCompanySetting(company, EntityType.Individual);

        var applicator = WorkspaceOperatingPolicyTestSupport.CreateInitializer(
            _db, _tenant, soloSetupEnabled: true);
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
        Assert.Equal(WorkspacePolicyReasonCodes.EntityPurposeMismatch, result.ReasonCode);
    }

    [Fact]
    public void Personal_purpose_stays_closed_without_the_fixture_seam_even_with_individual_profile()
    {
        const string ownerId = "owner-personal-closed";
        var (company, owner) = SeedUninitializedCompany(ownerId);
        SeedCompanySetting(company, EntityType.Individual);

        var result = ApplyPersonal(ownerId, company, owner, WorkspaceCollaborationIntent.JustMe, personalReleasedForDisposableFixtures: false);

        Assert.False(result.Applied);
        Assert.Equal(WorkspacePolicyReasonCodes.SetupScopeNotAllowed, result.ReasonCode);
    }
}

