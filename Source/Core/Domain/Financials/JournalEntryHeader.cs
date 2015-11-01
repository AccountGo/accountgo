//-----------------------------------------------------------------------
// <copyright file="JournalEntryHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("JournalEntryHeader")]
    public partial class JournalEntryHeader : BaseEntity
    {
        public JournalEntryHeader()
        {
            JournalEntryLines = new HashSet<JournalEntryLine>();
        }

        public int? GeneralLedgerHeaderId { get; set; }
        public int? PartyId { get; set; }
        public JournalVoucherTypes? VoucherType { get; set; }
        public DateTime Date { get; set; }
        public string Memo { get; set; }
        public string ReferenceNo { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual Party Party { get; set; }

        public virtual ICollection<JournalEntryLine> JournalEntryLines { get; set; }
    }
}
