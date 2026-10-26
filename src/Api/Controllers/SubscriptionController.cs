using System.Collections.Generic;
using System.Linq;
using Api.Filters;
using Core.Domain;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly Core.Entitlements.EntitlementsOptions _entitlements;

        public SubscriptionController(IAdministrationService adminService, Core.Entitlements.EntitlementsOptions entitlements)
        {
            _adminService = adminService;
            _entitlements = entitlements;
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
        public IActionResult UpdateSubscription([FromBody] UpdateSubscriptionDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var updated = _adminService.UpdateSubscription(dto);
                return Ok(updated);
            }
            catch (System.ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [TenantFree]
        [AllowAnonymous]
        [HttpGet("plans")]
        public IActionResult GetPlans()
        {
            var plans = _adminService.GetPlanDefinitions();
            foreach (var plan in plans)
                plan.SelfService = _entitlements.IsSelfServicePlan(plan.Key);
            return Ok(plans);
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
