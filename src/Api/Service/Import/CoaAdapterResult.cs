#nullable enable
using System.Collections.Generic;

namespace Api.Service.Import
{
    /// <summary>Outcome of Detect → MapToCanonical for a COA CSV payload.</summary>
    public sealed class CoaAdapterResult
    {
        public string FormatKey { get; set; } = "unknown";
        public string? DisplayName { get; set; }
        public bool Success => FileErrors.Count == 0;
        public List<string> FileErrors { get; set; } = new();
        public List<string> FileWarnings { get; set; } = new();
        public List<CoaCanonicalRow> Rows { get; set; } = new();
    }
}
