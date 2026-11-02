// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Core.Domain
{
    /// <summary>Explicit workspace purpose. Independent of plan, entity type, and collaboration.</summary>
    public enum WorkspacePurpose
    {
        Unspecified = 0,
        Personal = 1,
        Business = 2
    }

    /// <summary>Latest explicit owner choice about who will use the workspace.</summary>
    public enum WorkspaceCollaborationIntent
    {
        Unspecified = 0,
        JustMe = 1,
        WithOthers = 2
    }

    /// <summary>Current operating collaboration state. Historical team activation is tracked separately.</summary>
    public enum WorkspaceCollaborationState
    {
        LegacyUnclassified = 0,
        SoloActive = 1,
        TeamSetupPending = 2,
        TeamActive = 3
    }

    public enum WorkspaceInitializationSource
    {
        Unspecified = 0,
        NewWorkspace = 1,
        VerifiedLegacySetup = 2,
        ControlledRecovery = 3
    }

    public enum WorkspaceApprovalRuleKind
    {
        IndependentReviewerRequired = 0,
        SingleActorAllowed = 1
    }

    public enum WorkspaceApprovalRuleProvenance
    {
        LegacyEnforced = 0,
        NewWorkspaceConfirmed = 1,
        ExplicitPolicyChange = 2
    }

    public enum WorkspaceMemberGrantOrigin
    {
        LegacyExplicit = 0,
        SoloInitialization = 1,
        TeamCreationPreset = 2,
        Invitation = 3,
        AccessRequest = 4,
        Delegation = 5,
        Recovery = 6
    }

    public enum OwnerAccessPreset
    {
        Unspecified = 0,
        FullWorkspaceAccess = 1,
        Narrower = 2
    }

    /// <summary>Registration disclosure only — not a tax or legal determination.</summary>
    public enum BusinessRegistrationStatus
    {
        NotProvided = 0,
        NotRegistered = 1,
        Registered = 2,
        NotApplicable = 3
    }
}
