// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Api.Controllers;

[TenantFree]
[ApiController]
[Route("api/auth/oauth")]
public sealed class CliOAuthController : ControllerBase
{
    private readonly ICliOAuthService _oauth;
    private readonly IConfiguration _configuration;

    public CliOAuthController(ICliOAuthService oauth, IConfiguration configuration)
    {
        _oauth = oauth;
        _configuration = configuration;
    }

    /// <summary>
    /// OAuth authorize for public CLI clients. Validates params, persists a pending request,
    /// then redirects the browser to the fixed SPA consent route with an opaque request_id.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("cli-oauth")]
    [HttpGet("authorize")]
    public IActionResult Authorize(
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? scope,
        [FromQuery] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(responseType)
                || string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(redirectUri)
                || string.IsNullOrWhiteSpace(scope)
                || string.IsNullOrWhiteSpace(state)
                || string.IsNullOrWhiteSpace(codeChallenge)
                || string.IsNullOrWhiteSpace(codeChallengeMethod))
            {
                return BadRequest(new { error = "invalid_request", error_description = "Missing OAuth authorization query parameters" });
            }

            var requestId = _oauth.CreatePendingAuthorization(
                responseType,
                clientId,
                redirectUri,
                scope,
                state,
                codeChallenge,
                codeChallengeMethod);

            var webBase = (_configuration["PUBLIC_WEB_URL"]
                ?? _configuration["Cors:AllowedOrigins:0"]
                ?? "http://localhost:5173").TrimEnd('/');
            var consentUrl = $"{webBase}/cli/authorize?request_id={Uri.EscapeDataString(requestId)}";
            return Redirect(consentUrl);
        }
        catch (OAuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Error, error_description = ex.Message });
        }
    }

    [Authorize]
    [EnableRateLimiting("cli-oauth")]
    [HttpGet("requests/{requestId}")]
    public async Task<IActionResult> GetRequest(string requestId, CancellationToken ct)
    {
        try
        {
            var dto = await _oauth.GetPendingRequestAsync(requestId, User, ct);
            return Ok(dto);
        }
        catch (OAuthException ex)
        {
            return ProblemFromOAuth(ex);
        }
    }

    public sealed record CodeDecisionRequest(string RequestId, string Decision, string? DeviceLabel = null);

    [Authorize]
    [EnableRateLimiting("cli-oauth")]
    [HttpPost("code")]
    public async Task<IActionResult> Decide([FromBody] CodeDecisionRequest body, CancellationToken ct)
    {
        try
        {
            if (body == null || string.IsNullOrWhiteSpace(body.RequestId) || string.IsNullOrWhiteSpace(body.Decision))
                return BadRequest(new { error = "invalid_request", error_description = "requestId and decision are required" });

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();
            var result = await _oauth.DecideAsync(body.RequestId, body.Decision, body.DeviceLabel, User, ip, ua, ct);
            return Ok(new { redirectUrl = result.RedirectUrl });
        }
        catch (OAuthException ex)
        {
            return ProblemFromOAuth(ex);
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("cli-oauth")]
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        try
        {
            var form = await ReadFormAsync();
            var grantType = form.GetValueOrDefault("grant_type");
            var clientId = form.GetValueOrDefault("client_id");
            if (string.IsNullOrWhiteSpace(grantType) || string.IsNullOrWhiteSpace(clientId))
                return BadRequest(new { error = "invalid_request", error_description = "grant_type and client_id are required" });

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();

            if (string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
            {
                var code = form.GetValueOrDefault("code");
                var redirectUri = form.GetValueOrDefault("redirect_uri");
                var verifier = form.GetValueOrDefault("code_verifier");
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(verifier))
                {
                    return BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "code, redirect_uri and code_verifier are required for authorization_code"
                    });
                }

                var token = await _oauth.ExchangeAuthorizationCodeAsync(clientId, code, redirectUri, verifier, ip, ua, ct);
                return Ok(token);
            }

            if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
            {
                var refresh = form.GetValueOrDefault("refresh_token");
                if (string.IsNullOrWhiteSpace(refresh))
                {
                    return BadRequest(new
                    {
                        error = "invalid_request",
                        error_description = "refresh_token is required for refresh_token grant"
                    });
                }

                var token = await _oauth.RefreshAsync(clientId, refresh, ip, ua, ct);
                return Ok(token);
            }

            return BadRequest(new
            {
                error = "unsupported_grant_type",
                error_description = $"Unsupported grant_type: {grantType}"
            });
        }
        catch (OAuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Error, error_description = ex.Message });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("cli-oauth")]
    [HttpPost("revoke")]
    [Consumes("application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var form = await ReadFormAsync();
        var token = form.GetValueOrDefault("token");
        if (!string.IsNullOrWhiteSpace(token))
            await _oauth.RevokeAsync(token, ct);

        // RFC 7009: always 200 for unknown tokens.
        return Ok(new { revoked = true });
    }

    private async Task<Dictionary<string, string>> ReadFormAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in form.Keys)
            {
                var value = form[key].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    result[key] = value.Trim();
            }
            return result;
        }

        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var json = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, System.Text.Json.JsonElement>>(Request.Body);
                if (json != null)
                {
                    foreach (var (key, el) in json)
                    {
                        if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                result[key] = s.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Treat as empty; token endpoint will return invalid_request.
            }
        }

        return result;
    }

    private IActionResult ProblemFromOAuth(OAuthException ex)
    {
        if (!string.IsNullOrEmpty(ex.ErrorCode))
        {
            return StatusCode(ex.StatusCode, new
            {
                error = ex.Error,
                error_description = ex.Message,
                code = ex.ErrorCode
            });
        }

        return StatusCode(ex.StatusCode, new { error = ex.Error, error_description = ex.Message });
    }
}
