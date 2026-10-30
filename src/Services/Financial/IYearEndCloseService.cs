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
        public string Message { get; set; } = "";
        public int Count { get; set; }
        public string? LinkPath { get; set; }
    }

    public sealed class YearEndClosingLine
    {
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
        public bool CanClose => Status != FiscalYearCloseStatus.Closed && Blocking.Count == 0;
    }

    public sealed class YearEndYearSummary
    {
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
        YearEndPreview Close(string fiscalYearCode, bool acknowledgeWarnings, string closedBy);
        YearEndPreview Reopen(string fiscalYearCode, string reason, string reopenedBy);
    }
}
