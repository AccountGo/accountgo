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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

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
            IServiceProvider serviceProvider,
            IMemoryCache memoryCache)
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

            var cacheKey = $"tenant_resolution:{userId}:{slug.ToLowerInvariant()}";
            if (!memoryCache.TryGetValue(cacheKey, out CachedTenantInfo? tenantInfo) || tenantInfo == null)
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

                // 3. Exact match on Slug only
                var company = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug);
                if (company == null)
                {
                    await WriteNotFoundAsync(context);
                    return;
                }

                // 4. Look up CompanyMember for (CompanyId, UserId) with Status = Active
                var member = await db.CompanyMembers
                    .Include(m => m.SecurityRoles)
                    .FirstOrDefaultAsync(m => m.CompanyId == company.Id && m.UserId == userId && m.Status == CompanyMemberStatus.Active);

                if (member == null)
                {
                    // 5. Same 404 for company not found or not a member (no existence oracle)
                    await WriteNotFoundAsync(context);
                    return;
                }

                // 6. Role + permissions
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

                tenantInfo = new CachedTenantInfo(company.Id, company.Slug ?? slug, member.Role, permissions);
                memoryCache.Set(cacheKey, tenantInfo, TimeSpan.FromSeconds(60));
            }

            if (tenantContext is TenantContext tc)
            {
                tc.SetTenant(tenantInfo.CompanyId, tenantInfo.CompanySlug, tenantInfo.Role, tenantInfo.Permissions);
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

        public record CachedTenantInfo(
            int CompanyId,
            string CompanySlug,
            CompanyMemberRole Role,
            IReadOnlySet<string> Permissions);
    }
}
