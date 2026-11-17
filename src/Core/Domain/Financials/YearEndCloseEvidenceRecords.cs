// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    /// <summary>
    /// Company-scoped revision touched by every write that can change year-end readiness.
    /// Close compares a preview hash that includes this value.
    /// </summary>
    [Table("CompanyCloseState")]
    public class CompanyCloseState : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public long AccountingRevision { get; set; }
    }

    /// <summary>One close attempt. Reopen does not delete earlier attempts.</summary>
    [Table("YearEndCloseRun")]
    public class YearEndCloseRun : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int FinancialYearId { get; set; }
        public int AttemptNo { get; set; }
        [StringLength(64)]
        public string PreviewHash { get; set; } = "";
        public int PolicyVersion { get; set; }
        public long SourceRevision { get; set; }
        [StringLength(256)]
        public string Actor { get; set; } = "";
        public DateTime ClosedAtUtc { get; set; }
        public DateTime? BooksLockedThroughBefore { get; set; }
        public DateTime? BooksLockedThroughAfter { get; set; }
        public int? ClosingGeneralLedgerHeaderId { get; set; }
        [StringLength(2000)]
        public string? WarningExceptionReason { get; set; }
        [StringLength(64)]
        public string RecordedAuthority { get; set; } = "";
    }

    /// <summary>Canonical close snapshot. Append-only; a later close is a new row.</summary>
    [Table("YearEndCloseEvidence")]
    public class YearEndCloseEvidence : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int YearEndCloseRunId { get; set; }
        public int SchemaVersion { get; set; }
        [StringLength(64)]
        public string ContentDigest { get; set; } = "";
        public string Payload { get; set; } = "";
    }

    /// <summary>Append-only close, reopen, and external-issuance events.</summary>
    [Table("YearEndLifecycleEvent")]
    public class YearEndLifecycleEvent : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int FinancialYearId { get; set; }
        public int? YearEndCloseRunId { get; set; }
        [StringLength(32)]
        public string EventType { get; set; } = "";
        [StringLength(256)]
        public string Actor { get; set; } = "";
        public DateTime OccurredAtUtc { get; set; }
        [StringLength(2000)]
        public string? Reason { get; set; }
        public int? ReversalGeneralLedgerHeaderId { get; set; }
        public DateTime? BooksLockedThroughBefore { get; set; }
        public DateTime? BooksLockedThroughAfter { get; set; }
    }

    /// <summary>Idempotency receipt for a close command. Rolled back with the close.</summary>
    [Table("YearEndCommandReceipt")]
    public class YearEndCommandReceipt : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        [StringLength(64)]
        public string Operation { get; set; } = "";
        [StringLength(80)]
        public string IdempotencyKey { get; set; } = "";
        [StringLength(64)]
        public string RequestDigest { get; set; } = "";
        public int? YearEndCloseRunId { get; set; }
        public DateTime CommittedAtUtc { get; set; }
    }

    /// <summary>
    /// Whether annual financial statements for a year were issued outside AccountGo.
    /// Unknown is not treated as unissued. Issued is not cleared by a later unissued row.
    /// </summary>
    [Table("YearEndIssuanceDeclaration")]
    public class YearEndIssuanceDeclaration : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int FinancialYearId { get; set; }
        public int? YearEndCloseRunId { get; set; }
        /// <summary>Unknown, Unissued, or Issued.</summary>
        [StringLength(32)]
        public string Status { get; set; } = "Unknown";
        [StringLength(256)]
        public string Actor { get; set; } = "";
        public DateTime DeclaredAtUtc { get; set; }
        public DateTime? ExternalDate { get; set; }
        [StringLength(128)]
        public string? ExternalReference { get; set; }
        [StringLength(2000)]
        public string? Reason { get; set; }
    }
}
