#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Service.Ai;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Api.Controllers
{
    /// <summary>
    /// Authenticated proxy for AccountGo Assistant → Anter agent-runner.
    /// Anter API keys never leave the server.
    /// </summary>
    [TenantScoped]
    [EnableRateLimiting("ai-chat")]
    [Route("api/ai/chat")]
    [ApiController]
    public class AiChatController(
        IAnterAgentRunnerClient anterClient,
        ITenantContext tenantContext,
        ILogger<AiChatController> logger) : BaseController
    {
        private readonly IAnterAgentRunnerClient _anterClient = anterClient
            ?? throw new ArgumentNullException(nameof(anterClient));
        private readonly ITenantContext _tenantContext = tenantContext
            ?? throw new ArgumentNullException(nameof(tenantContext));
        private readonly ILogger<AiChatController> _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        public sealed class RunStreamRequest
        {
            public string? Message { get; set; }
            public string? SessionId { get; set; }
            public Dictionary<string, string>? ContextVariables { get; set; }
        }

        public sealed class ApprovalRequest
        {
            public string? Decision { get; set; }
            public string? Reason { get; set; }
        }

        private string? CurrentUserId =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        [HttpPost("run-stream")]
        public async Task RunStream([FromBody] RunStreamRequest body, CancellationToken cancellationToken)
        {
            if (!await EnsureConfiguredAsync(cancellationToken)) return;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!_tenantContext.IsResolved || !_tenantContext.CompanyId.HasValue)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { detail = "Workspace context is required." }, cancellationToken);
                return;
            }

            var message = body?.Message ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { detail = "message is required." }, cancellationToken);
                return;
            }

            if (message.Length > AnterRunStreamBody.MaxMessageLength)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(
                    new { detail = $"message must be at most {AnterRunStreamBody.MaxMessageLength} characters." },
                    cancellationToken);
                return;
            }

            var safeContext = SanitizeClientContext(body?.ContextVariables);
            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.RunStreamAsync(
                    message,
                    userId,
                    body?.SessionId,
                    safeContext,
                    companyId: _tenantContext.CompanyId.Value.ToString(),
                    companySlug: _tenantContext.CompanySlug,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Anter run-stream failed");
                Response.StatusCode = StatusCodes.Status502BadGateway;
                await Response.WriteAsJsonAsync(new { detail = "Could not reach the assistant service." }, cancellationToken);
                return;
            }

            await ProxyUpstreamAsync(upstream, cancellationToken);
        }

        [HttpGet("sessions")]
        public async Task<IActionResult> ListSessions(CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            using var upstream = await _anterClient.ListSessionsAsync(userId, cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<IActionResult> GetSession(string sessionId, CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            using var upstream = await _anterClient.GetSessionAsync(sessionId, userId, cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        [HttpDelete("sessions/{sessionId}")]
        public async Task<IActionResult> DeleteSession(string sessionId, CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            using var upstream = await _anterClient.DeleteSessionAsync(sessionId, userId, cancellationToken);
            if (upstream.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Ok(new { ok = true });
            }
            if ((int)upstream.StatusCode >= 400)
            {
                return await ToActionResultAsync(upstream, cancellationToken);
            }
            return Ok(new { ok = true });
        }

        [HttpGet("executions/{executionId}/stream")]
        public async Task StreamExecution(
            string executionId,
            [FromQuery] int resumeFrom = 0,
            CancellationToken cancellationToken = default)
        {
            if (!await EnsureConfiguredAsync(cancellationToken)) return;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (resumeFrom < 0) resumeFrom = 0;

            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.GetExecutionStreamAsync(
                    executionId,
                    resumeFrom,
                    userId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Anter execution stream failed");
                Response.StatusCode = StatusCodes.Status502BadGateway;
                await Response.WriteAsJsonAsync(new { detail = "Could not reach the assistant service." }, cancellationToken);
                return;
            }

            await ProxyUpstreamAsync(upstream, cancellationToken);
        }

        [HttpPost("executions/{executionId}/cancel")]
        public async Task<IActionResult> CancelExecution(string executionId, CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            using var upstream = await _anterClient.CancelExecutionAsync(executionId, userId, cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        [HttpPost("executions/{executionId}/resume")]
        public async Task ResumeExecution(string executionId, CancellationToken cancellationToken)
        {
            if (!await EnsureConfiguredAsync(cancellationToken)) return;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.ResumeExecutionAsync(executionId, userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Anter resume failed");
                Response.StatusCode = StatusCodes.Status502BadGateway;
                await Response.WriteAsJsonAsync(new { detail = "Could not reach the assistant service." }, cancellationToken);
                return;
            }

            await ProxyUpstreamAsync(upstream, cancellationToken);
        }

        [HttpPost("approvals/{executionId}/{toolCallId}")]
        public async Task<IActionResult> ResolveApproval(
            string executionId,
            string toolCallId,
            [FromBody] ApprovalRequest body,
            CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var decision = body?.Decision;
            if (decision is not ("approved" or "denied"))
            {
                return BadRequest(new { detail = "decision must be 'approved' or 'denied'." });
            }

            using var upstream = await _anterClient.ResolveApprovalAsync(
                executionId,
                toolCallId,
                decision,
                body?.Reason,
                userId,
                cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        /// <summary>
        /// Keep only non-identity client context (e.g. pageRoute). Tenant and user
        /// fields are applied later from JWT + ITenantContext.
        /// </summary>
        internal static Dictionary<string, string> SanitizeClientContext(
            Dictionary<string, string>? incoming)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (incoming == null) return merged;

            foreach (var pair in incoming)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                if (IsServerOwnedKey(pair.Key)) continue;
                if (pair.Value.Length > 2_000) continue;
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        private static bool IsServerOwnedKey(string key) =>
            key.Equals("userId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sessionId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("organizationId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("companyId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("companySlug", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> EnsureConfiguredAsync(CancellationToken cancellationToken)
        {
            if (_anterClient.IsConfigured) return true;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(new
            {
                detail = "AccountGo Assistant is not configured on this deployment."
            }, cancellationToken);
            return false;
        }

        private bool EnsureConfiguredResult(out IActionResult? result)
        {
            if (_anterClient.IsConfigured)
            {
                result = null;
                return true;
            }

            result = StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { detail = "AccountGo Assistant is not configured on this deployment." });
            return false;
        }

        private async Task ProxyUpstreamAsync(HttpResponseMessage upstream, CancellationToken cancellationToken)
        {
            using (upstream)
            {
                Response.StatusCode = (int)upstream.StatusCode;

                if ((int)upstream.StatusCode >= 400)
                {
                    // Do not forward raw upstream bodies (may leak provider internals).
                    var upstreamBody = await upstream.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Anter chat upstream returned {Status}. Body length={Length}",
                        (int)upstream.StatusCode,
                        upstreamBody?.Length ?? 0);
                    Response.ContentType = "application/json";
                    await Response.WriteAsJsonAsync(
                        new { detail = "The assistant service rejected the request." },
                        cancellationToken);
                    return;
                }

                var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
                Response.ContentType = contentType;
                Response.Headers.CacheControl = "no-cache";
                Response.Headers.Append("X-Accel-Buffering", "no");

                await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
                await stream.CopyToAsync(Response.Body, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }

        private static async Task<IActionResult> ToActionResultAsync(
            HttpResponseMessage upstream,
            CancellationToken cancellationToken)
        {
            var status = (int)upstream.StatusCode;
            var text = await upstream.Content.ReadAsStringAsync(cancellationToken);
            if (status >= 400)
            {
                return new ObjectResult(new { detail = "The assistant service rejected the request." })
                {
                    StatusCode = status,
                };
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new StatusCodeResult(status);
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                return new ObjectResult(doc.RootElement.Clone()) { StatusCode = status };
            }
            catch (JsonException)
            {
                return new ContentResult
                {
                    StatusCode = status,
                    Content = text,
                    ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "text/plain",
                };
            }
        }
    }
}
