#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.DocumentCaptures;
using Api.Service.Ai;
using Core.Domain;
using Core.Tenancy;
using Dto.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Services.Administration;
using Services.Billing;

namespace Api.Controllers
{
    /// <summary>
    /// Authenticated proxy for AccountGo Assistant → Anter agent-runner.
    /// Anter API keys never leave the server.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [TenantScoped]
    [EnableRateLimiting("ai-chat")]
    [Route("api/ai/chat")]
    [ApiController]
    public class AiChatController(
        IAnterAgentRunnerClient anterClient,
        IMcpCallerAssertionService assertionService,
        IAssistantPseudonymService pseudonymService,
        ITenantContext tenantContext,
        IAdministrationService administrationService,
        IPlanCatalog plans,
        ApiDbContext db,
        DocumentCaptureService captureService,
        ILogger<AiChatController> logger) : BaseController
    {
        private readonly IAnterAgentRunnerClient _anterClient = anterClient
            ?? throw new ArgumentNullException(nameof(anterClient));
        private readonly IMcpCallerAssertionService _assertionService = assertionService
            ?? throw new ArgumentNullException(nameof(assertionService));
        private readonly IAssistantPseudonymService _pseudonymService = pseudonymService
            ?? throw new ArgumentNullException(nameof(pseudonymService));
        private readonly ITenantContext _tenantContext = tenantContext
            ?? throw new ArgumentNullException(nameof(tenantContext));
        private readonly IAdministrationService _administrationService = administrationService
            ?? throw new ArgumentNullException(nameof(administrationService));
        private readonly IPlanCatalog _plans = plans
            ?? throw new ArgumentNullException(nameof(plans));
        private readonly ApiDbContext _db = db
            ?? throw new ArgumentNullException(nameof(db));
        private readonly DocumentCaptureService _captureService = captureService
            ?? throw new ArgumentNullException(nameof(captureService));
        private readonly ILogger<AiChatController> _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        public sealed class RunStreamRequest
        {
            public string? Message { get; set; }
            public string? SessionId { get; set; }
            public Dictionary<string, string>? ContextVariables { get; set; }
            [PublicId("cap")]
            public int? CaptureId { get; set; }
            public List<string>? AttachedFileIds { get; set; }
            [Newtonsoft.Json.JsonExtensionData]
            public Dictionary<string, object?>? AdditionalProperties { get; set; }
        }

        public sealed class ApprovalRequest
        {
            public string? Decision { get; set; }
            public string? Reason { get; set; }
        }

        public sealed class ResumeExecutionRequest
        {
            public string? SessionId { get; set; }
        }

        public sealed class ReidentifyRequest
        {
            public List<string>? Tokens { get; set; }
        }

        public const int MaxReidentifyTokens = 200;

        private string? CurrentUserId =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        private static bool HasMultipleAttachments(RunStreamRequest? body)
        {
            if (body is null)
                return false;
            var extraCount = body.AttachedFileIds?.Count(id => !string.IsNullOrWhiteSpace(id)) ?? 0;
            if (extraCount > 1)
                return true;
            if (body.CaptureId.HasValue && extraCount >= 1)
                return true;
            if (body.AdditionalProperties is not null
                && body.AdditionalProperties.TryGetValue("captureIds", out var rawIds)
                && rawIds is Newtonsoft.Json.Linq.JArray arr
                && arr.Count > 1)
            {
                return true;
            }
            return false;
        }

        [HttpPost("run-stream")]
        [RequestSizeLimit(AnterRunStreamBody.MaxRequestBodyBytes)]
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

            if (AssistantAttachmentMessageBuilder.ContainsForbiddenClientAttachmentData(body?.AdditionalProperties))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(
                    new { code = "DOCUMENT_ATTACHMENT_INVALID", detail = "Client-supplied attachment fields or bytes are not allowed." },
                    cancellationToken);
                return;
            }

            if (HasMultipleAttachments(body))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(
                    new { code = "DOCUMENT_ATTACHMENT_LIMIT", detail = "At most one document attachment can be sent per message." },
                    cancellationToken);
                return;
            }

            var companyId = _tenantContext.CompanyId.Value;
            if (body?.CaptureId is { } captureId)
            {
                var attached = await AssistantAttachmentMessageBuilder.TryAppendAttachmentAsync(
                    _db, companyId, userId, captureId, message, cancellationToken, _pseudonymService, body?.SessionId, _captureService);
                if (!attached.Succeeded)
                {
                    Response.StatusCode = attached.ErrorStatusCode;
                    await Response.WriteAsJsonAsync(attached.ErrorBody, cancellationToken);
                    return;
                }
                message = attached.CombinedMessage!;
            }

            var companySlug = _tenantContext.CompanySlug ?? $"company-{companyId}";
            var scopedUserId = AnterRunStreamBody.BuildScopedUserId(companyId, userId);
            var callerAssertion = _assertionService.MintAssertion(userId, companyId, companySlug, sessionId: body?.SessionId);
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

        [HttpPost("sessions/{sessionId}/reidentify")]
        public async Task<IActionResult> ReidentifySessionTokens(
            string sessionId,
            [FromBody] ReidentifyRequest? body,
            CancellationToken cancellationToken)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!_tenantContext.IsResolved || !_tenantContext.CompanyId.HasValue)
            {
                return BadRequest(new { detail = "Workspace context is required." });
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return BadRequest(new { detail = "sessionId is required." });
            }

            var requestedTokens = body?.Tokens;
            if (requestedTokens is { Count: > MaxReidentifyTokens })
            {
                return BadRequest(new { detail = $"At most {MaxReidentifyTokens} tokens may be re-identified per request." });
            }

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (requestedTokens is null || requestedTokens.Count == 0)
            {
                return Content(JsonSerializer.Serialize(new { names }), "application/json");
            }

            var companyId = _tenantContext.CompanyId.Value;
            var resolved = await _pseudonymService.ResolveTokensAsync(
                companyId,
                userId,
                sessionId,
                requestedTokens,
                cancellationToken);

            if (resolved.Count == 0)
            {
                return Content(JsonSerializer.Serialize(new { names }), "application/json");
            }

            // Synthetic entries (syn:...) have no backing workspace entity; return their stored DisplayText
            // directly since the row is already scoped to (companyId, userId, sessionId).
            foreach (var item in resolved.Values)
            {
                if (AssistantPseudonymKind.IsSyntheticPublicId(item.PublicId)
                    && !string.IsNullOrWhiteSpace(item.DisplayText))
                {
                    names[item.Token] = item.DisplayText;
                }
            }

            var permissions = _tenantContext.Permissions;

            if (permissions.Contains(Core.Domain.Security.SystemPermissions.SalesCustomersView))
            {
                var customerTokens = resolved.Values
                    .Where(r => string.Equals(r.Kind, AssistantPseudonymKind.Customer, StringComparison.Ordinal)
                        && r.PublicId.StartsWith("cus_", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (customerTokens.Count > 0)
                {
                    var ids = customerTokens.Select(t => t.PublicId).Distinct(StringComparer.Ordinal).ToList();
                    var lookup = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                        _db.Customers
                            .Where(c => ids.Contains(c.PublicId))
                            .Select(c => new { c.PublicId, Name = c.Party != null ? c.Party.Name : null }),
                        x => x.PublicId,
                        x => x.Name,
                        StringComparer.Ordinal,
                        cancellationToken);

                    foreach (var item in customerTokens)
                    {
                        if (lookup.TryGetValue(item.PublicId, out var name) && !string.IsNullOrWhiteSpace(name))
                        {
                            names[item.Token] = name;
                        }
                    }
                }
            }

            if (permissions.Contains(Core.Domain.Security.SystemPermissions.PurchasesVendorsView))
            {
                var vendorTokens = resolved.Values
                    .Where(r => string.Equals(r.Kind, AssistantPseudonymKind.Vendor, StringComparison.Ordinal)
                        && r.PublicId.StartsWith("ven_", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (vendorTokens.Count > 0)
                {
                    var ids = vendorTokens.Select(t => t.PublicId).Distinct(StringComparer.Ordinal).ToList();
                    var lookup = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                        _db.Vendors
                            .Where(v => ids.Contains(v.PublicId))
                            .Select(v => new { v.PublicId, Name = v.Party != null ? v.Party.Name : null }),
                        x => x.PublicId,
                        x => x.Name,
                        StringComparer.Ordinal,
                        cancellationToken);

                    foreach (var item in vendorTokens)
                    {
                        if (lookup.TryGetValue(item.PublicId, out var name) && !string.IsNullOrWhiteSpace(name))
                        {
                            names[item.Token] = name;
                        }
                    }
                }
            }

            if (permissions.Contains(Core.Domain.Security.SystemPermissions.BankingAccountsView))
            {
                var bankTokens = resolved.Values
                    .Where(r => string.Equals(r.Kind, AssistantPseudonymKind.BankAccount, StringComparison.Ordinal)
                        && r.PublicId.StartsWith("ba_", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (bankTokens.Count > 0)
                {
                    var ids = bankTokens.Select(t => t.PublicId).Distinct(StringComparer.Ordinal).ToList();
                    var lookup = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                        _db.Banks
                            .Where(b => ids.Contains(b.PublicId))
                            .Select(b => new { b.PublicId, Name = !string.IsNullOrEmpty(b.Name) ? b.Name : b.BankName }),
                        x => x.PublicId,
                        x => x.Name,
                        StringComparer.Ordinal,
                        cancellationToken);

                    foreach (var item in bankTokens)
                    {
                        if (lookup.TryGetValue(item.PublicId, out var name) && !string.IsNullOrWhiteSpace(name))
                        {
                            names[item.Token] = name;
                        }
                    }
                }
            }

            var canViewMembers = permissions.Contains(Core.Domain.Security.SystemPermissions.AdminMembersManage)
                || permissions.Contains(Core.Domain.Security.SystemPermissions.AuditLogsView);

            if (canViewMembers)
            {
                var personTokens = resolved.Values
                    .Where(r => string.Equals(r.Kind, AssistantPseudonymKind.Person, StringComparison.Ordinal)
                        && (r.PublicId.StartsWith("mem_", StringComparison.OrdinalIgnoreCase)
                            || r.PublicId.StartsWith("usr:", StringComparison.Ordinal)))
                    .ToList();
                if (personTokens.Count > 0)
                {
                    var memIds = personTokens
                        .Where(t => t.PublicId.StartsWith("mem_", StringComparison.OrdinalIgnoreCase))
                        .Select(t => t.PublicId)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    var usrIds = personTokens
                        .Where(t => t.PublicId.StartsWith("usr:", StringComparison.Ordinal))
                        .Select(t => t.PublicId["usr:".Length..])
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    var memberRows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        _db.CompanyMembers
                            .Where(m => m.CompanyId == companyId
                                && (memIds.Contains(m.PublicId) || usrIds.Contains(m.UserId)))
                            .Select(m => new { m.PublicId, m.UserId }),
                        cancellationToken);

                    var userManager = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                        .GetService<Microsoft.AspNetCore.Identity.UserManager<Api.Data.ApplicationUser>>(HttpContext.RequestServices);
                    var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (userManager is not null && memberRows.Count > 0)
                    {
                        var memberUserIds = memberRows.Select(m => m.UserId).Distinct(StringComparer.Ordinal).ToList();
                        var users = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                            userManager.Users
                                .Where(u => memberUserIds.Contains(u.Id) && u.DeletedAt == null)
                                .Select(u => new { u.Id, u.Email, u.UserName }),
                            u => u.Id,
                            StringComparer.Ordinal,
                            cancellationToken);

                        foreach (var m in memberRows)
                        {
                            if (!users.TryGetValue(m.UserId, out var u))
                                continue;
                            var display = !string.IsNullOrWhiteSpace(u.Email) ? u.Email : (u.UserName ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(display))
                            {
                                if (!string.IsNullOrWhiteSpace(m.PublicId))
                                    lookup[m.PublicId] = display;
                                lookup["usr:" + m.UserId] = display;
                            }
                        }
                    }

                    foreach (var item in personTokens)
                    {
                        if (lookup.TryGetValue(item.PublicId, out var name))
                        {
                            names[item.Token] = name;
                        }
                    }
                }
            }

            return Content(JsonSerializer.Serialize(new { names }), "application/json");
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
        public async Task ResumeExecution(
            string executionId,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResumeExecutionRequest? body,
            CancellationToken cancellationToken)
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
            var callerAssertion = _assertionService.MintAssertion(
                userId,
                companyId,
                companySlug,
                sessionId: body?.SessionId);

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
            companyPlan = _plans.DefaultPlanKey;
            activeModules = string.Empty;
            companyRole = _tenantContext.Role?.ToString() ?? "Member";
            subscriptionStatus = "active";

            try
            {
                var subscription = _administrationService.GetCurrentSubscription();
                companyPlan = string.IsNullOrWhiteSpace(subscription.Plan)
                    ? _plans.DefaultPlanKey
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
