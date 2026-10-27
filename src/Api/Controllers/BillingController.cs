// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Core.Billing;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>Workspace billing state and customer portal entry points.</summary>
    [ApiController]
    [Authorize]
    [TenantScoped]
    [Route("api/billing")]
    public class BillingController : BaseController
    {
        private readonly IBillingProvider _billing;
        private readonly ITenantContext _tenant;

        public BillingController(IBillingProvider billing, ITenantContext tenant)
        {
            _billing = billing;
            _tenant = tenant;
        }

        [HttpGet("state")]
        public async Task<IActionResult> GetState(CancellationToken ct)
        {
            var state = await _billing.GetBillingStateAsync(_tenant.RequireCompanyId(), ct);
            return Ok(new
            {
                billingEnabled = state.BillingEnabled,
                plan = state.Plan,
                tier = state.Tier,
                status = state.Status,
                currentPeriodEnd = state.CurrentPeriodEnd,
                cancelAtPeriodEnd = state.CancelAtPeriodEnd,
                limits = state.Limits,
                features = state.Features,
                portalAvailable = state.PortalAvailable
            });
        }

        [HttpPost("portal")]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        public async Task<IActionResult> OpenPortal([FromBody] BillingPortalRequest? body, CancellationToken ct)
        {
            var returnUrl = string.IsNullOrWhiteSpace(body?.ReturnUrl)
                ? "/"
                : body!.ReturnUrl!.Trim();
            var url = await _billing.GetPortalUrlAsync(_tenant.RequireCompanyId(), returnUrl, ct);
            if (string.IsNullOrEmpty(url))
                return BadRequest(new { message = "Billing portal is not available for this workspace." });
            return Ok(new { url });
        }
    }

    public sealed class BillingPortalRequest
    {
        public string? ReturnUrl { get; set; }
    }
}
