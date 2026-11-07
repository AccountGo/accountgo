#nullable enable
using System.Text.Json;

namespace Api.Mcp.Models
{
    /// <summary>
    /// Envelope for the workspace data tools (sales, purchases, banking, catalog, journals, workspace context).
    /// <see cref="Data"/> holds the record or page of records in the same camelCase shape the AccountGo pages receive.
    /// List tools also fill the paging fields; detail tools leave them null.
    /// </summary>
    public sealed class WorkspaceDataMcpResponse : FinancialMcpBaseResponse
    {
        /// <summary>Which data this is, e.g. "invoice" or "invoices".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Total matches before paging (list tools only).</summary>
        public int? TotalCount { get; set; }

        public int? Skip { get; set; }

        public int? Take { get; set; }

        /// <summary>The page path the user can open for this record or list, relative to the workspace.</summary>
        public string? Link { get; set; }

        public JsonElement? Data { get; set; }
    }
}
