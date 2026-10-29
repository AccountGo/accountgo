using System;
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Core.Billing;
using Core.Domain;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Billing;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IPlanCatalog _plans;
        private readonly IBillingProvider _billing;
        private readonly ITenantContext _tenant;

        public SubscriptionController(
            IAdministrationService adminService,
            IPlanCatalog plans,
            IBillingProvider billing,
            ITenantContext tenant)
        {
            _adminService = adminService;
            _plans = plans;
            _billing = billing;
            _tenant = tenant;
        }

        // GET /api/Subscription/companies was removed in Phase 2: it listed every workspace.
        // Use GET /api/organizations (caller's own memberships only).

        [TenantScoped]
        [Authorize]
        [HttpGet("current")]
        public IActionResult GetCurrentSubscription()
        {
            // Always the resolved workspace (X-AccountGo-Org); any ?slug= query value is ignored.
            var subscription = _adminService.GetCurrentSubscription();
            return Ok(subscription);
        }

        [TenantScoped]
        [Authorize]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpPut("update")]
        public async Task<IActionResult> UpdateSubscription([FromBody] UpdateSubscriptionDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                if (!string.IsNullOrWhiteSpace(dto.Plan) && _billing.RequiresProviderCheckout)
                {
                    var current = _adminService.GetCurrentSubscription();
                    var targetPlan = dto.Plan.Trim().ToLowerInvariant();
                    if (!string.Equals(current.Plan, targetPlan, StringComparison.OrdinalIgnoreCase))
                    {
                        var returnUrl = Request.Headers["Referer"].ToString();
                        if (string.IsNullOrWhiteSpace(returnUrl))
                            returnUrl = "/";
                        if (!string.IsNullOrWhiteSpace(dto.BillingInterval)
                            && !returnUrl.Contains("interval=", StringComparison.OrdinalIgnoreCase))
                        {
                            var sep = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                            returnUrl = $"{returnUrl}{sep}interval={Uri.EscapeDataString(dto.BillingInterval.Trim())}";
                        }

                        var result = await _billing.RequestPlanChangeAsync(
                            _tenant.RequireCompanyId(),
                            targetPlan,
                            returnUrl,
                            ct);
                        if (!string.IsNullOrEmpty(result.ErrorReason))
                            return BadRequest(new { message = result.ErrorReason });
                        if (!result.AppliedImmediately)
                            return Ok(new { redirectUrl = result.RedirectUrl });
                    }
                }

                var updated = _adminService.UpdateSubscription(dto);
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [TenantFree]
        [AllowAnonymous]
        [HttpGet("plans")]
        public IActionResult GetPlans()
        {
            return Ok(_plans.GetPlans());
        }

        [TenantFree]
        [AllowAnonymous]
        [HttpGet("modules")]
        public IActionResult GetModules()
        {
            var modules = _adminService.GetModuleDefinitions();
            return Ok(modules);
        }
    }
}
