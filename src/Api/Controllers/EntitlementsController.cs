// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Linq;
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
            return Ok(new
            {
                enabled = snapshot.Enabled,
                plan = snapshot.Plan,
                tier = snapshot.Tier,
                limited = snapshot.Limited,
                modules = snapshot.Modules,
                selfServicePlans = snapshot.SelfServicePlans,
                limits = snapshot.Usage.ToDictionary(
                    u => MeteredResources.Key(u.Resource),
                    u => new { limit = u.Limit, used = u.Used, period = u.Monthly ? "month" : null }),
                upgrade = new { available = false, contact = snapshot.SupportEmail }
            });
        }
    }
}
