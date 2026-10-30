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
    /// Emits <c>X-AccountGo-Entitlements-Version</c> and <c>X-AccountGo-Capacity</c> only when the engine already
    /// resolved policy for the current company in this request (no extra Company load).
    /// </summary>
    public sealed class EntitlementsVersionMiddleware
    {
        public const string HeaderName = "X-AccountGo-Entitlements-Version";

        /// <summary>
        /// <c>paid</c> (pro or comp tier) or <c>free</c>. The hosted Worker routes the workspace's later requests to
        /// the matching container pool.
        /// </summary>
        public const string CapacityHeaderName = "X-AccountGo-Capacity";

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
            var headers = context.Response.Headers;
            if (headers.ContainsKey(HeaderName) && headers.ContainsKey(CapacityHeaderName))
                return;

            var tenant = context.RequestServices.GetService<ITenantContext>();
            if (tenant == null || !tenant.CompanyId.HasValue)
                return;

            var entitlements = context.RequestServices.GetService<IEntitlementService>();
            if (entitlements == null)
                return;

            if (!headers.ContainsKey(HeaderName)
                && entitlements.TryGetResolvedPolicyVersion(tenant.CompanyId.Value, out var version)
                && !string.IsNullOrEmpty(version))
            {
                headers[HeaderName] = version;
            }

            if (!headers.ContainsKey(CapacityHeaderName)
                && entitlements.TryGetResolvedTier(tenant.CompanyId.Value, out var tier)
                && !string.IsNullOrEmpty(tier))
            {
                headers[CapacityHeaderName] = CapacityOf(tier);
            }
        }

        /// <summary>Capacity pool for a tier: paid plans and ops comp get the paid pool.</summary>
        public static string CapacityOf(string tier) =>
            string.Equals(tier, CompanyTiers.Pro, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tier, CompanyTiers.Comp, StringComparison.OrdinalIgnoreCase)
                ? "paid"
                : "free";
    }
}
