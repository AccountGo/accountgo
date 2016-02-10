//-----------------------------------------------------------------------
// <copyright file="GeneralLedgerSetting.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("GeneralLedgerSetting")]
    public partial class GeneralLedgerSetting : BaseEntity
    {
        public GeneralLedgerSetting()
        {
        }
        public int? CompanyId { get; set; }
        public virtual Company Company { get; set; }

        public int? PayableAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }
        public int? GoodsReceiptNoteClearingAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        public int? ShippingChargeAccountId { get; set; }
        public int? PermanentAccountId { get; set; }
        public int? IncomeSummaryAccountId { get; set; }

        public virtual Account PayableAccount { get; set; }
        public virtual Account PurchaseDiscountAccount { get; set; }
        public virtual Account GoodsReceiptNoteClearingAccount { get; set; }
        public virtual Account SalesDiscountAccount { get; set; }
        public virtual Account ShippingChargeAccount { get; set; }
        public virtual Account PermanentAccount { get; set; }
        public virtual Account IncomeSummaryAccount { get; set; }
    }
}
