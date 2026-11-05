// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;

namespace Core.Authorization
{
    public static class WorkspacePolicyReasonCodes
    {
        public const string WorkspacePolicyConflict = "workspace_policy_conflict";
        public const string SoloSetupNotEligible = "solo_setup_not_eligible";
        public const string ReviewerRequired = "reviewer_required";
        public const string ApprovalPolicyChangeRequired = "approval_policy_change_required";
        public const string SoloSetupDisabled = "solo_setup_disabled";
        public const string SetupScopeNotAllowed = "setup_scope_not_allowed";
        public const string NotPendingSignupWorkspace = "not_pending_signup_workspace";
        public const string PersonalPurposeDisabled = "personal_purpose_disabled";

        /// <summary>Personal purpose requires the Individual accounting profile, and business purpose must not use it.</summary>
        public const string EntityPurposeMismatch = "entity_purpose_mismatch";
        public const string RecoveryUnavailable = "recovery_unavailable";
        public const string RecoveryRequired = "recovery_required";
        public const string StalePolicyRevision = "stale_policy_revision";
        public const string IncompatiblePolicySchema = "incompatible_policy_schema";

        /// <summary>
        /// A member removal, suspension, departure or permission change (or account deletion) would leave a
        /// workspace that requires independent journal review without a usable reviewer.
        /// </summary>
        public const string LastReviewerUnavailable = "last_reviewer_unavailable";

        /// <summary>The caller is not an active workspace Owner and cannot authorize an approval-policy change.</summary>
        public const string OwnerAuthorizationRequired = "owner_authorization_required";

        /// <summary>The requested approval rule kind is not a recognised journal approval rule.</summary>
        public const string InvalidApprovalRule = "invalid_approval_rule";

        /// <summary>
        /// Single-actor journal posting may only be selected on an initialized workspace with a confirmed owner.
        /// Selecting it on an unclassified/uninitialized workspace would commit an unauthorized elevation.
        /// </summary>
        public const string WorkspaceNotInitialized = "workspace_not_initialized";

        /// <summary>The journal approval policy changed since an invitation was issued; resending needs explicit reconfirmation.</summary>
        public const string PolicyAssumptionsChanged = "policy_assumptions_changed";
    }

    public class IncompatiblePolicySchemaException : InvalidOperationException
    {
        public int CompanyId { get; }
        public int RequestedSchemaVersion { get; }
        public int SupportedSchemaVersion { get; }

        public IncompatiblePolicySchemaException(int companyId, int requestedSchemaVersion, int supportedSchemaVersion)
            : base($"Company {companyId} has policy schema version {requestedSchemaVersion}, which exceeds the supported version {supportedSchemaVersion}. Service upgrade required.")
        {
            CompanyId = companyId;
            RequestedSchemaVersion = requestedSchemaVersion;
            SupportedSchemaVersion = supportedSchemaVersion;
        }
    }

    public enum WorkspaceApprovalMode
    {
        /// <summary>No migrated/known rule — preserve catalog separation of duties.</summary>
        CatalogDefault = 0,
        IndependentReviewerRequired = 1,
        SingleActorAllowed = 2
    }

    public sealed record EffectiveApprovalRule(
        string OperationKey,
        WorkspaceApprovalMode Mode,
        WorkspaceApprovalRuleProvenance? Provenance,
        long PolicyRevision,
        int? ReviewerSecurityRoleId);

    public enum SoloSetupEligibilityStatus
    {
        Eligible = 0,
        Ineligible = 1,
        RecoveryRequired = 2
    }

    public sealed record SoloSetupEligibility(
        SoloSetupEligibilityStatus Status,
        string ReasonCode,
        string Message,
        long PolicyRevision);

    public sealed record WorkspaceOperatingSummary(
        WorkspacePurpose Purpose,
        WorkspaceCollaborationIntent CollaborationIntent,
        WorkspaceCollaborationState CollaborationState,
        long PolicyRevision,
        EffectiveApprovalRule NormalJournalWorkflow,
        string? OwnAccessProfileKey,
        string? OwnAccessProfileDisplayName,
        bool SoloSetupEnabled,
        IReadOnlyList<string> AllowedNextActions,
        // Server-derived: true only when the approved solo setup is actually AVAILABLE to the authenticated
        // caller — SoloSetupEnabled is true — AND
        // the workspace still carries the signup-specific pending-continuation marker and remains
        // structurally eligible (uninitialized, pristine, sole-owner) for the post-verification
        // continuation — i.e. the exact condition current/initialize will accept. The SPA routes to the
        // guided continuation on THIS flag, never on CollaborationState, so a legacy/non-signup unclassified
        // workspace, a non-owner member, an already-initialized workspace, or setup being disabled each
        // yields false and is never steered into a flow it cannot complete.
        bool PendingSignupContinuation);

    public interface IWorkspaceApprovalPolicyResolver
    {
        EffectiveApprovalRule Resolve(int companyId, string operationKey);
        EffectiveApprovalRule Resolve(WorkspaceOperatingSettings settings, WorkspaceApprovalRule? rule, string operationKey);
    }

    public interface IWorkspaceAccessProfileCatalog
    {
        string FullAccessProfileKey { get; }
        string FullAccessDisplayName { get; }
        int FullAccessProfileVersion { get; }
        IReadOnlyList<string> GetEffectivePermissionNames();
        Core.Domain.Security.SecurityRole? FindFullAccessRole();
        void EnsureSeeded();
    }

    /// <summary>
    /// Test-only synchronization gate for race-condition assertions.
    /// Not configurable from application settings or environment variables in production.
    /// </summary>
    public static class WorkspacePolicyTestGate
    {
        public static Action<int>? AfterLockAcquired { get; set; }
        public static Action<int>? AfterAuthorizationRead { get; set; }
        public static Action<int>? PostCommitVerificationFault { get; set; }

        public static void Reset()
        {
            AfterLockAcquired = null;
            AfterAuthorizationRead = null;
            PostCommitVerificationFault = null;
        }
    }

    public interface IWorkspacePolicyTransaction
    {
        /// <summary>
        /// Runs <paramref name="action"/> inside a guarded transaction after acquiring the company
        /// coordination row. Reuses an ambient EF transaction when one already exists.
        /// </summary>
        T Execute<T>(int companyId, Func<WorkspacePolicyGuard, T> action);

        void Execute(int companyId, Action<WorkspacePolicyGuard> action);

        T ExecuteAcrossCompanies<T>(IEnumerable<int> companyIds, Func<IReadOnlyList<WorkspacePolicyGuard>, T> action);

        void ExecuteAcrossCompanies(IEnumerable<int> companyIds, Action<IReadOnlyList<WorkspacePolicyGuard>> action);

        /// <summary>
        /// Opens a completion scope for an externally-owned transaction. Call immediately AFTER the owner
        /// begins its transaction; any guarded work or post-commit side effects enqueued while the scope is
        /// open are bound to it. The returned token MUST be completed exactly once — <see
        /// cref="IWorkspacePolicyCompletionToken.Committed"/> after the owner's transaction commits, or
        /// <see cref="IWorkspacePolicyCompletionToken.RolledBack"/> if it does not — so this service drains
        /// or discards ONLY that scope's items. This is explicit: commit versus rollback is never inferred
        /// from transaction disposal, so a later commit can never drain an earlier rolled-back scope's work.
        /// </summary>
        IWorkspacePolicyCompletionToken BeginExternalScope();

        /// <summary>
        /// Registers a side effect (e.g. telemetry) to run only after the owning transaction commits
        /// durably, never on rollback.
        /// </summary>
        void EnqueuePostCommit(Action action);
    }

    /// <summary>
    /// A one-shot completion handle for an externally-owned transaction scope. Exactly one of
    /// <see cref="Committed"/> or <see cref="RolledBack"/> must be called; both are idempotent no-ops after
    /// the first call. The token identifies the originating transaction explicitly, so the policy service
    /// drains (on commit) or discards (on rollback) only that transaction's deferred work — never another
    /// transaction's.
    /// </summary>
    public interface IWorkspacePolicyCompletionToken
    {
        /// <summary>The owner's transaction committed durably: drain this scope's verifications and side effects.</summary>
        void Committed();

        /// <summary>The owner's transaction did not commit: discard this scope's deferred work.</summary>
        void RolledBack();
    }

    /// <summary>Handle held while the company coordination row is locked.</summary>
    public sealed class WorkspacePolicyGuard
    {
        public int CompanyId { get; }
        public WorkspaceOperatingSettings Settings { get; }
        public long PolicyRevision => Settings.PolicyRevision;

        public WorkspacePolicyGuard(int companyId, WorkspaceOperatingSettings settings)
        {
            CompanyId = companyId;
            Settings = settings;
        }

        public void AdvanceRevision()
        {
            Settings.PolicyRevision++;
        }
    }

    public sealed record WorkspacePolicyMutationResult(
        bool Applied,
        string? ReasonCode,
        string? Message,
        long PolicyRevision,
        WorkspaceCollaborationState CollaborationState);

    /// <summary>An active human member who can currently review (approve) journal postings.</summary>
    public sealed record EligibleJournalReviewer(int MemberId, string UserId, CompanyMemberRole Role);

    /// <summary>
    /// A hypothetical change to one member used to evaluate reviewer capacity BEFORE it is applied.
    /// <see cref="NewRole"/> / <see cref="NewSecurityRoleIds"/> are null when that aspect is unchanged.
    /// </summary>
    public sealed record MemberReviewerChange(
        int MemberId,
        bool Removed,
        CompanyMemberRole? NewRole = null,
        IReadOnlyCollection<int>? NewSecurityRoleIds = null);

    /// <summary>
    /// Reviewer capacity for independent journal review. Preparers are members who can create journals;
    /// reviewers are Owner/Admin members who can post. Usable only when every preparer has at least one
    /// eligible reviewer who is not themselves — create-only and post-only roles may be held by different people.
    /// </summary>
    public sealed record IndependentReviewCapacity(
        int ActiveHumanCount,
        IReadOnlyList<EligibleJournalReviewer> EligibleReviewers,
        IReadOnlyList<EligibleJournalReviewer> EligiblePreparers)
    {
        public bool IsUsable =>
            EligibleReviewers.Count > 0
            && EligiblePreparers.Count > 0
            && EligiblePreparers.All(preparer =>
                EligibleReviewers.Any(reviewer =>
                    !string.Equals(reviewer.UserId, preparer.UserId, StringComparison.Ordinal)));
    }

    public sealed record ReviewerGuardResult(bool Blocked, string? ReasonCode, string? Message)
    {
        public static ReviewerGuardResult Allowed { get; } = new(false, null, null);
    }

    /// <summary>
    /// Single source of truth for "who can review a journal posting". Permission-aware (functional role
    /// grants <c>financials.journal.post</c>), governance-aware (approval records may only be recorded by an
    /// active Owner/Admin), and entitlement-aware (the financials module must be writable).
    /// </summary>
    public interface IWorkspaceReviewerEligibility
    {
        /// <summary>Eligible active human reviewers, optionally excluding one user (e.g. the acting Owner).</summary>
        IReadOnlyList<EligibleJournalReviewer> GetEligibleReviewers(int companyId, string? excludeUserId = null);

        /// <summary>Reviewer capacity as it would be after applying <paramref name="change"/> (or as-is when null).</summary>
        IndependentReviewCapacity EvaluateCapacity(int companyId, MemberReviewerChange? change = null);

        /// <summary>
        /// Blocks a member change only when the workspace explicitly requires independent journal review,
        /// review is usable today, and the change would make it unusable. Never relaxes approval.
        /// </summary>
        ReviewerGuardResult EvaluateMemberChange(int companyId, MemberReviewerChange change);

        /// <summary>
        /// Blocks a functional-role template permission change when any affected workspace that currently has
        /// usable independent review would lose independence for a permitted preparer. Pass the permission
        /// names the role would hold after the edit (empty when deleting the role's posting rights).
        /// </summary>
        ReviewerGuardResult EvaluateRolePermissionChange(
            int companyId,
            int securityRoleId,
            IReadOnlyCollection<string> newPermissionNamesForRole);
    }

    public interface IWorkspaceOperatingPolicyService
    {
        WorkspaceOperatingSummary GetSummary(int companyId, int? memberId = null, string? callerUserId = null);

        /// <summary>
        /// Owner-authorized explicit change of the journal approval rule. Records
        /// <see cref="WorkspaceApprovalRuleProvenance.ExplicitPolicyChange"/> and immutable audit evidence.
        /// Selecting independent review requires an eligible reviewer other than the caller.
        /// </summary>
        WorkspacePolicyMutationResult ChangeJournalApprovalPolicy(int companyId, string callerUserId, long expectedRevision, WorkspaceApprovalRuleKind newRuleKind);

        /// <summary>Same as above, inside an already-held coordination guard (e.g. a member change request).</summary>
        WorkspacePolicyMutationResult ChangeJournalApprovalPolicy(WorkspacePolicyGuard guard, string callerUserId, long expectedRevision, WorkspaceApprovalRuleKind newRuleKind);
        SoloSetupEligibility EvaluateLegacySoloEligibility(int companyId, string callerUserId);
        WorkspaceOperatingSettings EnsureCoordinationAndSettings(int companyId);
        WorkspacePolicyMutationResult ConfirmLegacySoloSetup(int companyId, string callerUserId, long expectedRevision);
        WorkspacePolicyMutationResult ReturnToSolo(int companyId, string callerUserId, long expectedRevision, bool revokeOutstandingInvitations);
        void UpdateConfirmedOwnerMember(int companyId, int newOwnerMemberId, string actorUserId);
    }
}
