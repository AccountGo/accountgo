using Dto.Common;
﻿using System;

namespace Dto.Financial
{
    public class JournalEntry : BaseDto
    {
        [PublicId("je")]
        public override int Id { get; set; }
        public DateTime JournalDate { get; set; }
        public int? VoucherType { get; set; }
        /// <summary>Closed fiscal year id when <see cref="VoucherType"/> is PriorPeriodAdjustment (7).</summary>
        [PublicId("fy")]
        public int? AdjustsFiscalYearId { get; set; }
        public string? AdjustsFiscalYearCode { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
        public bool? Posted { get; set; }
        public bool? ReadyForPosting { get; set; }
        /// <summary>Who prepared and posted this journal. Only set on a single-journal read by a member who may view record history.</summary>
        public JournalAttribution? Attribution { get; set; }
        public string? SourceSystem { get; set; }
        public string? SourceKey { get; set; }
        public decimal? debitAmount { get { return GetDebitAmount(); } }
        public decimal? creditAmount { get { return GetCreditAmount(); } }
        public System.Collections.Generic.IList<JournalEntryLine> JournalEntryLines { get; set; }

        public JournalEntry()
        {
            JournalEntryLines = new System.Collections.Generic.List<JournalEntryLine>();
        }

        private decimal GetDebitAmount()
        {
            decimal sum = 0;
            foreach(var entry in JournalEntryLines)
            {
                if(entry.DrCr == 1)
                    sum += entry.Amount.GetValueOrDefault();
            }
            return sum;
        }

        private decimal GetCreditAmount()
        {
            decimal sum = 0;
            foreach (var entry in JournalEntryLines)
            {
                if (entry.DrCr == 2)
                    sum += entry.Amount.GetValueOrDefault();
            }
            return sum;
        }
    }

    public class JournalEntryLine : BaseDto
    {
        [PublicId("jel")]
        public override int Id { get; set; }
        [PublicId("acct")]
        public int? AccountId { get; set; }
        public int DrCr { get; set; }
        public decimal? Amount { get; set; }
        public string? Memo { get; set; }
    }
}
