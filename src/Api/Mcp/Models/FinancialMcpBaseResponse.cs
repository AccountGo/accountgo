#nullable enable
using System.Collections.Generic;

namespace Api.Mcp.Models
{
    /// <summary>
    /// Agent-facing entitlement envelope for financial MCP tools.
    /// Soft <see cref="FinancialMcpStatus.PlanRestricted"/> / <see cref="FinancialMcpStatus.AccessDenied"/>
    /// results succeed at the MCP RPC layer so Anter does not trip its tool-failure circuit breaker.
    /// </summary>
    public abstract class FinancialMcpBaseResponse
    {
        public string Organization { get; set; } = string.Empty;

        /// <summary>One of <see cref="FinancialMcpStatus"/>.</summary>
        public string Status { get; set; } = FinancialMcpStatus.Success;

        /// <summary>Human-readable explanation when Status is not Success; null on success.</summary>
        public string? Message { get; set; }

        /// <summary>Modules that would unlock this tool (set on PlanRestricted).</summary>
        public IReadOnlyList<string>? RequiredModules { get; set; }

        /// <summary>Suggested upgrade plan key, e.g. "accounting" (set on PlanRestricted).</summary>
        public string? UpgradePlanHint { get; set; }
    }

    public static class FinancialMcpStatus
    {
        public const string Success = "Success";
        public const string PlanRestricted = "PlanRestricted";
        public const string AccessDenied = "AccessDenied";

        /// <summary>The requested record does not exist in this workspace.</summary>
        public const string NotFound = "NotFound";

        /// <summary>The arguments were not valid for this lookup; Message says why.</summary>
        public const string InvalidRequest = "InvalidRequest";

        /// <summary>The lookup failed for another reason; Message says what to tell the user.</summary>
        public const string Error = "Error";
    }
}
