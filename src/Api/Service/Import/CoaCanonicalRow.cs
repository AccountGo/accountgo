#nullable enable
using System.Collections.Generic;

namespace Api.Service.Import
{
    /// <summary>Normalized account row produced by a COA format adapter before shared validate/commit.</summary>
    public sealed class CoaCanonicalRow
    {
        public int RowNumber { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string ParentAccountCode { get; set; } = string.Empty;
        public int AccountClassId { get; set; }
        public string AccountClassName { get; set; } = "Unknown";
        public bool IsContraAccount { get; set; }
        public bool IsCash { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
