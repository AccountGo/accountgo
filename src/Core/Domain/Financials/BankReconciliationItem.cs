// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("BankReconciliationItem")]
    public partial class BankReconciliationItem : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int BankReconciliationId { get; set; }
        public int GeneralLedgerLineId { get; set; }
        public decimal Amount { get; set; }
        public DrOrCrSide DrCr { get; set; }

        public virtual BankReconciliation BankReconciliation { get; set; } = null!;
        public virtual GeneralLedgerLine GeneralLedgerLine { get; set; } = null!;
    }
}
