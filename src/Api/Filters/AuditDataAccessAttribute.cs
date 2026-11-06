// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Filters
{
    /// <summary>Writes a best-effort data-access security event after a successful response.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AuditDataAccessAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _eventType;
        private readonly string _name;

        public AuditDataAccessAttribute(string name, string eventType = "data.report.view")
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _eventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            if (executed.Exception != null || executed.Canceled)
                return;

            var statusCode = executed.Result switch
            {
                ObjectResult o => o.StatusCode ?? executed.HttpContext.Response.StatusCode,
                StatusCodeResult s => s.StatusCode,
                _ => executed.HttpContext.Response.StatusCode
            };
            if (statusCode >= 400)
                return;

            int? rowCount = executed.Result is ObjectResult objectResult
                ? CountRows(objectResult.Value)
                : null;
            await WriteAsync(context, rowCount);
        }

        private async Task WriteAsync(ActionExecutingContext context, int? rowCount)
        {
            try
            {
                var audit = context.HttpContext.RequestServices.GetService<ISecurityAuditService>();
                if (audit is null) return;
                var tenant = context.HttpContext.RequestServices.GetService<ITenantContext>();
                var companyId = tenant?.CompanyId;
                var userId = tenant?.UserId
                    ?? context.HttpContext.User.FindFirst("sub")?.Value
                    ?? context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var query = context.HttpContext.Request.Query
                    .Where(q => !SecurityAuditMetadata.IsForbidden(q.Key))
                    .ToDictionary(q => q.Key, q => q.Value.ToString());
                await audit.LogEventAsync(
                    _eventType,
                    "success",
                    userId,
                    companyId,
                    new { name = _name, query, rowCount });
            }
            catch
            {
                // Best effort.
            }
        }

        private static int? CountRows(object? value)
        {
            if (value is null) return null;
            if (value is ICollection collection) return collection.Count;
            if (value is IEnumerable enumerable and not string)
            {
                var count = 0;
                foreach (var _ in enumerable) count++;
                return count;
            }
            var type = value.GetType();
            foreach (var name in new[] { "Items", "items", "Data", "data", "Results", "results" })
            {
                var prop = type.GetProperty(name);
                if (prop?.GetValue(value) is { } nested)
                    return CountRows(nested);
            }
            return null;
        }
    }
}
