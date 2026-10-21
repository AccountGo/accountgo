using System;
using System.Threading.Tasks;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.Administration;

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
            var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenantContext.IsResolved)
            {
                var tenantProblem = new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = "X-AccountGo-Org header required",
                    Status = StatusCodes.Status400BadRequest
                };
                context.Result = new ObjectResult(tenantProblem)
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
                return;
            }

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

            var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenantContext.IsResolved || !tenantContext.Permissions.Contains(_permission))
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

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequireWorkspaceRoleAttribute : Attribute, IAsyncActionFilter
    {
        private readonly CompanyMemberRole _minRole;

        public RequireWorkspaceRoleAttribute(CompanyMemberRole minRole)
        {
            _minRole = minRole;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenantContext.IsResolved || !tenantContext.Role.HasValue)
            {
                var problem = new ProblemDetails
                {
                    Title = "Workspace not found",
                    Status = StatusCodes.Status404NotFound,
                    Detail = "The requested workspace was not found or you are not an active member."
                };
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status404NotFound
                };
                return;
            }

            var currentRole = tenantContext.Role.Value;
            bool allowed = false;

            if (_minRole == CompanyMemberRole.Owner)
            {
                allowed = currentRole == CompanyMemberRole.Owner;
            }
            else if (_minRole == CompanyMemberRole.Admin)
            {
                // Owner satisfies Admin
                allowed = currentRole == CompanyMemberRole.Owner || currentRole == CompanyMemberRole.Admin;
            }
            else if (_minRole == CompanyMemberRole.Member)
            {
                allowed = true;
            }

            if (!allowed)
            {
                var problem = new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = $"This operation requires the {_minRole} role or higher in this workspace.",
                    Status = StatusCodes.Status403Forbidden
                };
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
