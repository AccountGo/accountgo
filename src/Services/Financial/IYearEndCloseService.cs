// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Services.Financial
{
    public enum FiscalYearCloseStatus
    {
        Open = 1,
        Ended = 2,
        Closed = 3
    }

    public sealed class YearEndCheckItem
    {
        public string Code { get; set; } = "";
        public string Tier { get; set; } = "";
        public string State { get; set; } = "Fail";
        public string Message { get; set; } = "";
        public int Count { get; set; }
        public decimal? Amount { get; set; }
        public string? LinkPath { get; set; }
        public bool CanResolve { get; set; }
        public string? RequiredPermission { get; set; }
    }

    public sealed class YearEndCloseCommand
    {
        public string? WarningExceptionReason { get; init; }
        public string? PreviewHash { get; init; }
        public string? IdempotencyKey { get; init; }
        public bool RequirePreviewHash { get; init; }
        public bool RequireWarningReason { get; init; }
        public bool IndependentReviewerRequired { get; init; }
        public bool ActorIsOwnerOrAdmin { get; init; }
    }

    public sealed class YearEndReopenCommand
    {
        public bool IndependentReviewerRequired { get; init; }
        public bool ActorIsOwnerOrAdmin { get; init; }
    }

    public sealed class YearEndIssuanceCommand
    {
        public string Status { get; init; } = "";
        public string? Reason { get; init; }
        public DateTime? ExternalDate { get; init; }
        public string? ExternalReference { get; init; }
    }

    public sealed class YearEndClosingLine
    {
        [Dto.Common.PublicId("acct")]
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string DrCr { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public sealed class YearEndPreview
    {
        public string FiscalYearCode { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FiscalYearCloseStatus Status { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedBy { get; set; }
        public IReadOnlyList<YearEndCheckItem> Blocking { get; set; } = Array.Empty<YearEndCheckItem>();
        public IReadOnlyList<YearEndCheckItem> Warnings { get; set; } = Array.Empty<YearEndCheckItem>();
        public IReadOnlyList<YearEndClosingLine> ClosingLines { get; set; } = Array.Empty<YearEndClosingLine>();
        public IReadOnlyList<PartnerAllocationPreview> PartnerAllocations { get; set; } = Array.Empty<PartnerAllocationPreview>();
        public string? PartnerLossNotice { get; set; }
        /// <summary>The equity account that receives the year's result, so the preview can name it. Null once closed or when none is configured.</summary>
        public string? CloseTargetAccountCode { get; set; }
        public string? CloseTargetAccountName { get; set; }
        public DateTime? CurrentBooksLockedThrough { get; set; }
        public DateTime? ProposedBooksLockedThrough { get; set; }
        public bool RequiresLockAcknowledgement { get; set; }
        public DateTime? NewlyUnlockedFrom { get; set; }
        public DateTime? NewlyUnlockedThrough { get; set; }
        public bool CanClose => Status != FiscalYearCloseStatus.Closed && Blocking.Count == 0;
        public string PreviewHash { get; set; } = "";
        public long AccountingRevision { get; set; }
        public IReadOnlyList<YearEndCheckItem> Checks { get; set; } = Array.Empty<YearEndCheckItem>();
        public string? PackageNote { get; set; }
        public string IssuanceStatus { get; set; } = "Unknown";
        public bool CanReopen { get; set; }
        public string? ReopenBlockedReason { get; set; }
        [Dto.Common.PublicId("fycr")]
        public int? LatestCloseRunId { get; set; }
    }

    public sealed class PartnerAllocationPreview
    {
        [Dto.Common.PublicId("part")]
        public int PartnerId { get; set; }
        public string Name { get; set; } = "";
        public decimal Percent { get; set; }
        public decimal ProfitShare { get; set; }
        public decimal DrawingsClosed { get; set; }
        public decimal CapitalAfterClose { get; set; }
    }

    public sealed class YearEndYearSummary
    {
        [Dto.Common.PublicId("fy")]
        public int Id { get; set; }
        public string FiscalYearCode { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public FiscalYearCloseStatus Status { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedBy { get; set; }
    }

    public interface IYearEndCloseService
    {
        IReadOnlyList<YearEndYearSummary> ListYears();
        YearEndPreview Preview(string fiscalYearCode);
        YearEndPreview Close(string fiscalYearCode, bool acknowledgeWarnings, string closedBy, YearEndCloseCommand? command = null);
        YearEndPreview Reopen(string fiscalYearCode, string reason, string reopenedBy, bool acknowledgeIndependentLock = false, DateTime? expectedLockThrough = null, YearEndReopenCommand? command = null);
        YearEndPreview DeclareIssuance(string fiscalYearCode, YearEndIssuanceCommand command, string actor);
    }
}
