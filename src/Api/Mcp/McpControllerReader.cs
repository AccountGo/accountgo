#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Filters;
using Api.Mcp.Models;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Mcp
{
    /// <summary>The outcome of reading a GET endpoint for an assistant tool.</summary>
    public sealed class McpReadResult
    {
        public string Status { get; init; } = FinancialMcpStatus.Success;
        public string? Message { get; init; }
        public IReadOnlyList<string>? RequiredModules { get; init; }

        /// <summary>The value the endpoint returned (the same object the page receives as JSON).</summary>
        public object? Value { get; init; }

        public bool Succeeded => Status == FinancialMcpStatus.Success;
    }

    /// <summary>
    /// Lets assistant tools read exactly what a page reads. It runs one of the API's GET actions in-process, behind
    /// that action's own filters (tenant classification, module, permission, operation and statement-import guards),
    /// as the workspace member the request was resolved for. Tools then see the same records, totals and statuses as
    /// the screen, and the same access rules apply, without a second copy of either.
    /// <para>
    /// Only actions marked <see cref="HttpGetAttribute"/> can be read, so a tool can never reach a write through this
    /// path. A refusal from any filter comes back as <see cref="FinancialMcpStatus.AccessDenied"/> or
    /// <see cref="FinancialMcpStatus.PlanRestricted"/> instead of an error, so the assistant can explain it.
    /// </para>
    /// </summary>
    public sealed class McpControllerReader
    {
        private readonly IServiceProvider _services;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantContext _tenant;
        private readonly Newtonsoft.Json.JsonSerializerSettings _jsonSettings;

        public McpControllerReader(
            IServiceProvider services,
            IHttpContextAccessor httpContextAccessor,
            ITenantContext tenant,
            IOptions<MvcNewtonsoftJsonOptions>? jsonOptions = null)
        {
            _services = services;
            _httpContextAccessor = httpContextAccessor;
            _tenant = tenant;
            _jsonSettings = jsonOptions?.Value?.SerializerSettings ?? new Newtonsoft.Json.JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            };
        }

        /// <summary>Runs <paramref name="actionName"/> on <typeparamref name="TController"/> behind its filters.</summary>
        /// <param name="actionName">The C# method name of a GET action, e.g. <c>nameof(SalesController.SalesInvoice)</c>.</param>
        /// <param name="invoke">Calls the action on the controller instance with the tool's arguments.</param>
        /// <param name="arguments">The action arguments by parameter name, for filters that inspect them.</param>
        public async Task<McpReadResult> ReadAsync<TController>(
            string actionName,
            Func<TController, object> invoke,
            IDictionary<string, object?>? arguments = null)
            where TController : ControllerBase
        {
            if (!_tenant.IsResolved || _tenant.CompanyId == null || string.IsNullOrEmpty(_tenant.UserId))
            {
                throw new InvalidOperationException("Tenant context is required. Ensure the request carries a valid caller assertion or 'X-AccountGo-Org' header.");
            }

            var method = ResolveGetAction(typeof(TController), actionName);
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices ?? _services;

            // The controller sees the member the request was resolved for, never the shared MCP gate principal, so
            // actions that read the caller's id (linked entries, operating summary) answer for that member.
            var httpContext = new DefaultHttpContext { RequestServices = requestServices };
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, _tenant.UserId!),
                    new Claim("sub", _tenant.UserId!)
                },
                authenticationType: "McpCallerAssertion"));
            if (_httpContextAccessor.HttpContext is { } outer)
            {
                httpContext.TraceIdentifier = outer.TraceIdentifier;
                httpContext.RequestAborted = outer.RequestAborted;
            }

            var descriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = typeof(TController).GetTypeInfo(),
                MethodInfo = method,
                ActionName = actionName,
                ControllerName = typeof(TController).Name.EndsWith("Controller", StringComparison.Ordinal)
                    ? typeof(TController).Name[..^"Controller".Length]
                    : typeof(TController).Name,
                RouteValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            };
            descriptor.RouteValues["action"] = actionName;
            descriptor.RouteValues["controller"] = descriptor.ControllerName;

            var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
            var filters = CollectFilters(typeof(TController), method);
            var args = arguments ?? new Dictionary<string, object?>();

            var controller = ActivatorUtilities.CreateInstance<TController>(requestServices);
            try
            {
                controller.ControllerContext = new ControllerContext(actionContext);
                var executing = new ActionExecutingContext(actionContext, filters, args, controller);

                async Task<ActionExecutedContext> Run(int index)
                {
                    if (index == filters.Count)
                    {
                        var result = await InvokeAsync(controller, invoke);
                        return new ActionExecutedContext(actionContext, filters, controller) { Result = result };
                    }

                    switch (filters[index])
                    {
                        case IAsyncActionFilter asyncFilter:
                        {
                            ActionExecutedContext? executed = null;
                            await asyncFilter.OnActionExecutionAsync(executing, async () => executed = await Run(index + 1));
                            return executed ?? new ActionExecutedContext(actionContext, filters, controller)
                            {
                                Canceled = true,
                                Result = executing.Result
                            };
                        }
                        case IActionFilter syncFilter:
                        {
                            syncFilter.OnActionExecuting(executing);
                            if (executing.Result != null)
                            {
                                return new ActionExecutedContext(actionContext, filters, controller)
                                {
                                    Canceled = true,
                                    Result = executing.Result
                                };
                            }

                            var executed = await Run(index + 1);
                            syncFilter.OnActionExecuted(executed);
                            return executed;
                        }
                        default:
                            return await Run(index + 1);
                    }
                }

                var outcome = await Run(0);
                return ToReadResult(outcome.Result ?? executing.Result);
            }
            finally
            {
                (controller as IDisposable)?.Dispose();
            }
        }

        /// <summary>Serializes a value exactly as the API's JSON responses do (camelCase, Newtonsoft).</summary>
        public JsonElement ToJson(object? value)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(value, _jsonSettings);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static MethodInfo ResolveGetAction(Type controllerType, string actionName)
        {
            var candidates = controllerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == actionName && m.GetCustomAttributes<HttpGetAttribute>(inherit: true).Any())
                .ToList();
            if (candidates.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{controllerType.Name}.{actionName} must be exactly one GET action to be read by an assistant tool (found {candidates.Count}).");
            }

            return candidates[0];
        }

        /// <summary>
        /// The same filters MVC would run for this action: the global tenant-classification guard, then the
        /// controller's filter attributes, then the action's. Write-only global filters do not apply to a GET.
        /// </summary>
        private static List<IFilterMetadata> CollectFilters(Type controllerType, MethodInfo method)
        {
            var filters = new List<IFilterMetadata> { new TenantEndpointFilter() };
            filters.AddRange(controllerType.GetCustomAttributes(inherit: true).OfType<IFilterMetadata>()
                .Where(f => f is IAsyncActionFilter or IActionFilter));
            filters.AddRange(method.GetCustomAttributes(inherit: true).OfType<IFilterMetadata>()
                .Where(f => f is IAsyncActionFilter or IActionFilter));
            return filters;
        }

        private static async Task<IActionResult?> InvokeAsync<TController>(TController controller, Func<TController, object> invoke)
        {
            var returned = invoke(controller);
            return returned switch
            {
                Task<IActionResult> task => await task,
                Task<ActionResult> task => await task,
                IActionResult result => result,
                Task task => throw new InvalidOperationException($"Unsupported asynchronous action result {task.GetType().Name}."),
                _ => new OkObjectResult(returned)
            };
        }

        private static McpReadResult ToReadResult(IActionResult? result)
        {
            switch (result)
            {
                case null:
                    return new McpReadResult { Status = FinancialMcpStatus.Error, Message = "The request returned no result." };
                case ObjectResult objectResult:
                {
                    var status = objectResult.StatusCode ?? StatusCodes.Status200OK;
                    if (status is >= 200 and < 300)
                    {
                        if (objectResult.Value is Exception)
                            return new McpReadResult { Status = FinancialMcpStatus.Error, Message = "The record could not be loaded." };
                        return new McpReadResult { Value = objectResult.Value };
                    }

                    return Refusal(status, objectResult.Value);
                }
                case StatusCodeResult statusResult when statusResult.StatusCode is >= 200 and < 300:
                    return new McpReadResult();
                case StatusCodeResult statusResult:
                    return Refusal(statusResult.StatusCode, null);
                case ForbidResult or ChallengeResult:
                    return Refusal(StatusCodes.Status403Forbidden, null);
                default:
                    return new McpReadResult { Status = FinancialMcpStatus.Error, Message = "The request returned an unsupported result." };
            }
        }

        private static McpReadResult Refusal(int status, object? body)
        {
            var problem = body as ProblemDetails;
            var message = problem?.Detail ?? problem?.Title ?? MessageOf(body);

            if (status == StatusCodes.Status404NotFound)
                return new McpReadResult { Status = FinancialMcpStatus.NotFound, Message = message ?? "The record was not found in this workspace." };

            if (status == StatusCodes.Status400BadRequest)
                return new McpReadResult { Status = FinancialMcpStatus.InvalidRequest, Message = message ?? "The request was not valid." };

            if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                if (problem != null &&
                    (problem.Extensions.ContainsKey("requiredModule") || problem.Extensions.ContainsKey("requiredModules")))
                {
                    var modules = problem.Extensions.TryGetValue("requiredModules", out var many) && many is IEnumerable<string> list
                        ? list.ToList()
                        : problem.Extensions.TryGetValue("requiredModule", out var one) && one is string single && single.Length > 0
                            ? new List<string> { single }
                            : null;
                    return new McpReadResult
                    {
                        Status = FinancialMcpStatus.PlanRestricted,
                        Message = message ?? "This workspace's plan does not include this area. Modules are managed under Settings > Subscription.",
                        RequiredModules = modules
                    };
                }

                return new McpReadResult
                {
                    Status = FinancialMcpStatus.AccessDenied,
                    Message = message ?? "Access denied. You do not have the permission this page requires; ask a workspace admin."
                };
            }

            return new McpReadResult { Status = FinancialMcpStatus.Error, Message = message ?? $"The request failed with status {status}." };
        }

        private static string? MessageOf(object? body)
        {
            if (body == null) return null;
            var property = body.GetType().GetProperty("message") ?? body.GetType().GetProperty("Message");
            return property?.GetValue(body) as string;
        }
    }
}
