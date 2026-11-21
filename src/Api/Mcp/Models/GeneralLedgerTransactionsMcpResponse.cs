#nullable enable
using System;
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    public class GeneralLedgerTransactionsMcpResponse : FinancialMcpBaseResponse
    {
        public int TotalCount { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? AccountCode { get; set; }
        public List<GeneralLedgerTransactionMcp> Transactions { get; set; } = new();
    }

    /// <summary>A posted journal for assistants. Ids are public ids (<c>gl_…</c>, <c>acct_…</c>), never integer keys.</summary>
    public class GeneralLedgerTransactionMcp
    {
        public string? Id { get; set; }
        public DateTime Date { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentTypeLabel { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>The originating document's public id; its prefix depends on <see cref="DocumentType"/>. Null when the type has no public id.</summary>
        public string? SourceDocumentId { get; set; }
        public string? SourcePath { get; set; }
        public string? ReversedGeneralLedgerHeaderId { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public List<GeneralLedgerTransactionLineMcp> Lines { get; set; } = new();
    }

    public class GeneralLedgerTransactionLineMcp
    {
        public string? Id { get; set; }
        public string? AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
