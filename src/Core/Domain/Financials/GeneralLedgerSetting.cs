//-----------------------------------------------------------------------
// <copyright file="GeneralLedgerSetting.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("GeneralLedgerSetting")]
    public partial class GeneralLedgerSetting : BaseEntity, ICompanyOwned
    {
        public GeneralLedgerSetting()
        {
        }
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = null!;

        public int? PayableAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }
        public int? GoodsReceiptNoteClearingAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        public int? ShippingChargeAccountId { get; set; }
        public int? PermanentAccountId { get; set; }
        public int? IncomeSummaryAccountId { get; set; }
        public int? CustomerCreditLiabilityAccountId { get; set; }
        public int? SalesReturnsAllowancesAccountId { get; set; }
        public int? SupplierCreditAssetAccountId { get; set; }
        public int? SupplierAdvanceAccountId { get; set; }
        public int? SupplierReturnClearingAccountId { get; set; }
        public int? ReturnValuationDifferenceAccountId { get; set; }
        public int? ReceivableLossAllowanceAccountId { get; set; }
        public int? ImpairmentExpenseAccountId { get; set; }
        public int? BadDebtRecoveryAccountId { get; set; }

        public virtual Account? PayableAccount { get; set; }
        public virtual Account? PurchaseDiscountAccount { get; set; }
        public virtual Account? GoodsReceiptNoteClearingAccount { get; set; }
        public virtual Account? SalesDiscountAccount { get; set; }
        public virtual Account? ShippingChargeAccount { get; set; }
        public virtual Account? PermanentAccount { get; set; }
        public virtual Account? IncomeSummaryAccount { get; set; }
        public virtual Account? CustomerCreditLiabilityAccount { get; set; }
        public virtual Account? SalesReturnsAllowancesAccount { get; set; }
        public virtual Account? SupplierCreditAssetAccount { get; set; }
        public virtual Account? SupplierAdvanceAccount { get; set; }
        public virtual Account? SupplierReturnClearingAccount { get; set; }
        public virtual Account? ReturnValuationDifferenceAccount { get; set; }
        public virtual Account? ReceivableLossAllowanceAccount { get; set; }
        public virtual Account? ImpairmentExpenseAccount { get; set; }
        public virtual Account? BadDebtRecoveryAccount { get; set; }
    }
}
