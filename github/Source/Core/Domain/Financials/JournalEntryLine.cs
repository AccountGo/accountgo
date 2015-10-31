using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("JournalEntryLine")]
    public partial class JournalEntryLine : BaseEntity
    {
        public int JournalEntryHeaderId { get; set; }
        public int AccountId { get; set; }
        public TransactionTypes DrCr { get; set; }
        public decimal Amount { get; set; }
        public string Memo { get; set; }

        public virtual JournalEntryHeader JournalEntryHeader { get; set; }
        public virtual Account Account { get; set; }
    }
}
