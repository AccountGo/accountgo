// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    /// <summary>
    /// One-to-one operating policy for a workspace. Purpose and collaboration are independent of
    /// subscription, legal/accounting identity, functional grants, and approval rules.
    /// </summary>
    [Table("WorkspaceOperatingSettings")]
    public class WorkspaceOperatingSettings : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = null!;

        public WorkspacePurpose Purpose { get; set; } = WorkspacePurpose.Unspecified;
        public WorkspaceCollaborationIntent CollaborationIntent { get; set; } = WorkspaceCollaborationIntent.Unspecified;
        public WorkspaceCollaborationState CollaborationState { get; set; } = WorkspaceCollaborationState.LegacyUnclassified;

        /// <summary>Active human Owner whose setup was confirmed. Optional for unclassified legacy data.</summary>
        public int? ConfirmedOwnerMemberId { get; set; }
        public virtual CompanyMember? ConfirmedOwnerMember { get; set; }

        public DateTime? InitializedAtUtc { get; set; }
        public string? InitializedByUserId { get; set; }
        public WorkspaceInitializationSource InitializationSource { get; set; } = WorkspaceInitializationSource.Unspecified;

        /// <summary>Monotonic historical marker; never cleared when members leave.</summary>
        public DateTime? EverTeamActivatedAtUtc { get; set; }

        public DateTime? LastConfirmedAtUtc { get; set; }
        public string? LastConfirmedByUserId { get; set; }

        /// <summary>Application-managed durable revision; EF concurrency token.</summary>
        [ConcurrencyCheck]
        public long PolicyRevision { get; set; }

        public int PolicySchemaVersion { get; set; } = WorkspaceOperatingPolicySchema.CurrentVersion;
    }

    public static class WorkspaceOperatingPolicySchema
    {
        public const int CurrentVersion = 1;
    }
}
