// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("BankReconciliation")]
    public partial class BankReconciliation : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int BankId { get; set; }
        public DateTime StatementEndDate { get; set; }
        public decimal StatementEndingBalance { get; set; }
        public decimal BeginningBalance { get; set; }
        public BankReconciliationStatuses Status { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        [StringLength(100)]
        public string? FinishedBy { get; set; }
        public DateTime? FinishedAt { get; set; }

        [StringLength(100)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        [StringLength(100)]
        public string? UndoneBy { get; set; }
        public DateTime? UndoneAt { get; set; }
        [StringLength(500)]
        public string? UndoReason { get; set; }

        /// <summary>Optimistic concurrency token (integer; incremented on each write).</summary>
        public long Version { get; set; } = 1;

        public virtual Bank Bank { get; set; } = null!;
        public virtual ICollection<BankReconciliationItem> Items { get; set; } = new HashSet<BankReconciliationItem>();
    }
}
