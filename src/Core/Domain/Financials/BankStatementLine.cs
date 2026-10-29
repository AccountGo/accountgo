// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    /// <summary>A line from a bank statement file. Never posts to the ledger by itself (Phase 3A).</summary>
    [Table("BankStatementLine")]
    public partial class BankStatementLine : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int BankId { get; set; }
        public int BankStatementImportId { get; set; }
        public BankStatementSources Source { get; set; }

        /// <summary>Bank-supplied id (OFX FITID) or a content hash for CSV; null disables dedupe.</summary>
        [StringLength(200)]
        public string? ExternalId { get; set; }

        public DateTime PostedDate { get; set; }

        /// <summary>Signed: positive is a deposit, negative is a payment.</summary>
        public decimal Amount { get; set; }

        [StringLength(200)]
        public string? Payee { get; set; }

        [StringLength(500)]
        public string? Memo { get; set; }

        [StringLength(100)]
        public string? Reference { get; set; }

        public int? MatchedGeneralLedgerLineId { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Bank Bank { get; set; } = null!;
        public virtual BankStatementImport BankStatementImport { get; set; } = null!;
        public virtual GeneralLedgerLine? MatchedGeneralLedgerLine { get; set; }
    }
}
