#nullable enable

namespace Api.Service.Ai
{
    /// <summary>
    /// Request-scoped MCP caller session context populated exclusively from a validated
    /// signed <c>X-Mcp-Caller-Assertion</c> header. Never read from unsigned headers.
    /// </summary>
    public interface IMcpCallerSessionContext
    {
        /// <summary>
        /// The authenticated assistant chat session id bound to the current MCP call,
        /// or <c>null</c> when the caller assertion did not carry a session id.
        /// </summary>
        string? SessionId { get; }
    }

    public sealed class McpCallerSessionContext : IMcpCallerSessionContext
    {
        public string? SessionId { get; private set; }

        public void SetSessionId(string? sessionId)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        }
    }
}
