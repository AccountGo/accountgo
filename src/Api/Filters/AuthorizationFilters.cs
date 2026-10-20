using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.Administration;
using Services.Security;

namespace Api.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequireModuleAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _moduleKey;

        public RequireModuleAttribute(string moduleKey)
        {
            _moduleKey = moduleKey ?? throw new ArgumentNullException(nameof(moduleKey));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var adminService = context.HttpContext.RequestServices.GetRequiredService<IAdministrationService>();

            if (!adminService.IsModuleEnabled(_moduleKey))
            {
                var problem = new ProblemDetails
                {
                    Type = "https://accountgo.ph/errors/module-not-subscribed",
                    Title = "Module Not Subscribed",
                    Detail = $"The '{_moduleKey}' module is not included in this organization's active plan.",
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = "MODULE_NOT_SUBSCRIBED";
                problem.Extensions["requiredModule"] = _moduleKey;
                problem.Extensions["upgradeAvailable"] = true;

                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _permission;

        public RequirePermissionAttribute(string permission)
        {
            _permission = permission ?? throw new ArgumentNullException(nameof(permission));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var username = user.Identity.Name 
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var securityService = context.HttpContext.RequestServices.GetRequiredService<ISecurityService>();

            if (string.IsNullOrEmpty(username) || !securityService.CheckPermission(_permission, username))
            {
                var problem = new ProblemDetails
                {
                    Type = "https://accountgo.ph/errors/permission-denied",
                    Title = "Access Denied",
                    Detail = $"You do not have the required permission '{_permission}' to perform this action.",
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = "PERMISSION_DENIED";
                problem.Extensions["requiredPermission"] = _permission;

                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }
    }
}
