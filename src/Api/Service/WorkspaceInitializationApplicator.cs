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
    public sealed class WorkspaceInitializationRequest
    {
        public WorkspacePurpose Purpose { get; init; }
        public WorkspaceCollaborationIntent CollaborationIntent { get; init; }
        public OwnerAccessPreset OwnerAccessPreset { get; init; } = OwnerAccessPreset.FullWorkspaceAccess;
        public WorkspaceApprovalRuleKind? InitialJournalWorkflow { get; init; }
    }

    /// <summary>
    /// The journeys released to the setup audience: Business or freelance work, on your own or with others.
    /// Personal purpose (solo, or household with others) stays out of scope until it is explicitly released:
    /// <see cref="WorkspaceExperienceOptions.PersonalPurposeEnabled"/> AND a finance-approved Individual equity mapping.
    /// </summary>
    public static class WorkspaceInitializationScope
    {
        public const string UnsupportedScopeMessage =
            "The current setup audience supports business or freelance work, on your own or with other people.";

        public static bool IsSupported(WorkspacePurpose purpose, WorkspaceCollaborationIntent intent) =>
            IsSupported(purpose, intent, personalReleased: false);

        public static bool IsSupported(WorkspacePurpose purpose, WorkspaceCollaborationIntent intent, bool personalReleased) =>
            intent is WorkspaceCollaborationIntent.JustMe or WorkspaceCollaborationIntent.WithOthers
            && (purpose == WorkspacePurpose.Business || (purpose == WorkspacePurpose.Personal && personalReleased));

        /// <summary>Personal purpose opens only when both the rollout switch and the finance approval are true.</summary>
        public static bool IsPersonalReleased(WorkspaceExperienceOptions options) =>
            options.PersonalPurposeEnabled && EntityEquityMapping.IsIndividualMappingApproved;
    }

    public sealed class WorkspaceInitializationResult
    {
        public bool Applied { get; init; }
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
        public long PolicyRevision { get; init; }
        public string? AccessProfileKey { get; init; }
        public WorkspaceApprovalMode JournalMode { get; init; }
    }

    /// <summary>
    /// Applies explicit About/setup choices inside an existing company-creation or confirmation transaction.
    /// Caller must already hold the company create/membership transaction; this uses the policy guard.
    /// </summary>
    public sealed class WorkspaceInitializationApplicator
    {
        private readonly ApiDbContext _db;
        private readonly IWorkspacePolicyTransaction _policyTransaction;
        private readonly IWorkspaceAccessProfileCatalog _accessCatalog;
        private readonly IWorkspaceApprovalPolicyResolver _approvalResolver;
        private readonly CompanyAccountingPolicyService _accountingPolicy;
        private readonly WorkspaceExperienceOptions _experience;
        private readonly bool _personalReleasedForDisposableFixtures;

        /// <param name="personalReleasedForDisposableFixtures">
        /// Disposable-fixture seam only; dependency injection never supplies it. Wizard setup uses
        /// <see cref="WorkspaceInitializationScope.IsPersonalReleased"/>; the dedicated personal-books path verifies its own admission.
        /// </param>
        public WorkspaceInitializationApplicator(
            ApiDbContext db,
            IWorkspacePolicyTransaction policyTransaction,
            IWorkspaceAccessProfileCatalog accessCatalog,
            IWorkspaceApprovalPolicyResolver approvalResolver,
            IOptions<WorkspaceExperienceOptions> experienceOptions,
            bool personalReleasedForDisposableFixtures = false,
            CompanyAccountingPolicyService? accountingPolicy = null)
        {
            _personalReleasedForDisposableFixtures = personalReleasedForDisposableFixtures;
            _db = db;
            _policyTransaction = policyTransaction;
            _accessCatalog = accessCatalog;
            _approvalResolver = approvalResolver;
            _accountingPolicy = accountingPolicy ?? new CompanyAccountingPolicyService(db);
            _experience = WorkspaceExperienceOptions.FromEnvironment(experienceOptions.Value);
        }

        public WorkspaceExperienceOptions Experience => _experience;

        public WorkspaceInitializationResult ApplyNewWorkspace(
            int companyId,
            string ownerUserId,
            int ownerMemberId,
            WorkspaceInitializationRequest request,
            bool requireSignupContinuationEvidence = false,
            bool personalBooksSetup = false)
        {
            // Only the dedicated server setup path can admit personal books independently of the wizard rollout.
            var dedicatedPersonalSetup = personalBooksSetup && EntityEquityMapping.IsIndividualMappingApproved
                && request.Purpose == WorkspacePurpose.Personal
                && request.CollaborationIntent == WorkspaceCollaborationIntent.JustMe;
            if (!_experience.SoloSetupEnabled && !dedicatedPersonalSetup)
            {
                if (request.CollaborationIntent == WorkspaceCollaborationIntent.JustMe)
                {
                    return _policyTransaction.Execute(companyId, guard =>
                    {
                        var settings = guard.Settings;
                        if (settings.InitializedAtUtc.HasValue)
                        {
                            return new WorkspaceInitializationResult
                            {
                                Applied = false,
                                ReasonCode = WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                                Message = "Workspace is already initialized.",
                                PolicyRevision = settings.PolicyRevision,
                                JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                            };
                        }

                        return new WorkspaceInitializationResult
                        {
                            Applied = false,
                            ReasonCode = WorkspacePolicyReasonCodes.SoloSetupDisabled,
                            Message = "Solo/team workspace initialization is not enabled; workspace remains unclassified.",
                            JournalMode = WorkspaceApprovalMode.CatalogDefault
                        };
                    });
                }

                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = WorkspacePolicyReasonCodes.SoloSetupDisabled,
                    Message = "Solo/team workspace initialization is not enabled; workspace remains unclassified.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            // Approved-journey scope. Setup releases the Business solo (Just me) and team (With others)
            // journeys. Personal purpose is out of scope until it is released and must not be reachable,
            // regardless of supplied answers or journal workflow.
            var personalReleased = dedicatedPersonalSetup || _personalReleasedForDisposableFixtures
                || WorkspaceInitializationScope.IsPersonalReleased(_experience);
            if (!WorkspaceInitializationScope.IsSupported(request.Purpose, request.CollaborationIntent, personalReleased))
            {
                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = WorkspacePolicyReasonCodes.SetupScopeNotAllowed,
                    Message = WorkspaceInitializationScope.UnsupportedScopeMessage,
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            if (request.Purpose == WorkspacePurpose.Unspecified
                || request.CollaborationIntent == WorkspaceCollaborationIntent.Unspecified)
            {
                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = "about_answers_required",
                    Message = "Purpose and collaboration answers are required for the new workspace experience.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            if (request.Purpose == WorkspacePurpose.Personal && !personalReleased)
            {
                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = WorkspacePolicyReasonCodes.PersonalPurposeDisabled,
                    Message = "Personal workspace purpose is not available until the personal equity mapping is approved.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            // An admitted, in-scope initialization attempt begins here (enrollment, scope, and answer-shape
            // checks have all passed). Count the onboarding START for the new-workspace flow. A start is an
            // attempt, so it is emitted inline regardless of the under-lock eligibility outcome, mirroring the
            // legacy-confirmation flow.
            WorkspaceOperatingPolicyMetrics.OnboardingStarts.Add(1,
                new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace));

            return _policyTransaction.Execute(companyId, guard =>
            {
                var settings = guard.Settings;

                // Durable, deduplicated cohort START at the initialize-attempt boundary. For the signup
                // continuation this is a no-op (signup already recorded the start); for the direct
                // authenticated create-and-initialize path this is where the cohort member enters. Recording
                // it BEFORE the eligibility/commit decision — and separately from completion — means an
                // attempt that is rejected or later abandoned still leaves a start with no completion, so
                // cohort conversion counts dropouts rather than being pinned to 100%.
                WorkspaceOnboardingCohort.RecordStart(_db, companyId, ownerUserId,
                    WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace, boundary: "initialize");

                if (settings.InitializedAtUtc.HasValue)
                {
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Ineligible));
                    return new WorkspaceInitializationResult
                    {
                        Applied = false,
                        ReasonCode = WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                        Message = "Workspace is already initialized.",
                        PolicyRevision = settings.PolicyRevision,
                        JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                    };
                }

                // Durable, server-issued eligibility proof — evaluated under the coordination lock. Being
                // uninitialized is NOT sufficient: a legacy, current-team, former-team, or transferred-owner
                // workspace can also be uninitialized. The workspace must be a pristine, sole-original-owner
                // workspace with no disqualifying membership, invitation, policy-transition,
                // ownership-transfer, or collaboration history. For the authenticated current/initialize
                // CONTINUATION, it must additionally carry the signup-specific pending-continuation marker
                // (generic origin evidence is insufficient — every created workspace has that), so a
                // pristine-but-non-signup workspace cannot be initialized through that endpoint. The
                // synchronous create path, which creates and initializes a fresh workspace in one request,
                // runs the structural proof only.
                var eligibility = requireSignupContinuationEvidence
                    ? ContractAVerifier.VerifyPendingSignupContinuation(_db, companyId, ownerUserId)
                    : ContractAVerifier.VerifySolePristineOwner(_db, companyId, ownerUserId, "initialization");
                if (!eligibility.Allowed)
                {
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Ineligible));
                    return new WorkspaceInitializationResult
                    {
                        Applied = false,
                        ReasonCode = WorkspacePolicyReasonCodes.NotPendingSignupWorkspace,
                        Message = eligibility.Detail,
                        PolicyRevision = settings.PolicyRevision,
                        JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                    };
                }

                // Personal/business isolation: the accounting profile must match the declared purpose. Personal
                // workspaces use the Individual equity mapping; business workspaces never do. When personal is
                // finance-released, an authorized initialization may convert a provisioned non-Individual
                // profile to Individual — but only through the accounting-policy path so PermanentAccountId,
                // display names, and closed-year freezes stay consistent. Never mutate EntityType alone.
                var companySetting = _db.CompanySettings.IgnoreQueryFilters()
                    .SingleOrDefault(s => s.CompanyId == companyId);
                if (request.Purpose == WorkspacePurpose.Personal
                    && WorkspaceInitializationScope.IsPersonalReleased(_experience)
                    && companySetting != null
                    && companySetting.EntityType != EntityType.Individual)
                {
                    var conversion = _accountingPolicy.Apply(
                        companySetting,
                        new AccountingPolicyChange { EntityType = EntityType.Individual },
                        DateTime.UtcNow);
                    if (!conversion.Ok)
                    {
                        return new WorkspaceInitializationResult
                        {
                            Applied = false,
                            ReasonCode = WorkspacePolicyReasonCodes.EntityPurposeMismatch,
                            Message = conversion.ClosedYearsConflict
                                ? "The accounting profile cannot change after a fiscal year has been closed."
                                : conversion.Errors.Count > 0
                                    ? string.Join(" ", conversion.Errors)
                                    : "Personal workspaces use the Individual accounting profile, and business workspaces do not.",
                            PolicyRevision = settings.PolicyRevision,
                            JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                        };
                    }
                }

                var provisionedEntityType = companySetting?.EntityType;
                var entityMatchesPurpose = request.Purpose == WorkspacePurpose.Personal
                    ? provisionedEntityType == EntityType.Individual
                    : provisionedEntityType != EntityType.Individual;
                if (!entityMatchesPurpose)
                {
                    return new WorkspaceInitializationResult
                    {
                        Applied = false,
                        ReasonCode = WorkspacePolicyReasonCodes.EntityPurposeMismatch,
                        Message = "Personal workspaces use the Individual accounting profile, and business workspaces do not.",
                        PolicyRevision = settings.PolicyRevision,
                        JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                    };
                }

                settings.Purpose = request.Purpose;
                settings.CollaborationIntent = request.CollaborationIntent;
                settings.ConfirmedOwnerMemberId = ownerMemberId;
                settings.InitializedAtUtc = DateTime.UtcNow;
                settings.InitializedByUserId = ownerUserId;
                settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
                settings.LastConfirmedAtUtc = settings.InitializedAtUtc;
                settings.LastConfirmedByUserId = ownerUserId;

                string? profileKey = null;
                if (request.CollaborationIntent == WorkspaceCollaborationIntent.JustMe)
                {
                    settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                    AssignFullAccess(ownerMemberId, ownerUserId);
                    profileKey = _accessCatalog.FullAccessProfileKey;
                    SetJournalRule(companyId, WorkspaceApprovalRuleKind.SingleActorAllowed, ownerUserId, settings.PolicyRevision);
                }
                else
                {
                    settings.CollaborationState = WorkspaceCollaborationState.TeamSetupPending;
                    if (request.OwnerAccessPreset == OwnerAccessPreset.FullWorkspaceAccess)
                    {
                        AssignFullAccess(ownerMemberId, ownerUserId);
                        profileKey = _accessCatalog.FullAccessProfileKey;
                    }

                    var journalKind = request.InitialJournalWorkflow ?? WorkspaceApprovalRuleKind.IndependentReviewerRequired;
                    // Independent review only becomes usable once an eligible reviewer accepts; still persist the chosen rule.
                    SetJournalRule(companyId, journalKind, ownerUserId, settings.PolicyRevision);
                }

                var company = _db.Companies.IgnoreQueryFilters().Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;

                var previousRevision = settings.PolicyRevision;
                guard.AdvanceRevision();

                var auditPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    eventKind = "WorkspaceInitialization",
                    companyId,
                    purpose = settings.Purpose.ToString(),
                    collaborationIntent = settings.CollaborationIntent.ToString(),
                    collaborationState = settings.CollaborationState.ToString(),
                    confirmedOwnerMemberId = ownerMemberId,
                    initializedByUserId = ownerUserId,
                    initializationSource = settings.InitializationSource.ToString(),
                    accessProfileKey = profileKey,
                    previousPolicyRevision = previousRevision,
                    policyRevision = settings.PolicyRevision
                });

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = ownerUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                    TableName = "__WorkspaceInitialization",
                    RecordId = companyId.ToString(),
                    NewValue = auditPayload
                });

                // Durable onboarding COMPLETION in the SAME transaction as the initialization, so it exists
                // iff the workspace was actually initialized. The matching START was recorded independently
                // (at signup or at the attempt boundary above), so cohort conversion = completions / starts
                // correctly excludes dropouts. Deduplicated: a repeat initialization cannot add a second
                // completion.
                WorkspaceOnboardingCohort.RecordCompletion(_db, companyId, ownerUserId,
                    WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace,
                    settings.CollaborationState.ToString(), settings.PolicyRevision);

                // Emit the SUCCESS completion counter only after the owning transaction durably commits.
                // Both call paths are covered: the controller-owned external scope (Create) drains on
                // policyScope.Committed(), and the applicator-owned Execute (current/initialize) drains inline
                // after its own commit. A later rollback on either path discards this without emitting — so a
                // rolled-back or retried initialization never over-counts a completion.
                _policyTransaction.EnqueuePostCommit(() =>
                    WorkspaceOperatingPolicyMetrics.OnboardingCompletions.Add(1,
                        new KeyValuePair<string, object?>("flow", WorkspaceOperatingPolicyMetrics.FlowLabels.NewWorkspace),
                        new KeyValuePair<string, object?>("status", WorkspaceOperatingPolicyMetrics.StatusLabels.Success)));

                var journal = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost);
                return new WorkspaceInitializationResult
                {
                    Applied = true,
                    PolicyRevision = settings.PolicyRevision,
                    AccessProfileKey = profileKey,
                    JournalMode = journal.Mode
                };
            });
        }

        private void AssignFullAccess(int ownerMemberId, string grantedByUserId)
        {
            _accessCatalog.EnsureSeeded();
            var role = _accessCatalog.FindFullAccessRole()
                ?? throw new InvalidOperationException("Managed Full workspace access profile is not seeded.");

            var existing = _db.CompanyMemberSecurityRoles
                .IgnoreQueryFilters()
                .FirstOrDefault(r => r.CompanyMemberId == ownerMemberId && r.SecurityRoleId == role.Id);
            if (existing != null)
                return;

            _db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
            {
                CompanyMemberId = ownerMemberId,
                SecurityRoleId = role.Id,
                GrantOrigin = WorkspaceMemberGrantOrigin.SoloInitialization,
                GrantedAtUtc = DateTime.UtcNow,
                GrantedByUserId = grantedByUserId,
                AccessProfileVersion = _accessCatalog.FullAccessProfileVersion
            });
        }

        private WorkspaceApprovalRule? FindJournalRule(int companyId) =>
            _db.WorkspaceApprovalRules.IgnoreQueryFilters()
                .SingleOrDefault(r => r.CompanyId == companyId && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);

        private void SetJournalRule(int companyId, WorkspaceApprovalRuleKind kind, string userId, long revision)
        {
            var rule = FindJournalRule(companyId);
            if (rule == null)
            {
                rule = new WorkspaceApprovalRule
                {
                    CompanyId = companyId,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost
                };
                _db.WorkspaceApprovalRules.Add(rule);
            }

            rule.RuleKind = kind;
            rule.Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed;
            rule.ConfiguredAtUtc = DateTime.UtcNow;
            rule.ConfiguredByUserId = userId;
            rule.ConfiguredAtPolicyRevision = revision;
            rule.PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion;
        }
    }
}
