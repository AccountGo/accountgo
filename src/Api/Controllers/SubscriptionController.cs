using System.Collections.Generic;
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

        public SubscriptionController(IAdministrationService adminService)
        {
            _adminService = adminService;
        }

        [AllowAnonymous]
        [HttpGet("companies")]
        public IActionResult GetCompanies()
        {
            var companies = _adminService.GetCompanies();
            return Ok(companies.Select(c => new
            {
                id = c.Id.ToString(),
                name = c.Name,
                shortName = c.ShortName,
                slug = string.IsNullOrWhiteSpace(c.Slug) ? (c.ShortName?.ToLowerInvariant() ?? $"org-{c.Id}") : c.Slug,
                plan = c.Plan ?? "accounting",
                subscriptionStatus = c.SubscriptionStatus ?? "active",
                activeModules = c.GetActiveModules(),
                companyCode = c.CompanyCode
            }));
        }

        [AllowAnonymous]
        [HttpGet("current")]
        public IActionResult GetCurrentSubscription([FromQuery] string? slug = null)
        {
            var subscription = _adminService.GetCurrentSubscription(slug);
            return Ok(subscription);
        }

        [Authorize]
        [HttpPut("update")]
        public IActionResult UpdateSubscription([FromBody] UpdateSubscriptionDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var updated = _adminService.UpdateSubscription(dto);
            return Ok(updated);
        }

        [AllowAnonymous]
        [HttpGet("plans")]
        public IActionResult GetPlans()
        {
            var plans = _adminService.GetPlanDefinitions();
            return Ok(plans);
        }

        [AllowAnonymous]
        [HttpGet("modules")]
        public IActionResult GetModules()
        {
            var modules = _adminService.GetModuleDefinitions();
            return Ok(modules);
        }
    }
}
