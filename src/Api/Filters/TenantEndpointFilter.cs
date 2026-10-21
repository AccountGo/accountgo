#nullable enable
using System;
using System.Linq;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Filters
{
    public class TenantEndpointFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor descriptor)
            {
                var isScoped = IsTenantScoped(descriptor);
                if (isScoped)
                {
                    var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                    if (!tenantContext.IsResolved)
                    {
                        var problem = new ProblemDetails
                        {
                            Title = "Bad Request",
                            Status = StatusCodes.Status400BadRequest,
                            Detail = "X-AccountGo-Org header required"
                        };
                        context.Result = new ObjectResult(problem)
                        {
                            StatusCode = StatusCodes.Status400BadRequest
                        };
                        return;
                    }
                }
            }

            await next();
        }

        public static bool IsTenantScoped(ControllerActionDescriptor descriptor)
        {
            var method = descriptor.MethodInfo;
            var controller = descriptor.ControllerTypeInfo;

            var hasActionScoped = method.GetCustomAttributes(typeof(TenantScopedAttribute), true).Any();
            var hasActionFree = method.GetCustomAttributes(typeof(TenantFreeAttribute), true).Any();

            if (hasActionScoped && hasActionFree)
            {
                throw new InvalidOperationException($"Action '{method.DeclaringType?.Name}.{method.Name}' cannot have both [TenantScoped] and [TenantFree].");
            }

            if (hasActionScoped) return true;
            if (hasActionFree) return false;

            var hasControllerScoped = controller.GetCustomAttributes(typeof(TenantScopedAttribute), true).Any();
            var hasControllerFree = controller.GetCustomAttributes(typeof(TenantFreeAttribute), true).Any();

            if (hasControllerScoped && hasControllerFree)
            {
                throw new InvalidOperationException($"Controller '{controller.Name}' cannot have both [TenantScoped] and [TenantFree].");
            }

            if (hasControllerScoped) return true;
            if (hasControllerFree) return false;

            throw new InvalidOperationException($"Action '{method.DeclaringType?.Name}.{method.Name}' must have either [TenantScoped] or [TenantFree].");
        }
    }
}
