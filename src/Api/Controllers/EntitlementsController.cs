// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Middleware;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Billing;

namespace Api.Controllers
{
    /// <summary>What the active workspace's plan includes and how much of it is used.</summary>
    [ApiController]
    [Authorize]
    [TenantScoped]
    [Route("api/entitlements")]
    public class EntitlementsController : BaseController
    {
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;
        private readonly IPlanCatalog _plans;

        public EntitlementsController(
            IEntitlementService entitlements,
            ITenantContext tenant,
            IPlanCatalog plans)
        {
            _entitlements = entitlements;
            _tenant = tenant;
            _plans = plans;
        }

        /// <summary>User-scoped free-workspace slot usage (no tenant header required).</summary>
        [TenantFree]
        [HttpGet("me")]
        public IActionResult GetMe()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var decision = _entitlements.EvaluateForUser(
                userId, new UserAction.CreateWorkspace(_plans.DefaultPlanKey));
            return Ok(new
            {
                freeWorkspace = new
                {
                    used = decision.Used ?? 0,
                    limit = decision.Limit
                }
            });
        }

        [HttpGet]
        public IActionResult Get()
        {
            var snapshot = _entitlements.Get(_tenant.RequireCompanyId());
            var payload = new
            {
                enabled = snapshot.Enabled,
                plan = snapshot.Plan,
                effectivePlan = snapshot.EffectivePlan,
                tier = snapshot.Tier,
                limited = snapshot.Limited,
                modules = snapshot.Modules,
                readOnlyModules = snapshot.ReadOnlyModules,
                features = snapshot.Features.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                lifecycle = snapshot.Lifecycle == null ? null : new
                {
                    status = snapshot.Lifecycle.Status,
                    graceEndsAt = snapshot.Lifecycle.GraceEndsAt,
                    cancelAtPeriodEnd = snapshot.Lifecycle.CancelAtPeriodEnd,
                    periodEnd = snapshot.Lifecycle.PeriodEnd,
                    reducedBy = snapshot.Lifecycle.ReducedBy
                },
                selfServicePlans = snapshot.SelfServicePlans,
                allowedTransitions = snapshot.AllowedTransitions,
                limits = snapshot.Usage.ToDictionary(
                    u => MeteredResources.Key(u.Resource),
                    u => new
                    {
                        limit = u.Limit,
                        used = u.Used,
                        period = u.Monthly ? "month" : null,
                        overLimit = u.OverLimit
                    }),
                version = snapshot.Version,
                upgrade = new { available = false, contact = snapshot.SupportEmail }
            };

            // ETag hashes the full payload (includes usage); Version header is policy-only.
            var etag = '"' + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))).ToLowerInvariant()[..16] + '"';
            Response.Headers.ETag = etag;
            if (!string.IsNullOrEmpty(snapshot.Version))
                Response.Headers[EntitlementsVersionMiddleware.HeaderName] = snapshot.Version;

            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && string.Equals(ifNoneMatch, etag, StringComparison.Ordinal))
                return StatusCode(304);

            return Ok(payload);
        }
    }
}
