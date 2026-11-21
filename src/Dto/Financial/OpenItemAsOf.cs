#nullable enable
using System;
using Dto.Common;

namespace Dto.Financial
{
    /// <summary>One invoice or bill with what was still owed on it at the end of a given day.</summary>
    public class OpenItemAsOf
    {
        public string DocumentType { get; set; } = string.Empty;
        public string PartyType { get; set; } = string.Empty;
        [PublicIdByType(nameof(DocumentType))]
        public int Id { get; set; }
        public string? No { get; set; }
        [PublicIdByType(nameof(PartyType))]
        public int PartyId { get; set; }
        public string? PartyName { get; set; }
        public DateTime Date { get; set; }
        public DateTime DueDate { get; set; }
        public decimal BalanceDue { get; set; }
        /// <summary>False when the document has no ledger posting, so it is owed but absent from the control account.</summary>
        public bool Posted { get; set; }
    }
}
