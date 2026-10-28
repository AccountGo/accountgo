using System;
using System.Threading.Tasks;
using Core.Billing;
using Core.Data;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Administration;
using Services.Entitlements;

namespace Api.Filters
{
    /// <summary>Requires the workspace's plan to include the module, or with several keys, any one of them.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequireModuleAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string[] _moduleKeys;

        public RequireModuleAttribute(params string[] moduleKeys)
        {
            if (moduleKeys == null || moduleKeys.Length == 0)
                throw new ArgumentNullException(nameof(moduleKeys));
            _moduleKeys = moduleKeys;
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

            // Optional: lightweight filter tests and non-tenant harnesses may not register IRepository<Company>.
            var companies = context.HttpContext.RequestServices.GetService<IRepository<Company>>();
            var company = (companies != null && tenantContext.CompanyId.HasValue)
                ? companies.GetById(tenantContext.CompanyId.Value)
                : null;

            bool allowed;
            string? denyCode = null;
            string? lapsedModule = null;
            if (company != null)
            {
                // One load: IsModuleAvailable already applies lifecycle + owner opt-outs.
                allowed = Array.Exists(_moduleKeys, m => SubscriptionAccess.IsModuleAvailable(company, m));
                // SUBSCRIPTION_INACTIVE only when an active subscription would allow a key; opted-out or
                // never-included modules stay MODULE_NOT_SUBSCRIBED because paying would not unlock them.
                if (!allowed && Company.IsPaidPlan(company.Plan) && SubscriptionAccess.IsInactive(company))
                    lapsedModule = Array.Find(_moduleKeys, company.HasModule);
                denyCode = !allowed
                    ? (lapsedModule != null ? "SUBSCRIPTION_INACTIVE" : "MODULE_NOT_SUBSCRIBED")
                    : null;

                var entitlements = context.HttpContext.RequestServices.GetService<IEntitlementService>();
                var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()
                    ?.CreateLogger(typeof(RequireModuleAttribute));
                EntitlementEngineShadow.CompareModuleGate(
                    entitlements, logger, company.Id, _moduleKeys, allowed, denyCode);
            }
            else
            {
                var adminService = context.HttpContext.RequestServices.GetRequiredService<IAdministrationService>();
                allowed = Array.Exists(_moduleKeys, adminService.IsModuleEnabled);
            }

            if (!allowed)
            {
                if (lapsedModule != null)
                {
                    var inactive = new ProblemDetails
                    {
                        Type = "https://accountgo.ph/errors/subscription-inactive",
                        Title = "Subscription Inactive",
                        Detail = "This workspace's subscription is inactive. Update billing to restore paid modules.",
                        Status = StatusCodes.Status403Forbidden
                    };
                    inactive.Extensions["code"] = "SUBSCRIPTION_INACTIVE";
                    inactive.Extensions["status"] = company!.SubscriptionStatus ?? "canceled";
                    inactive.Extensions["requiredModule"] = lapsedModule;
                    inactive.Extensions["upgradeAvailable"] = true;

                    context.Result = new ObjectResult(inactive)
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }

                var problem = new ProblemDetails
                {
                    Type = "https://accountgo.ph/errors/module-not-subscribed",
                    Title = "Module Not Subscribed",
                    Detail = _moduleKeys.Length == 1
                        ? $"The '{_moduleKeys[0]}' module is not included in this organization's active plan."
                        : $"None of the '{string.Join("', '", _moduleKeys)}' modules is included in this organization's active plan.",
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = "MODULE_NOT_SUBSCRIBED";
                problem.Extensions["requiredModule"] = _moduleKeys[0];
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
                context.Result = Denied(_permission);
                return;
            }

            await next();
        }

        /// <summary>The 403 for a missing permission; also for actions that need a permission only in some cases.</summary>
        public static ObjectResult Denied(string permission)
        {
            var problem = new ProblemDetails
            {
                Type = "https://accountgo.ph/errors/permission-denied",
                Title = "Access Denied",
                Detail = $"You do not have the required permission '{permission}' to perform this action.",
                Status = StatusCodes.Status403Forbidden
            };
            problem.Extensions["code"] = "PERMISSION_DENIED";
            problem.Extensions["requiredPermission"] = permission;

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
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
