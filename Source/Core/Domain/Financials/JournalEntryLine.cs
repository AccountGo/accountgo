//-----------------------------------------------------------------------
// <copyright file="JournalEntryLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("JournalEntryLine")]
    public partial class JournalEntryLine : BaseEntity
    {
        public int JournalEntryHeaderId { get; set; }
        public int AccountId { get; set; }
        public DrOrCrSide DrCr { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; }

        public virtual JournalEntryHeader JournalEntryHeader { get; set; }
        public virtual Account Account { get; set; }
    }
}
