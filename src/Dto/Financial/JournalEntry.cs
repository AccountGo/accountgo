using System;

namespace Dto.Financial
{
    public class JournalEntry : BaseDto
    {
        public DateTime JournalDate { get; set; }
        public int? VoucherType { get; set; }
        public string ReferenceNo { get; set; }
        public string Memo { get; set; }
        public bool? Posted { get; set; }
        public bool? ReadyForPosting { get; set; }
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
        public int? AccountId { get; set; }
        public int DrCr { get; set; }
        public decimal? Amount { get; set; }
        public string Memo { get; set; }
    }
}
