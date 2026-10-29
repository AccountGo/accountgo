// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    /// <summary>One committed statement file (CSV / OFX) for a bank account (Phase 3A).</summary>
    [Table("BankStatementImport")]
    public partial class BankStatementImport : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int BankId { get; set; }
        public BankStatementSources Source { get; set; }

        [StringLength(260)]
        public string? FileName { get; set; }

        public DateTime ImportedAt { get; set; }

        [StringLength(100)]
        public string? ImportedBy { get; set; }

        /// <summary>CSV column map the user confirmed; null for OFX.</summary>
        public string? ColumnMapJson { get; set; }

        public virtual Bank Bank { get; set; } = null!;
        public virtual ICollection<BankStatementLine> Lines { get; set; } = new HashSet<BankStatementLine>();
    }
}
