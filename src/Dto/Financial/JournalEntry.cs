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
        public System.Collections.Generic.IList<JournalEntryLine> JournalEntryLines { get; set; }

        public JournalEntry()
        {
            JournalEntryLines = new System.Collections.Generic.List<JournalEntryLine>();
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
