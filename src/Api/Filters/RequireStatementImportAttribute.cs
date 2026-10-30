// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Threading.Tasks;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Filters
{
    /// <summary>
    /// Statement import / match / lines (#436). Allows financials, the hosted
    /// <see cref="FeatureKeys.BankingStatementImport"/> key, or the community edition — never a plain
    /// <see cref="RequireFeatureAttribute"/> alone (community emits no feature keys).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RequireStatementImportAttribute : Attribute, IAsyncActionFilter
    {
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
            var method = context.HttpContext.Request.Method;
            var financialsAccess = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                ? ModuleAccess.Read
                : ModuleAccess.Write;
            var decision = StatementImportAccess.Evaluate(entitlements, companyId, financialsAccess);
            if (!decision.Allowed)
            {
                var support = entitlements.Options.SupportEmail;
                var problem = EntitlementProblemFactory.FromDecision(decision, tenantContext, support);
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
