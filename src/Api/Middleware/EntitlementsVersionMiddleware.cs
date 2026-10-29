// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading.Tasks;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Middleware
{
    /// <summary>
    /// Emits <c>X-AccountGo-Entitlements-Version</c> only when the engine already resolved policy
    /// for the current company in this request (no extra Company load).
    /// </summary>
    public sealed class EntitlementsVersionMiddleware
    {
        public const string HeaderName = "X-AccountGo-Entitlements-Version";

        private readonly RequestDelegate _next;

        public EntitlementsVersionMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                TrySetHeader(context);
                return Task.CompletedTask;
            });
            await _next(context);
        }

        private static void TrySetHeader(HttpContext context)
        {
            if (context.Response.Headers.ContainsKey(HeaderName))
                return;

            var tenant = context.RequestServices.GetService<ITenantContext>();
            if (tenant == null || !tenant.CompanyId.HasValue)
                return;

            var entitlements = context.RequestServices.GetService<IEntitlementService>();
            if (entitlements == null)
                return;

            if (entitlements.TryGetResolvedPolicyVersion(tenant.CompanyId.Value, out var version)
                && !string.IsNullOrEmpty(version))
            {
                context.Response.Headers[HeaderName] = version;
            }
        }
    }
}
