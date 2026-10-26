//-----------------------------------------------------------------------
// <copyright file="JournalEntryLine.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("JournalEntryLine")]
    public partial class JournalEntryLine : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int JournalEntryHeaderId { get; set; }
        public int AccountId { get; set; }
        public DrOrCrSide DrCr { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; }

        public virtual JournalEntryHeader JournalEntryHeader { get; set; }
        public virtual Account Account { get; set; }
    }
}
