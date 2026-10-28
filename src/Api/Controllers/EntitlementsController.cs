// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Middleware;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        public EntitlementsController(IEntitlementService entitlements, ITenantContext tenant)
        {
            _entitlements = entitlements;
            _tenant = tenant;
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
