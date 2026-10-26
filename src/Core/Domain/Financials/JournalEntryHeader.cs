//-----------------------------------------------------------------------
// <copyright file="JournalEntryHeader.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("JournalEntryHeader")]
    public partial class JournalEntryHeader : BaseEntity, ICompanyOwned
    {
        public JournalEntryHeader()
        {
            JournalEntryLines = new HashSet<JournalEntryLine>();
        }

        public int CompanyId { get; set; }

        public int? GeneralLedgerHeaderId { get; set; }
        public int? PartyId { get; set; }
        public JournalVoucherTypes? VoucherType { get; set; }
        public DateTime Date { get; set; }
        public string Memo { get; set; }
        public string ReferenceNo { get; set; }
        public bool? Posted { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual Party Party { get; set; }

        public virtual ICollection<JournalEntryLine> JournalEntryLines { get; set; }
    }
}
