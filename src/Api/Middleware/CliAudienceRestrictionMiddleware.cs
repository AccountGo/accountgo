// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Service;
using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

/// <summary>
/// Blocks <c>aud=cli</c> tokens from credential-minting and account-administration endpoints.
/// </summary>
public sealed class CliAudienceRestrictionMiddleware
{
    private static readonly string[] DeniedPrefixes =
    {
        "/api/auth/oauth/code",
        "/api/auth/oauth/requests",
        "/api/auth/change-password",
        "/api/auth/password",
        "/api/auth/mfa",
        "/api/auth/sessions",
        "/api/security/"
    };

    private readonly RequestDelegate _next;

    public CliAudienceRestrictionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true && IsCliAudience(context))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (DeniedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "access_denied",
                    error_description = "CLI tokens cannot access credential or session administration endpoints."
                });
                return;
            }

            // Require api scope on CLI tokens for all other authenticated API calls.
            var scope = context.User.FindFirst("scope")?.Value ?? string.Empty;
            var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!scopes.Contains("api", StringComparer.Ordinal)
                && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/auth/oauth/token", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/auth/oauth/revoke", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "insufficient_scope",
                    error_description = "CLI token is missing the api scope."
                });
                return;
            }
        }

        await _next(context);
    }

    private static bool IsCliAudience(HttpContext context)
    {
        return context.User.Claims.Any(c =>
            (c.Type is "aud" or "http://schemas.microsoft.com/identity/claims/audience")
            && string.Equals(c.Value, CliOAuthService.AudienceCli, StringComparison.OrdinalIgnoreCase));
    }
}
