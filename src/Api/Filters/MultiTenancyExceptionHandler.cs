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

            if (exception is QuotaExceededException quotaEx)
            {
                var problem = new ProblemDetails
                {
                    Type = "https://accountgo.dev/errors/quota-exceeded",
                    Title = "Plan limit reached",
                    Detail = quotaEx.Message,
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = "QUOTA_EXCEEDED";
                problem.Extensions["resource"] = MeteredResources.Key(quotaEx.Resource);
                problem.Extensions["limit"] = quotaEx.Limit;
                problem.Extensions["used"] = quotaEx.Used;
                problem.Extensions["period"] = quotaEx.Monthly ? "month" : null;
                problem.Extensions["upgradeAvailable"] = false;
                problem.Extensions["contact"] = SupportEmail(httpContext);
                await WriteForbiddenAsync(httpContext, problem, cancellationToken);
                return true;
            }

            if (exception is PlanNotAvailableException planEx)
            {
                var problem = new ProblemDetails
                {
                    Type = "https://accountgo.dev/errors/plan-not-available",
                    Title = "Plan not available",
                    Detail = planEx.Message,
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = "PLAN_NOT_AVAILABLE";
                problem.Extensions["plan"] = planEx.Plan;
                problem.Extensions["contact"] = SupportEmail(httpContext);
                await WriteForbiddenAsync(httpContext, problem, cancellationToken);
                return true;
            }

            return false;
        }

        private static string? SupportEmail(HttpContext httpContext) =>
            httpContext.RequestServices.GetService<EntitlementsOptions>()?.SupportEmail;

        private static async Task WriteForbiddenAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
    }
}
