// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Auditing
{
    /// <summary>Best-effort <c>access.denied</c> logging, rate-limited to one event per user/operation/minute.</summary>
    public static class AccessDeniedAudit
    {
        public static void TryLog(
            HttpContext http,
            string? operation,
            string? permission,
            string? reasonCode)
        {
            try
            {
                var userId = http.RequestServices.GetService<ITenantContext>()?.UserId
                    ?? http.User?.FindFirst("sub")?.Value
                    ?? http.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? "anonymous";
                var opKey = string.IsNullOrWhiteSpace(operation) ? permission ?? "unknown" : operation;
                var cache = http.RequestServices.GetService<IMemoryCache>();
                if (cache != null)
                {
                    var cacheKey = $"access.denied:{userId}:{opKey}:{DateTime.UtcNow:yyyyMMddHHmm}";
                    var created = false;
                    cache.GetOrCreate(cacheKey, entry =>
                    {
                        created = true;
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                        return 1;
                    });
                    if (!created) return;
                }

                var audit = http.RequestServices.GetService<ISecurityAuditService>();
                if (audit is null) return;
                var companyId = http.RequestServices.GetService<ITenantContext>()?.CompanyId;
                _ = audit.LogEventAsync(
                    "access.denied",
                    "denied",
                    userId == "anonymous" ? null : userId,
                    companyId,
                    new { operation = opKey, permission, reason = reasonCode });
            }
            catch
            {
                // Best effort: never fail the deny response.
            }
        }
    }
}
