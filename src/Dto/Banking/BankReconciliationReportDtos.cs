// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class BankReconciliationReportDto
    {
        public int ReconciliationId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public DateTime StatementEndDate { get; set; }
        public string CurrencyCode { get; set; } = "USD";

        public decimal BeginningBalance { get; set; }
        public decimal ClearedDeposits { get; set; }
        public decimal ClearedPayments { get; set; }
        public decimal ClearedBalance { get; set; }

        public decimal StatementEndingBalance { get; set; }
        public decimal DepositsInTransit { get; set; }
        public decimal OutstandingPayments { get; set; }
        public decimal AdjustedBankBalance { get; set; }

        public decimal BooksBalance { get; set; }
        public decimal Difference { get; set; }

        public string? PreparedBy { get; set; }
        public DateTime? PreparedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        /// <summary>Opening-balance line cleared on this reconciliation, when present.</summary>
        public BankReconciliationReportLineDto? OpeningBalanceLine { get; set; }

        public IReadOnlyList<BankReconciliationReportLineDto> OutstandingItems { get; set; } =
            Array.Empty<BankReconciliationReportLineDto>();

        /// <summary>Lines dated in the period but posted after Finish (ordered by header creation).</summary>
        public IReadOnlyList<BankReconciliationReportLineDto> PostedAfterFinishItems { get; set; } =
            Array.Empty<BankReconciliationReportLineDto>();
    }

    public class BankReconciliationReportLineDto
    {
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public decimal In { get; set; }
        public decimal Out { get; set; }
        public int AgeDays { get; set; }
        public int HeaderId { get; set; }
    }
}
