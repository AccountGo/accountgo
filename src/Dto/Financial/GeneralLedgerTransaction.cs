#nullable disable
using System;
using System.Collections.Generic;

namespace Dto.Financial
{
    /// <summary>A posted journal, including every line even when filtered by account.</summary>
    public class GeneralLedgerTransaction
    {
        /// <summary>Gets or sets the posted transaction number.</summary>
        public int Id { get; set; }
        /// <summary>Gets or sets the posting date.</summary>
        public DateTime Date { get; set; }
        /// <summary>Gets or sets the source document type name.</summary>
        public string DocumentType { get; set; }
        /// <summary>Gets or sets the human-readable document type.</summary>
        public string DocumentTypeLabel { get; set; }
        /// <summary>Gets or sets the journal narration.</summary>
        public string Description { get; set; }
        /// <summary>Gets or sets the originating document identifier.</summary>
        public int? SourceDocumentId { get; set; }
        /// <summary>Gets or sets an optional SPA path relative to the organization.</summary>
        public string SourcePath { get; set; }
        /// <summary>Gets or sets the original journal number for a reversal.</summary>
        public int? ReversedGeneralLedgerHeaderId { get; set; }
        /// <summary>Gets or sets total debits in major currency units.</summary>
        public decimal TotalDebit { get; set; }
        /// <summary>Gets or sets total credits in major currency units.</summary>
        public decimal TotalCredit { get; set; }
        /// <summary>Gets or sets all account lines.</summary>
        public List<GeneralLedgerTransactionLine> Lines { get; set; } = new List<GeneralLedgerTransactionLine>();
    }

    /// <summary>An immutable posted account movement in major currency units.</summary>
    public class GeneralLedgerTransactionLine
    {
        /// <summary>Gets or sets the ledger line identifier.</summary>
        public int Id { get; set; }
        /// <summary>Gets or sets the account identifier.</summary>
        public int AccountId { get; set; }
        /// <summary>Gets or sets the account code.</summary>
        public string AccountCode { get; set; }
        /// <summary>Gets or sets the account name.</summary>
        public string AccountName { get; set; }
        /// <summary>Gets or sets the debit amount.</summary>
        public decimal Debit { get; set; }
        /// <summary>Gets or sets the credit amount.</summary>
        public decimal Credit { get; set; }
    }
}
