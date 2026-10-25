#nullable enable
using System;

namespace Api.Service.Ai
{
    public sealed record McpCallerAssertionPayload(
        string UserId,
        int CompanyId,
        string CompanySlug,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds);

    /// <summary>
    /// Mints and validates short-lived HMAC-SHA256 caller assertions (<c>X-Mcp-Caller-Assertion</c>)
    /// passed through Anter's <c>mcpPassThroughHeaders</c> overlay to bind shared-agent MCP tool
    /// executions to the authenticated AccountGo user and resolved workspace.
    /// </summary>
    public interface IMcpCallerAssertionService
    {
        string MintAssertion(string userId, int companyId, string companySlug, TimeSpan? ttl = null);

        bool TryValidateAssertion(string? assertion, out McpCallerAssertionPayload? payload);
    }
}
