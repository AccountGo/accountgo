// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    /// <summary>
    /// Operation-scoped approval policy. Absence of a known rule must preserve stricter catalog behavior,
    /// never imply single-actor permission.
    /// </summary>
    [Table("WorkspaceApprovalRule")]
    public class WorkspaceApprovalRule : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = null!;

        public string OperationKey { get; set; } = "";
        public WorkspaceApprovalRuleKind RuleKind { get; set; } = WorkspaceApprovalRuleKind.IndependentReviewerRequired;
        public WorkspaceApprovalRuleProvenance Provenance { get; set; } = WorkspaceApprovalRuleProvenance.LegacyEnforced;

        public int PolicySchemaVersion { get; set; } = WorkspaceOperatingPolicySchema.CurrentVersion;
        public DateTime ConfiguredAtUtc { get; set; }
        public string? ConfiguredByUserId { get; set; }
        public long ConfiguredAtPolicyRevision { get; set; }

        /// <summary>
        /// Optional reviewer capability hint using existing functional role id.
        /// Authorization still validates a different active eligible human at execution time.
        /// </summary>
        public int? ReviewerSecurityRoleId { get; set; }
        public virtual Security.SecurityRole? ReviewerSecurityRole { get; set; }
    }
}
