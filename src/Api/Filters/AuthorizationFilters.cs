using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Api.Authorization;
using Api.Data;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Filters
{
    public static class AccessControlHttpContextItemKeys
    {
        public const string ResourceContext = "__AccessResourceContext";
    }
    /// <summary>Requires the workspace's plan to include the module, or with several keys, any one of them.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequireModuleAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string[] _moduleKeys;

        public IReadOnlyList<string> ModuleKeys => _moduleKeys;

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

            var companyId = tenantContext.RequireCompanyId();
            var entitlements = context.HttpContext.RequestServices.GetRequiredService<IEntitlementService>();
            var access = ResolveAccess(context);
            var decision = entitlements.Evaluate(companyId, new EntitlementAction.Module(access, _moduleKeys));

            if (!decision.Allowed)
            {
                var problem = EntitlementProblemFactory.FromDecision(
                    decision,
                    tenantContext,
                    entitlements.Options.SupportEmail,
                    requiredModules: _moduleKeys);
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }

        private static ModuleAccess ResolveAccess(ActionExecutingContext context)
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
                return ModuleAccess.Read;
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<AllowWhenReadOnlyAttribute>() != null)
                return ModuleAccess.Read;
            return ModuleAccess.Write;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _permission;

        public string Permission => _permission;

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
            var entitlements = context.HttpContext.RequestServices.GetService<IEntitlementService>();
            var db = context.HttpContext.RequestServices.GetService<ApiDbContext>();
            if (context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName) &&
                string.Equals(actionName, "PostJournalEntry", StringComparison.Ordinal) && tenantContext.CompanyId.HasValue)
            {
                var dto = context.ActionArguments.Values.OfType<Dto.Financial.JournalEntry>().FirstOrDefault();
                var linkedEntries = context.HttpContext.RequestServices.GetService<Api.Service.LinkedEntryService>();
                if (dto != null && dto.Id > 0 && linkedEntries != null)
                {
                    var code = linkedEntries.IsMirrorJournal(tenantContext.CompanyId.Value, dto.Id)
                        ? "linked_entry_use_pair_reversal"
                        : linkedEntries.GetSourcePairState(tenantContext.CompanyId.Value, dto.Id) is { } state && state != "PendingSource"
                            ? "linked_entry_not_pending" : null;
                    if (code != null)
                    {
                        var conflict = new ProblemDetails { Title = code, Detail = "Linked journals must be posted through their linked entry pair.", Status = StatusCodes.Status409Conflict };
                        conflict.Extensions["code"] = code;
                        context.Result = new ObjectResult(conflict) { StatusCode = StatusCodes.Status409Conflict };
                        return;
                    }
                }
            }
            var httpContextAccessor = context.HttpContext.RequestServices.GetService<IHttpContextAccessor>()
                ?? new HttpContextAccessor { HttpContext = context.HttpContext };
            var authService = context.HttpContext.RequestServices.GetService<IAccessAuthorizationService>()
                ?? new AccessAuthorizationService(tenantContext, entitlements, db, httpContextAccessor);

            var resourceContext = ResolveActionResourceContext(context, tenantContext, db, _permission);

            var decision = authService.AuthorizeRequirement(
                requiredPermissionsAll: new[] { _permission },
                resource: resourceContext,
                operationKey: _permission);

            if (!decision.Allowed)
            {
                if (decision.ReasonCode == AccessReasonCode.PermissionDenied)
                {
                    context.Result = Denied(decision.MissingPermission ?? _permission, decision.PolicyId, decision.PolicyVersion);
                    return;
                }

                var problem = new ProblemDetails
                {
                    Title = decision.SafeTitle,
                    Detail = decision.SafeMessage,
                    Status = decision.HttpStatus
                };
                problem.Extensions["code"] = decision.ReasonCodeString;
                problem.Extensions["policyId"] = decision.PolicyId;
                problem.Extensions["policyVersion"] = decision.PolicyVersion;
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = decision.HttpStatus
                };
                return;
            }

            await next();
        }

        internal static AccessResourceContext? ResolveActionResourceContext(
            ActionExecutingContext context,
            ITenantContext tenantContext,
            ApiDbContext? db,
            string operationKey)
        {
            if (context.HttpContext.Items.TryGetValue(AccessControlHttpContextItemKeys.ResourceContext, out var resObj) &&
                resObj is AccessResourceContext explicitContext)
            {
                return explicitContext;
            }

            if (string.Equals(operationKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal) &&
                tenantContext.CompanyId.HasValue)
            {
                var companyId = tenantContext.CompanyId.Value;
                var dto = context.ActionArguments.Values.OfType<Dto.Financial.JournalEntry>().FirstOrDefault();
                string? persistedPreparer = null;
                if (db != null && dto != null && dto.Id > 0)
                {
                    persistedPreparer = db.AuditLogs
                        .AsNoTracking()
                        .Where(a => a.CompanyId == companyId &&
                                    a.TableName == "__JournalEntryPreparer" &&
                                    a.RecordId == dto.Id.ToString())
                        .OrderByDescending(a => a.Id)
                        .Select(a => a.NewValue)
                        .FirstOrDefault();
                }

                var currentActorId = tenantContext.UserId;
                var effectivePreparer = persistedPreparer ?? currentActorId ?? "system:draft-preparer";
                var aprHeader = context.HttpContext.Request.Headers["X-Approval-Request-Id"].FirstOrDefault();
                var approverHeader = context.HttpContext.Request.Headers["X-Approved-By-User-Id"].FirstOrDefault();

                if (db != null && dto != null && dto.Id > 0)
                {
                    var header = db.JournalEntryHeaders
                        .AsNoTracking()
                        .FirstOrDefault(h => h.Id == dto.Id);

                    if (header == null)
                    {
                        return new AccessResourceContext
                        {
                            WorkspaceCompanyId = companyId,
                            ResourceCompanyId = -1,
                            ResourceType = "JournalEntry",
                            ResourceId = dto.Id.ToString(CultureInfo.InvariantCulture),
                            WorkflowState = "Draft",
                            IsPeriodLockedOrClosed = false,
                            PreparedByUserId = effectivePreparer,
                            IndependentApproverUserId = approverHeader,
                            ApprovalRequestId = aprHeader
                        };
                    }

                    var isPosted = header.Posted == true || header.GeneralLedgerHeaderId.HasValue;
                    return new AccessResourceContext
                    {
                        WorkspaceCompanyId = companyId,
                        ResourceCompanyId = companyId,
                        ResourceType = "JournalEntry",
                        ResourceId = dto.Id.ToString(CultureInfo.InvariantCulture),
                        WorkflowState = isPosted ? "Posted" : "Draft",
                        IsPeriodLockedOrClosed = false,
                        PreparedByUserId = effectivePreparer,
                        IndependentApproverUserId = approverHeader,
                        ApprovalRequestId = aprHeader
                    };
                }

                return new AccessResourceContext
                {
                    WorkspaceCompanyId = companyId,
                    ResourceCompanyId = companyId,
                    ResourceType = "JournalEntry",
                    WorkflowState = "Draft",
                    IsPeriodLockedOrClosed = false,
                    PreparedByUserId = effectivePreparer,
                    IndependentApproverUserId = approverHeader,
                    ApprovalRequestId = aprHeader
                };
            }

            return null;
        }

        /// <summary>The 403 for a missing permission; also for actions that need a permission only in some cases.</summary>
        public static ObjectResult Denied(string permission, string? policyId = null, string? policyVersion = null)
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
            if (!string.IsNullOrWhiteSpace(policyId))
                problem.Extensions["policyId"] = policyId;
            if (!string.IsNullOrWhiteSpace(policyVersion))
                problem.Extensions["policyVersion"] = policyVersion;

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

        public CompanyMemberRole MinimumRole => _minRole;

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
            var authService = context.HttpContext.RequestServices.GetService<IAccessAuthorizationService>()
                ?? new AccessAuthorizationService(tenantContext);

            var allowedRoles = _minRole switch
            {
                CompanyMemberRole.Owner => new[] { CompanyMemberRole.Owner },
                CompanyMemberRole.Admin => new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin },
                _ => new[] { CompanyMemberRole.Owner, CompanyMemberRole.Admin, CompanyMemberRole.Member }
            };

            var decision = authService.AuthorizeRequirement(allowedGovernanceRoles: allowedRoles);
            if (!decision.Allowed)
            {
                if (decision.ReasonCode is AccessReasonCode.TenantNotResolved or AccessReasonCode.MembershipInactiveOrMissing)
                {
                    var notFoundProblem = new ProblemDetails
                    {
                        Title = "Workspace not found",
                        Status = StatusCodes.Status404NotFound,
                        Detail = "The requested workspace was not found or you are not an active member."
                    };
                    context.Result = new ObjectResult(notFoundProblem)
                    {
                        StatusCode = StatusCodes.Status404NotFound
                    };
                    return;
                }

                var problem = new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = $"This operation requires the {_minRole} role or higher in this workspace.",
                    Status = StatusCodes.Status403Forbidden
                };
                problem.Extensions["code"] = decision.ReasonCodeString;
                problem.Extensions["policyId"] = decision.PolicyId;
                problem.Extensions["policyVersion"] = decision.PolicyVersion;
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }
    }

    /// <summary>
    /// Enforces a cataloged operation policy from <see cref="AccessOperationCatalog"/> via <see cref="IAccessAuthorizationService"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequireOperationAttribute : Attribute, IAsyncActionFilter
    {
        public string OperationKey { get; }

        public RequireOperationAttribute(string operationKey)
        {
            OperationKey = operationKey ?? throw new ArgumentNullException(nameof(operationKey));
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
            var entitlements = context.HttpContext.RequestServices.GetService<IEntitlementService>();
            var db = context.HttpContext.RequestServices.GetService<ApiDbContext>();
            var httpContextAccessor = context.HttpContext.RequestServices.GetService<IHttpContextAccessor>()
                ?? new HttpContextAccessor { HttpContext = context.HttpContext };
            var authService = context.HttpContext.RequestServices.GetService<IAccessAuthorizationService>()
                ?? new AccessAuthorizationService(tenantContext, entitlements, db, httpContextAccessor);

            var resourceContext = RequirePermissionAttribute.ResolveActionResourceContext(context, tenantContext, db, OperationKey);
            var decision = await authService.AuthorizeAsync(OperationKey, resourceContext, cancellationToken: context.HttpContext.RequestAborted);
            if (!decision.Allowed)
            {
                if (decision.ReasonCode == AccessReasonCode.PermissionDenied && !string.IsNullOrWhiteSpace(decision.MissingPermission))
                {
                    context.Result = RequirePermissionAttribute.Denied(decision.MissingPermission!, decision.PolicyId, decision.PolicyVersion);
                    return;
                }

                if (decision.ReasonCode == AccessReasonCode.EntitlementDenied && decision.EntitlementDecision != null)
                {
                    entitlements ??= context.HttpContext.RequestServices.GetService<IEntitlementService>();
                    var entProblem = EntitlementProblemFactory.FromDecision(
                        decision.EntitlementDecision,
                        tenantContext,
                        entitlements?.Options.SupportEmail,
                        requiredModules: decision.RequiredModules);
                    context.Result = new ObjectResult(entProblem)
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }

                var problem = new ProblemDetails
                {
                    Title = decision.SafeTitle,
                    Detail = decision.SafeMessage,
                    Status = decision.HttpStatus
                };
                problem.Extensions["code"] = decision.ReasonCodeString;
                problem.Extensions["policyId"] = decision.PolicyId;
                problem.Extensions["policyVersion"] = decision.PolicyVersion;
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = decision.HttpStatus
                };
                return;
            }

            await next();
        }
    }
}
