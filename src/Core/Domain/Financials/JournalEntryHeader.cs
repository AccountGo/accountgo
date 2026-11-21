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
    [PublicIdPrefix("je")]
    public partial class JournalEntryHeader : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public JournalEntryHeader()
        {
            JournalEntryLines = new HashSet<JournalEntryLine>();
        }

        public int CompanyId { get; set; }

        public int? GeneralLedgerHeaderId { get; set; }
        public int? PartyId { get; set; }
        public JournalVoucherTypes? VoucherType { get; set; }
        /// <summary>Closed fiscal year corrected by a <see cref="JournalVoucherTypes.PriorPeriodAdjustment"/> journal.</summary>
        public int? AdjustsFiscalYearId { get; set; }
        public DateTime Date { get; set; }
        public string? Memo { get; set; }
        public string? ReferenceNo { get; set; }
        public bool? Posted { get; set; }
        /// <summary>Lowercase identifier of the external source system for imported journal drafts, or null for manual journals.</summary>
        public string? SourceSystem { get; set; }
        /// <summary>Unique key within (<see cref="CompanyId"/>, <see cref="SourceSystem"/>) for idempotent draft creation, or null for manual journals.</summary>
        public string? SourceKey { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual Party? Party { get; set; }
        public virtual FinancialYear? AdjustsFiscalYear { get; set; }

        public virtual ICollection<JournalEntryLine> JournalEntryLines { get; set; } = null!;
    }
}
