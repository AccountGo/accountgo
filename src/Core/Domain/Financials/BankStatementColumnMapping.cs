// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    /// <summary>
    /// Last confirmed CSV column map for a bank account, keyed by normalised header names (#447 Phase B).
    /// </summary>
    [Table("BankStatementColumnMapping")]
    public partial class BankStatementColumnMapping : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int BankId { get; set; }

        /// <summary>Normalised header tokens joined by <c>|</c> (see <c>StatementHeaderSignature</c>).</summary>
        [Required, StringLength(1000)]
        public string HeaderSignature { get; set; } = string.Empty;

        [Required]
        public string ColumnMapJson { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; }

        public virtual Bank Bank { get; set; } = null!;
    }
}
