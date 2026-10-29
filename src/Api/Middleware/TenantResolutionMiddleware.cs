#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Api.Options;
using Api.Service.Ai;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Middleware
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        public const string HeaderName = "X-AccountGo-Org";
        public const string McpServerKeyHeaderName = "X-Mcp-Server-Key";
        public const string McpGateClaimType = "mcp_gate";

        public TenantResolutionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            ApiDbContext db)
        {
            // On /mcp, allow the configured Anter MCP gate credential to authenticate the
            // transport connection (needed for MCP initialize/tools/list catalog discovery
            // and as the primary auth alongside per-run X-Mcp-Caller-Assertion).
            // The McpGate principal carries no userId or tenant context of its own.
            if (context.Request.Path.StartsWithSegments("/mcp") &&
                (context.User?.Identity == null || !context.User.Identity.IsAuthenticated))
            {
                TryAuthenticateMcpGate(context);
            }

            // Per-run signed caller assertion from Anter AI (mcpPassThroughHeaders["X-Mcp-Caller-Assertion"])
            if (context.Request.Headers.TryGetValue(McpCallerAssertionService.HeaderName, out var assertionValues) &&
                !string.IsNullOrWhiteSpace(assertionValues.FirstOrDefault()))
            {
                await ResolveFromCallerAssertionAsync(
                    context,
                    tenantContext,
                    db,
                    assertionValues.First()!.Trim());
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var slugValues) ||
                string.IsNullOrWhiteSpace(slugValues.FirstOrDefault()))
            {
                await _next(context);
                return;
            }

            var slug = slugValues.First()!.Trim();

            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                await WriteUnauthorizedAsync(context, "Authentication is required to access organization resources.");
                return;
            }

            var userId = GetPrincipalUserId(context.User);
            if (string.IsNullOrEmpty(userId))
            {
                await WriteUnauthorizedAsync(context, "User identifier claim is missing.");
                return;
            }

            // Read on every request so revocations also take effect across concurrent requests.
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug);
            if (company == null)
            {
                await WriteNotFoundAsync(context);
                return;
            }

            if (!await TryPopulateTenantMembershipAsync(db, tenantContext, company, userId, slug))
            {
                await WriteNotFoundAsync(context);
                return;
            }

            await _next(context);
        }

        private async Task ResolveFromCallerAssertionAsync(
            HttpContext context,
            ITenantContext tenantContext,
            ApiDbContext db,
            string rawAssertion)
        {
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                await WriteUnauthorizedAsync(context, "Primary authentication is required alongside caller assertion.");
                return;
            }

            var assertionService = context.RequestServices.GetService<IMcpCallerAssertionService>();
            if (assertionService == null || !assertionService.TryValidateAssertion(rawAssertion, out var payload) || payload == null)
            {
                await WriteUnauthorizedAsync(context, "Invalid or expired MCP caller assertion.");
                return;
            }

            // If authenticated via a user JWT (rather than the shared McpGate key), the JWT user must match the assertion.
            var isMcpGate = string.Equals(
                context.User.FindFirstValue(McpGateClaimType),
                "true",
                StringComparison.Ordinal);

            if (!isMcpGate)
            {
                var jwtUserId = GetPrincipalUserId(context.User);
                if (string.IsNullOrEmpty(jwtUserId) ||
                    !string.Equals(jwtUserId, payload.UserId, StringComparison.Ordinal))
                {
                    await WriteUnauthorizedAsync(context, "Caller assertion user mismatch.");
                    return;
                }
            }
            else
            {
                // Verify the asserted user still exists and has not been soft-deleted.
                var userManager = context.RequestServices.GetService<UserManager<ApplicationUser>>();
                if (userManager != null)
                {
                    var appUser = await userManager.FindByIdAsync(payload.UserId);
                    if (appUser == null || appUser.DeletedAt != null)
                    {
                        await WriteUnauthorizedAsync(context, "Account deleted or not found.");
                        return;
                    }
                }
            }

            var company = await db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == payload.CompanyId && c.Slug == payload.CompanySlug);
            if (company == null)
            {
                await WriteNotFoundAsync(context);
                return;
            }

            if (!await TryPopulateTenantMembershipAsync(db, tenantContext, company, payload.UserId, payload.CompanySlug))
            {
                await WriteNotFoundAsync(context);
                return;
            }

            await _next(context);
        }

        private static async Task<bool> TryPopulateTenantMembershipAsync(
            ApiDbContext db,
            ITenantContext tenantContext,
            Core.Domain.Company company,
            string userId,
            string fallbackSlug)
        {
            var member = await db.CompanyMembers.AsNoTracking()
                .Include(m => m.SecurityRoles)
                .FirstOrDefaultAsync(m =>
                    m.CompanyId == company.Id &&
                    m.UserId == userId &&
                    m.Status == CompanyMemberStatus.Active);

            if (member == null)
            {
                return false;
            }

            HashSet<string> permissions;
            if (member.Role == CompanyMemberRole.Owner || member.Role == CompanyMemberRole.Admin)
            {
                var allPerms = await db.Set<SecurityPermission>().Select(p => p.Name).ToListAsync();
                permissions = new HashSet<string>(allPerms, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var roleIds = member.SecurityRoles.Select(r => r.SecurityRoleId).ToList();
                var memberPerms = await db.Set<SecurityRolePermission>()
                    .Where(srp => roleIds.Contains(srp.SecurityRoleId))
                    .Select(srp => srp.SecurityPermission.Name)
                    .Distinct()
                    .ToListAsync();
                permissions = new HashSet<string>(memberPerms, StringComparer.OrdinalIgnoreCase);
            }

            if (tenantContext is TenantContext tc)
            {
                tc.SetTenant(company.Id, company.Slug ?? fallbackSlug, member.Role, permissions);
            }

            return true;
        }

        public static bool TryCreateMcpGatePrincipal(
            HttpContext context,
            string authenticationType,
            out ClaimsPrincipal? principal)
        {
            principal = null;
            var anterOptions = context.RequestServices.GetService<IOptions<AnterOptions>>()?.Value;
            var expectedKey = anterOptions?.ApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return false;
            }

            string? candidate = null;
            if (context.Request.Headers.TryGetValue(McpServerKeyHeaderName, out var mcpKeyVals) &&
                !string.IsNullOrWhiteSpace(mcpKeyVals.FirstOrDefault()))
            {
                candidate = mcpKeyVals.First()!.Trim();
            }
            else if (context.Request.Headers.TryGetValue("Authorization", out var authVals) &&
                     !string.IsNullOrWhiteSpace(authVals.FirstOrDefault()))
            {
                var rawAuth = authVals.First()!.Trim();
                if (rawAuth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = rawAuth["Bearer ".Length..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var candidateBytes = Encoding.UTF8.GetBytes(candidate);
            if (expectedBytes.Length == candidateBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes))
            {
                var identity = new ClaimsIdentity(
                    new[] { new Claim(McpGateClaimType, "true") },
                    authenticationType: authenticationType);
                principal = new ClaimsPrincipal(identity);
                return true;
            }

            return false;
        }

        private static void TryAuthenticateMcpGate(HttpContext context)
        {
            if (TryCreateMcpGatePrincipal(context, "McpGate", out var principal) && principal != null)
            {
                context.User = principal;
            }
        }

        private static string? GetPrincipalUserId(ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity?.Name;

        private static async Task WriteUnauthorizedAsync(HttpContext context, string detail)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = detail
            };
            await context.Response.WriteAsJsonAsync(problem);
        }

        private static async Task WriteNotFoundAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Title = "Workspace not found",
                Status = StatusCodes.Status404NotFound,
                Detail = "The requested workspace was not found or you are not an active member."
            };
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
