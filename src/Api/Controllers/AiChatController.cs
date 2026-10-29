#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Service.Ai;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Services.Administration;

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
        IMcpCallerAssertionService assertionService,
        ITenantContext tenantContext,
        IAdministrationService administrationService,
        ILogger<AiChatController> logger) : BaseController
    {
        private readonly IAnterAgentRunnerClient _anterClient = anterClient
            ?? throw new ArgumentNullException(nameof(anterClient));
        private readonly IMcpCallerAssertionService _assertionService = assertionService
            ?? throw new ArgumentNullException(nameof(assertionService));
        private readonly ITenantContext _tenantContext = tenantContext
            ?? throw new ArgumentNullException(nameof(tenantContext));
        private readonly IAdministrationService _administrationService = administrationService
            ?? throw new ArgumentNullException(nameof(administrationService));
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

            var companyId = _tenantContext.CompanyId.Value;
            var companySlug = _tenantContext.CompanySlug ?? $"company-{companyId}";
            var scopedUserId = AnterRunStreamBody.BuildScopedUserId(companyId, userId);
            var callerAssertion = _assertionService.MintAssertion(userId, companyId, companySlug);
            var safeContext = SanitizeClientContext(body?.ContextVariables);
            ResolveEntitlementContext(
                out var companyPlan,
                out var activeModules,
                out var companyRole,
                out var subscriptionStatus);

            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.RunStreamAsync(
                    message,
                    scopedUserId,
                    body?.SessionId,
                    safeContext,
                    companyId: companyId.ToString(),
                    companySlug: companySlug,
                    cancellationToken: cancellationToken,
                    callerAssertion: callerAssertion,
                    accountGoUserId: userId,
                    companyPlan: companyPlan,
                    activeModules: activeModules,
                    companyRole: companyRole,
                    subscriptionStatus: subscriptionStatus);
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
            if (!TryResolveScopedUser(out var scopedUserId, out _, out var authFail)) return authFail!;

            using var upstream = await _anterClient.ListSessionsAsync(scopedUserId, cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<IActionResult> GetSession(string sessionId, CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            if (!TryResolveScopedUser(out var scopedUserId, out _, out var authFail)) return authFail!;

            using var upstream = await _anterClient.GetSessionAsync(sessionId, scopedUserId, cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        [HttpDelete("sessions/{sessionId}")]
        public async Task<IActionResult> DeleteSession(string sessionId, CancellationToken cancellationToken)
        {
            if (!EnsureConfiguredResult(out var fail)) return fail!;
            if (!TryResolveScopedUser(out var scopedUserId, out _, out var authFail)) return authFail!;

            using var upstream = await _anterClient.DeleteSessionAsync(sessionId, scopedUserId, cancellationToken);
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

            if (!_tenantContext.IsResolved || !_tenantContext.CompanyId.HasValue)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { detail = "Workspace context is required." }, cancellationToken);
                return;
            }

            if (resumeFrom < 0) resumeFrom = 0;
            var scopedUserId = AnterRunStreamBody.BuildScopedUserId(_tenantContext.CompanyId.Value, userId);

            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.GetExecutionStreamAsync(
                    executionId,
                    resumeFrom,
                    scopedUserId,
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
            if (!TryResolveScopedUser(out var scopedUserId, out _, out var authFail)) return authFail!;

            using var upstream = await _anterClient.CancelExecutionAsync(executionId, scopedUserId, cancellationToken);
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

            if (!_tenantContext.IsResolved || !_tenantContext.CompanyId.HasValue)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { detail = "Workspace context is required." }, cancellationToken);
                return;
            }

            var companyId = _tenantContext.CompanyId.Value;
            var companySlug = _tenantContext.CompanySlug ?? $"company-{companyId}";
            var scopedUserId = AnterRunStreamBody.BuildScopedUserId(companyId, userId);
            var callerAssertion = _assertionService.MintAssertion(userId, companyId, companySlug);

            HttpResponseMessage upstream;
            try
            {
                upstream = await _anterClient.ResumeExecutionAsync(
                    executionId,
                    scopedUserId,
                    cancellationToken,
                    callerAssertion);
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
            if (!TryResolveScopedUser(out var scopedUserId, out _, out var authFail)) return authFail!;

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
                scopedUserId,
                cancellationToken);
            return await ToActionResultAsync(upstream, cancellationToken);
        }

        private bool TryResolveScopedUser(
            out string scopedUserId,
            out string rawUserId,
            out IActionResult? failure)
        {
            scopedUserId = string.Empty;
            rawUserId = CurrentUserId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawUserId))
            {
                failure = Unauthorized();
                return false;
            }

            if (!_tenantContext.IsResolved || !_tenantContext.CompanyId.HasValue)
            {
                failure = BadRequest(new { detail = "Workspace context is required." });
                return false;
            }

            scopedUserId = AnterRunStreamBody.BuildScopedUserId(_tenantContext.CompanyId.Value, rawUserId);
            failure = null;
            return true;
        }

        /// <summary>
        /// Keep only non-identity client context (e.g. pageRoute). Tenant, user, and
        /// entitlement fields are applied later from JWT + ITenantContext / subscription.
        /// </summary>
        internal static Dictionary<string, string> SanitizeClientContext(
            Dictionary<string, string>? incoming)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (incoming == null) return merged;

            foreach (var pair in incoming)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                if (AnterRunStreamBody.IsServerOwnedKey(pair.Key)) continue;
                if (pair.Value.Length > 2_000) continue;
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        private void ResolveEntitlementContext(
            out string companyPlan,
            out string activeModules,
            out string companyRole,
            out string subscriptionStatus)
        {
            companyPlan = "accounting_free";
            activeModules = string.Empty;
            companyRole = _tenantContext.Role?.ToString() ?? "Member";
            subscriptionStatus = "active";

            try
            {
                var subscription = _administrationService.GetCurrentSubscription();
                companyPlan = string.IsNullOrWhiteSpace(subscription.Plan)
                    ? "community"
                    : subscription.Plan;
                activeModules = string.Join(
                    ",",
                    subscription.ActiveModules ?? new List<string>());
                subscriptionStatus = string.IsNullOrWhiteSpace(subscription.SubscriptionStatus)
                    ? "active"
                    : subscription.SubscriptionStatus.Trim().ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not resolve subscription entitlement context for company {CompanyId}",
                    _tenantContext.CompanyId);
            }
        }

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
                using var _ = JsonDocument.Parse(text);
                return new ContentResult
                {
                    StatusCode = status,
                    Content = text,
                    ContentType = "application/json",
                };
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
