// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Api.Options;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Service
{
    public sealed class WorkspaceOperatingPolicyService : IWorkspaceOperatingPolicyService
    {
        private readonly ApiDbContext _db;
        private readonly IWorkspaceApprovalPolicyResolver _approvalResolver;
        private readonly IWorkspaceAccessProfileCatalog _accessCatalog;
        private readonly IWorkspacePolicyTransaction _policyTransaction;
        private readonly WorkspaceExperienceOptions _experience;
        private readonly IWorkspaceCanaryEnrollment _canary;

        public WorkspaceOperatingPolicyService(
            ApiDbContext db,
            IWorkspaceApprovalPolicyResolver approvalResolver,
            IWorkspaceAccessProfileCatalog accessCatalog,
            IWorkspacePolicyTransaction policyTransaction,
            IOptions<WorkspaceExperienceOptions> experienceOptions,
            IWorkspaceCanaryEnrollment? canary = null)
        {
            _db = db;
            _approvalResolver = approvalResolver;
            _accessCatalog = accessCatalog;
            _policyTransaction = policyTransaction;
            _experience = WorkspaceExperienceOptions.FromEnvironment(experienceOptions.Value);
            // The canary admission gate must match what new-workspace initialization enforces. Fall back to
            // one built from the same (environment-resolved) experience options when DI does not supply it,
            // so the capability always reflects SoloSetupEnabled AND canary enrollment.
            _canary = canary ?? new WorkspaceCanaryEnrollment(experienceOptions);
        }

        public WorkspaceOperatingSettings EnsureCoordinationAndSettings(int companyId) =>
            _policyTransaction.Execute(companyId, guard => guard.Settings);

        public WorkspaceOperatingSummary GetSummary(int companyId, int? memberId = null, string? callerUserId = null)
        {
            var settings = _db.WorkspaceOperatingSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefault(s => s.CompanyId == companyId)
                ?? new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                    PolicyRevision = 0
                };

            var journalRule = _approvalResolver.Resolve(companyId, AccessOperationCatalog.FinancialsJournalPost);

            string? profileKey = null;
            string? profileDisplay = null;
            if (memberId.HasValue)
            {
                var fullAccessRoleId = _accessCatalog.FindFullAccessRole()?.Id;
                if (fullAccessRoleId.HasValue)
                {
                    var hasProfile = _db.CompanyMemberSecurityRoles
                        .IgnoreQueryFilters()
                        .Any(r => r.CompanyMemberId == memberId.Value && r.SecurityRoleId == fullAccessRoleId.Value);
                    if (hasProfile)
                    {
                        profileKey = _accessCatalog.FullAccessProfileKey;
                        profileDisplay = _accessCatalog.FullAccessDisplayName;
                    }
                }
            }

            var nextActions = new List<string>();
            if (settings.CollaborationState == WorkspaceCollaborationState.LegacyUnclassified
                && _experience.LegacySoloConfirmationEnabled)
                nextActions.Add("evaluate_solo_setup");
            if (settings.CollaborationState == WorkspaceCollaborationState.SoloActive)
                nextActions.Add("invite_someone");
            if (settings.CollaborationState == WorkspaceCollaborationState.TeamSetupPending)
                nextActions.Add("invite_collaborator");

            if (_experience.LegacySoloConfirmationEnabled && settings.CollaborationState != WorkspaceCollaborationState.SoloActive)
            {
                bool canReturn = false;
                if (memberId.HasValue)
                {
                    var callerMember = _db.CompanyMembers.IgnoreQueryFilters()
                        .FirstOrDefault(m => m.Id == memberId.Value && m.CompanyId == companyId);
                    if (callerMember != null && callerMember.Role == CompanyMemberRole.Owner && callerMember.Status == CompanyMemberStatus.Active)
                    {
                        var otherActiveHumans = _db.CompanyMembers.IgnoreQueryFilters()
                            .Any(m => m.CompanyId == companyId
                                      && m.Status == CompanyMemberStatus.Active
                                      && m.Id != callerMember.Id);
                        canReturn = !otherActiveHumans;
                    }
                }
                else
                {
                    var activeMembers = _db.CompanyMembers.IgnoreQueryFilters()
                        .Where(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active)
                        .ToList();
                    canReturn = activeMembers.Count == 1 && activeMembers[0].Role == CompanyMemberRole.Owner;
                }

                if (canReturn)
                {
                    nextActions.Add("return_to_solo");
                }
            }

            // Server-derived pending-signup-continuation capability. It mirrors EXACTLY what
            // current/initialize requires, so the SPA never routes a caller into a continuation the server
            // would reject:
            //   1. The admission gates — SoloSetupEnabled AND canary enrollment for this owner — evaluated
            //      by the same IsNewBusinessSoloAudienceAdmitted decision the endpoint uses. With setup
            //      disabled or the owner unenrolled the flow is unavailable, so the flag is false even
            //      though the workspace is structurally eligible.
            //   2. The signup marker + pristine/uninitialized/sole-owner structural proof.
            // It is meaningful only for the original owner (the marker is bound to that user id), so it is
            // false whenever the caller is unknown or is any other member. The initialization-time checks
            // are retained as the authoritative gate; this flag is purely advisory for routing.
            var pendingSignupContinuation = !string.IsNullOrWhiteSpace(callerUserId)
                && _canary.IsNewBusinessSoloAudienceAdmitted(email: null, userId: callerUserId)
                && ContractAVerifier.VerifyPendingSignupContinuation(_db, companyId, callerUserId!).Allowed;

            return new WorkspaceOperatingSummary(
                settings.Purpose,
                settings.CollaborationIntent,
                settings.CollaborationState,
                settings.PolicyRevision,
                journalRule,
                profileKey,
                profileDisplay,
                _experience.SoloSetupEnabled,
                nextActions,
                pendingSignupContinuation);
        }

        public SoloSetupEligibility EvaluateLegacySoloEligibility(int companyId, string callerUserId)
        {
            WorkspaceOperatingPolicyMetrics.OnboardingStarts.Add(1,
                new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo));

            if (!_experience.LegacySoloConfirmationEnabled)
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupDisabled,
                    "Solo workspace setup is not enabled.",
                    0);
            }

            return _policyTransaction.Execute(companyId, guard => EvaluateLegacySoloEligibility(guard, callerUserId));
        }

        public SoloSetupEligibility EvaluateLegacySoloEligibility(WorkspacePolicyGuard guard, string callerUserId)
        {
            if (!_experience.LegacySoloConfirmationEnabled)
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupDisabled,
                    "Solo workspace setup is not enabled.",
                    guard.PolicyRevision);
            }

            var companyId = guard.CompanyId;
            var settings = guard.Settings;
            if (settings.InitializedAtUtc.HasValue
                || settings.CollaborationState is WorkspaceCollaborationState.SoloActive
                    or WorkspaceCollaborationState.TeamActive
                    or WorkspaceCollaborationState.TeamSetupPending)
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                    "This workspace is already initialized.",
                    settings.PolicyRevision);
            }

            if (settings.EverTeamActivatedAtUtc.HasValue)
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                    "This workspace has historical collaboration evidence and cannot use automatic solo setup.",
                    settings.PolicyRevision);
            }

            var members = _db.CompanyMembers
                .IgnoreQueryFilters()
                .Where(m => m.CompanyId == companyId)
                .ToList();

            var owners = members.Where(m => m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active).ToList();
            if (owners.Count != 1 || !string.Equals(owners[0].UserId, callerUserId, StringComparison.Ordinal))
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                    "Only the sole active Owner can confirm solo setup.",
                    settings.PolicyRevision);
            }

            if (members.Any(m => m.Id != owners[0].Id))
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                    "Another membership exists. Member count alone cannot restore solo setup.",
                    settings.PolicyRevision);
            }

            var acceptedInvite = _db.CompanyInvitations
                .IgnoreQueryFilters()
                .Any(i => i.CompanyId == companyId && i.AcceptedAt != null);
            if (acceptedInvite)
            {
                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.Ineligible,
                    WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                    "An accepted invitation is historical collaboration evidence.",
                    settings.PolicyRevision);
            }

            var company = _db.Companies.IgnoreQueryFilters().Single(c => c.Id == companyId);
            if (string.IsNullOrWhiteSpace(company.CreatedByUserId)
                || !string.Equals(company.CreatedByUserId, callerUserId, StringComparison.Ordinal)
                || owners[0].InvitedByUserId != null)
            {
                // Ambiguous ownership / transfer history — require controlled recovery.
                if (!_experience.ControlledRecoveryEnabled)
                {
                    return new SoloSetupEligibility(
                        SoloSetupEligibilityStatus.RecoveryRequired,
                        WorkspacePolicyReasonCodes.RecoveryUnavailable,
                        "History is ambiguous and controlled recovery is not available yet.",
                        settings.PolicyRevision);
                }

                return new SoloSetupEligibility(
                    SoloSetupEligibilityStatus.RecoveryRequired,
                    WorkspacePolicyReasonCodes.RecoveryRequired,
                    "History is ambiguous and requires controlled recovery.",
                    settings.PolicyRevision);
            }

            return new SoloSetupEligibility(
                SoloSetupEligibilityStatus.Eligible,
                "eligible",
                "This workspace appears to have always been solo.",
                settings.PolicyRevision);
        }

        public WorkspacePolicyMutationResult ConfirmLegacySoloSetup(int companyId, string callerUserId, long expectedRevision)
        {
            return _policyTransaction.Execute(companyId, guard =>
            {
                var settings = guard.Settings;
                if (settings.PolicyRevision != expectedRevision)
                {
                    WorkspaceOperatingPolicyMetrics.Conflicts.Add(1,
                        new KeyValuePair<string, object?>("conflict_type", WorkspaceOperatingPolicyMetrics.ConflictLabels.StaleRevision));
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Conflict));

                    return new WorkspacePolicyMutationResult(
                        false,
                        WorkspacePolicyReasonCodes.StalePolicyRevision,
                        "The workspace setup changed. Review the current state and confirm again.",
                        settings.PolicyRevision,
                        settings.CollaborationState);
                }

                var eligibility = EvaluateLegacySoloEligibility(guard, callerUserId);
                if (eligibility.Status == SoloSetupEligibilityStatus.RecoveryRequired)
                {
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Ineligible));

                    return new WorkspacePolicyMutationResult(
                        false,
                        _experience.ControlledRecoveryEnabled
                            ? WorkspacePolicyReasonCodes.RecoveryRequired
                            : WorkspacePolicyReasonCodes.RecoveryUnavailable,
                        _experience.ControlledRecoveryEnabled
                            ? eligibility.Message
                            : "Controlled recovery is not available yet.",
                        eligibility.PolicyRevision,
                        WorkspaceCollaborationState.LegacyUnclassified);
                }

                if (eligibility.Status != SoloSetupEligibilityStatus.Eligible)
                {
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Ineligible));

                    return new WorkspacePolicyMutationResult(
                        false,
                        eligibility.ReasonCode,
                        eligibility.Message,
                        eligibility.PolicyRevision,
                        WorkspaceCollaborationState.LegacyUnclassified);
                }

                var owner = _db.CompanyMembers.IgnoreQueryFilters()
                    .Single(m => m.CompanyId == companyId && m.UserId == callerUserId
                                 && m.Role == CompanyMemberRole.Owner
                                 && m.Status == CompanyMemberStatus.Active);

                settings.Purpose = settings.Purpose == WorkspacePurpose.Unspecified
                    ? WorkspacePurpose.Business
                    : settings.Purpose;
                settings.CollaborationIntent = WorkspaceCollaborationIntent.JustMe;
                settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                settings.ConfirmedOwnerMemberId = owner.Id;
                settings.InitializedAtUtc = DateTime.UtcNow;
                settings.InitializedByUserId = callerUserId;
                settings.InitializationSource = WorkspaceInitializationSource.VerifiedLegacySetup;
                settings.LastConfirmedAtUtc = settings.InitializedAtUtc;
                settings.LastConfirmedByUserId = callerUserId;

                AssignFullAccessIfMissing(owner.Id, callerUserId);
                EnsureSingleActorJournalRule(companyId, callerUserId, settings.PolicyRevision);

                var company = _db.Companies.IgnoreQueryFilters().Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = callerUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                    TableName = "__WorkspaceInitialization",
                    RecordId = companyId.ToString(),
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        eventKind = "LegacySoloConfirmation",
                        companyId,
                        confirmedOwnerMemberId = owner.Id,
                        confirmedByUserId = callerUserId,
                        policyRevision = settings.PolicyRevision
                    })
                });

                guard.AdvanceRevision();

                WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                    new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo),
                    new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Success));

                return new WorkspacePolicyMutationResult(
                    true,
                    null,
                    "Solo setup confirmed.",
                    settings.PolicyRevision,
                    settings.CollaborationState);
            });
        }

        public WorkspacePolicyMutationResult ReturnToSolo(
            int companyId,
            string callerUserId,
            long expectedRevision,
            bool revokeOutstandingInvitations)
        {
            if (!_experience.LegacySoloConfirmationEnabled)
            {
                return new WorkspacePolicyMutationResult(
                    false,
                    WorkspacePolicyReasonCodes.SoloSetupDisabled,
                    "Solo workspace setup is not enabled.",
                    0,
                    WorkspaceCollaborationState.LegacyUnclassified);
            }

            return _policyTransaction.Execute(companyId, guard =>
            {
                var settings = guard.Settings;
                if (settings.PolicyRevision != expectedRevision)
                {
                    return new WorkspacePolicyMutationResult(
                        false,
                        WorkspacePolicyReasonCodes.StalePolicyRevision,
                        "The workspace setup changed. Review the current state and confirm again.",
                        settings.PolicyRevision,
                        settings.CollaborationState);
                }

                var owner = _db.CompanyMembers.IgnoreQueryFilters()
                    .FirstOrDefault(m => m.CompanyId == companyId && m.UserId == callerUserId
                                         && m.Role == CompanyMemberRole.Owner
                                         && m.Status == CompanyMemberStatus.Active);
                if (owner == null)
                {
                    return new WorkspacePolicyMutationResult(
                        false,
                        WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                        "Only the active Owner can return this workspace to just me.",
                        settings.PolicyRevision,
                        settings.CollaborationState);
                }

                var otherActiveHumans = _db.CompanyMembers.IgnoreQueryFilters()
                    .Any(m => m.CompanyId == companyId
                              && m.Status == CompanyMemberStatus.Active
                              && m.Id != owner.Id);
                if (otherActiveHumans)
                {
                    return new WorkspacePolicyMutationResult(
                        false,
                        WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                        "Remove or suspend other active members before returning to just me.",
                        settings.PolicyRevision,
                        settings.CollaborationState);
                }

                var pendingInvites = _db.CompanyInvitations.IgnoreQueryFilters()
                    .Where(i => i.CompanyId == companyId
                                && i.AcceptedAt == null
                                && i.RevokedAt == null
                                && i.ExpiresAt > DateTime.UtcNow)
                    .ToList();
                if (pendingInvites.Count > 0 && !revokeOutstandingInvitations)
                {
                    return new WorkspacePolicyMutationResult(
                        false,
                        WorkspacePolicyReasonCodes.ApprovalPolicyChangeRequired,
                        "Outstanding invitations must be revoked or reconfirmed before returning to just me.",
                        settings.PolicyRevision,
                        settings.CollaborationState);
                }

                if (revokeOutstandingInvitations)
                {
                    foreach (var invite in pendingInvites)
                        invite.RevokedAt = DateTime.UtcNow;
                }

                // Preserve grants and approval rules; never recreate an initial solo grant.
                var previousState = settings.CollaborationState;
                var previousRevision = settings.PolicyRevision;
                settings.CollaborationIntent = WorkspaceCollaborationIntent.JustMe;
                settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                settings.ConfirmedOwnerMemberId = owner.Id;
                settings.LastConfirmedAtUtc = DateTime.UtcNow;
                settings.LastConfirmedByUserId = callerUserId;
                guard.AdvanceRevision();

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = callerUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Modified,
                    TableName = "__WorkspacePolicyTransition",
                    RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FieldName = "CollaborationState",
                    OriginalValue = previousState.ToString(),
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        eventKind = "ReturnToSolo",
                        companyId,
                        actorUserId = callerUserId,
                        previousState = previousState.ToString(),
                        newState = settings.CollaborationState.ToString(),
                        previousPolicyRevision = previousRevision,
                        policyRevision = settings.PolicyRevision,
                        revokedInvitations = revokeOutstandingInvitations ? pendingInvites.Count : 0
                    })
                });

                return new WorkspacePolicyMutationResult(
                    true,
                    null,
                    "Workspace is set up for just you. Existing access and approval rules were preserved.",
                    settings.PolicyRevision,
                    settings.CollaborationState);
            });
        }

        public void UpdateConfirmedOwnerMember(int companyId, int newOwnerMemberId, string actorUserId)
        {
            _policyTransaction.Execute(companyId, guard =>
            {
                var settings = guard.Settings;
                var member = _db.CompanyMembers.IgnoreQueryFilters()
                    .SingleOrDefault(m => m.Id == newOwnerMemberId && m.CompanyId == companyId);
                if (member == null)
                    throw new InvalidOperationException("Confirmed owner must belong to the same company.");

                var oldOwnerId = settings.ConfirmedOwnerMemberId;
                var previousRevision = settings.PolicyRevision;
                settings.ConfirmedOwnerMemberId = newOwnerMemberId;
                settings.LastConfirmedAtUtc = DateTime.UtcNow;
                settings.LastConfirmedByUserId = actorUserId;
                // Ownership transfer does not grant Full workspace access automatically.
                guard.AdvanceRevision();

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = actorUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Modified,
                    TableName = "__WorkspacePolicyTransition",
                    RecordId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FieldName = "ConfirmedOwnerMemberId",
                    OriginalValue = oldOwnerId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        eventKind = "ConfirmedOwnerChanged",
                        companyId,
                        actorUserId,
                        previousOwnerMemberId = oldOwnerId,
                        newOwnerMemberId,
                        previousPolicyRevision = previousRevision,
                        policyRevision = settings.PolicyRevision
                    })
                });
            });
        }

        private void AssignFullAccessIfMissing(int memberId, string grantedByUserId)
        {
            _accessCatalog.EnsureSeeded();
            var role = _accessCatalog.FindFullAccessRole()
                ?? throw new InvalidOperationException("Managed Full workspace access profile is not seeded.");
            if (_db.CompanyMemberSecurityRoles.IgnoreQueryFilters()
                .Any(r => r.CompanyMemberId == memberId && r.SecurityRoleId == role.Id))
                return;

            _db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
            {
                CompanyMemberId = memberId,
                SecurityRoleId = role.Id,
                GrantOrigin = WorkspaceMemberGrantOrigin.SoloInitialization,
                GrantedAtUtc = DateTime.UtcNow,
                GrantedByUserId = grantedByUserId,
                AccessProfileVersion = _accessCatalog.FullAccessProfileVersion
            });
        }

        private void EnsureSingleActorJournalRule(int companyId, string userId, long revision)
        {
            var rule = _db.WorkspaceApprovalRules.IgnoreQueryFilters()
                .SingleOrDefault(r => r.CompanyId == companyId
                                      && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
            if (rule == null)
            {
                rule = new WorkspaceApprovalRule
                {
                    CompanyId = companyId,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost
                };
                _db.WorkspaceApprovalRules.Add(rule);
            }

            // Never overwrite an established independent-review rule by inference during legacy confirm.
            if (rule.Provenance == WorkspaceApprovalRuleProvenance.ExplicitPolicyChange
                && rule.RuleKind == WorkspaceApprovalRuleKind.IndependentReviewerRequired)
                return;

            if (rule.Provenance == WorkspaceApprovalRuleProvenance.LegacyEnforced
                || rule.RuleKind != WorkspaceApprovalRuleKind.SingleActorAllowed)
            {
                rule.RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed;
                rule.Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed;
                rule.ConfiguredAtUtc = DateTime.UtcNow;
                rule.ConfiguredByUserId = userId;
                rule.ConfiguredAtPolicyRevision = revision;
                rule.PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion;
            }
        }
    }
}
