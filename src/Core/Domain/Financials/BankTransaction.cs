// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("BankTransaction")]
    public partial class BankTransaction : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public BankTransactionKinds Kind { get; set; }
        public int BankId { get; set; }
        /// <summary>Destination bank for <see cref="BankTransactionKinds.Transfer"/>.</summary>
        public int? ToBankId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        /// <summary>Contra GL account for money in/out, fee, interest, adjustment. Null for transfers and opening balance.</summary>
        public int? ContraAccountId { get; set; }
        public int? PartyId { get; set; }
        [Required]
        [StringLength(50)]
        public string No { get; set; } = string.Empty;
        [StringLength(100)]
        public string? ReferenceNo { get; set; }
        [StringLength(500)]
        public string? Memo { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public int? VoidGeneralLedgerHeaderId { get; set; }

        public bool IsVoid => VoidGeneralLedgerHeaderId.HasValue;

        public virtual Bank Bank { get; set; } = null!;
        public virtual Bank? ToBank { get; set; }
        public virtual Account? ContraAccount { get; set; }
        public virtual Party? Party { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }
    }
}
