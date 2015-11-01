//-----------------------------------------------------------------------
// <copyright file="GeneralLedgerHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Purchases;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("GeneralLedgerHeader")]
    public partial class GeneralLedgerHeader : BaseEntity
    {
        public GeneralLedgerHeader()
        {
            GeneralLedgerLines = new HashSet<GeneralLedgerLine>();
            PurchaseOrderReceipts = new HashSet<PurchaseReceiptHeader>();
        }

        public virtual DateTime Date { get; set; }
        public virtual DocumentTypes DocumentType { get; set; }
        public virtual string Description { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual ICollection<GeneralLedgerLine> GeneralLedgerLines { get; set; }
        public virtual ICollection<PurchaseReceiptHeader> PurchaseOrderReceipts { get; set; }

    }
}
