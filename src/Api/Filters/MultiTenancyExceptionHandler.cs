#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Filters
{
    public class MultiTenancyExceptionHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is TenantEntityNotFoundException notFoundEx)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Title = "Referenced entity not found",
                    Detail = notFoundEx.Message,
                    Status = StatusCodes.Status400BadRequest
                };
                problem.Extensions["code"] = "CROSS_TENANT_REFERENCE_DENIED";
                problem.Extensions["entityType"] = notFoundEx.EntityType;
                problem.Extensions["entityId"] = notFoundEx.EntityId;
                await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
                return true;
            }

            if (exception is CrossTenantWriteException crossEx)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Title = "Cross-tenant write rejected",
                    Detail = crossEx.Message,
                    Status = StatusCodes.Status400BadRequest
                };
                problem.Extensions["code"] = "CROSS_TENANT_WRITE_DENIED";
                await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
                return true;
            }

            if (exception is TenantNotResolvedException unresEx)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Title = "Tenant not resolved",
                    Detail = unresEx.Message,
                    Status = StatusCodes.Status400BadRequest
                };
                problem.Extensions["code"] = "TENANT_NOT_RESOLVED";
                await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
                return true;
            }

            if (exception is EntitlementException entitlementEx)
            {
                var tenant = httpContext.RequestServices.GetService<ITenantContext>();
                var support = httpContext.RequestServices.GetService<EntitlementsOptions>()?.SupportEmail;
                var problem = EntitlementProblemFactory.FromException(entitlementEx, tenant, support);
                await WriteForbiddenAsync(httpContext, problem, cancellationToken);
                return true;
            }

            return false;
        }

        private static async Task WriteForbiddenAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
    }
}
