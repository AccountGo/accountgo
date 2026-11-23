#nullable enable
using System.Text.RegularExpressions;

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
        /// or <c>null</c> when the caller assertion did not carry a valid session id.
        /// </summary>
        string? SessionId { get; }
    }

    public sealed partial class McpCallerSessionContext : IMcpCallerSessionContext
    {
        private static readonly Regex ValidSessionIdPattern = GenerateValidSessionIdRegex();

        public string? SessionId { get; private set; }

        /// <summary>
        /// Validates an assistant chat session id: 1–128 characters of <c>[A-Za-z0-9_-]</c> only.
        /// Any other value (null, empty, whitespace, invalid characters, or over 128 chars) is
        /// treated as absent (<c>null</c>) so the caller falls back to a per-request ephemeral key.
        /// </summary>
        public static string? NormalizeSessionId(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            return ValidSessionIdPattern.IsMatch(sessionId) ? sessionId : null;
        }

        public void SetSessionId(string? sessionId)
        {
            SessionId = NormalizeSessionId(sessionId);
        }

        [GeneratedRegex(@"^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
        private static partial Regex GenerateValidSessionIdRegex();
    }
}
