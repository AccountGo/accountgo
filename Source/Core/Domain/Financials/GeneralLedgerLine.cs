//-----------------------------------------------------------------------
// <copyright file="GeneralLedgerLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("GeneralLedgerLine")]
    public partial class GeneralLedgerLine : BaseEntity
    {
        public GeneralLedgerLine()
        {
        }

        public int GeneralLedgerHeaderId { get; set; }
        public int AccountId { get; set; }
        public TransactionTypes DrCr { get; set; }
        public decimal Amount { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Account Account { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
    }
}
