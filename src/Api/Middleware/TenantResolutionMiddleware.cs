#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Api.Data;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Middleware
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        public const string HeaderName = "X-AccountGo-Org";

        public TenantResolutionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            ApiDbContext db)
        {
            if (!context.Request.Headers.TryGetValue(HeaderName, out var slugValues) ||
                string.IsNullOrWhiteSpace(slugValues.FirstOrDefault()))
            {
                await _next(context);
                return;
            }

            var slug = slugValues.First()!.Trim();

            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Title = "Unauthorized",
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = "Authentication is required to access organization resources."
                };
                await context.Response.WriteAsJsonAsync(problem);
                return;
            }

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value
                ?? context.User.Identity.Name;

            if (string.IsNullOrEmpty(userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Title = "Unauthorized",
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = "User identifier claim is missing."
                };
                await context.Response.WriteAsJsonAsync(problem);
                return;
            }

            // Read on every request so revocations also take effect across concurrent requests.
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug);
            if (company == null)
            {
                await WriteNotFoundAsync(context);
                return;
            }

            var member = await db.CompanyMembers.AsNoTracking()
                .Include(m => m.SecurityRoles)
                .FirstOrDefaultAsync(m => m.CompanyId == company.Id && m.UserId == userId && m.Status == CompanyMemberStatus.Active);
            if (member == null)
            {
                await WriteNotFoundAsync(context);
                return;
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
                tc.SetTenant(company.Id, company.Slug ?? slug, member.Role, permissions);
            }

            await _next(context);
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
