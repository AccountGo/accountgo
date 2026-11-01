using System;
using Api.Filters;
using Core.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Dashboard;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    public class DashboardController : BaseController
    {
        private readonly IDashboardService _dashboardService;
        private readonly IAccessAuthorizationService? _authorizationService;

        public DashboardController(
            IDashboardService dashboardService,
            IAccessAuthorizationService? authorizationService = null)
        {
            _dashboardService = dashboardService;
            _authorizationService = authorizationService;
        }

        [HttpGet]
        [Route("Summary")]
        [RequireOperation(AccessOperationCatalog.DashboardSummaryView)]
        public IActionResult Summary(
            DateTime? from = null,
            DateTime? to = null,
            int topReceivables = 5,
            int topPayables = 5,
            int recentActivity = 5)
        {
            if (_authorizationService != null)
            {
                var decision = _authorizationService.Authorize(AccessOperationCatalog.DashboardSummaryView);
                if (!decision.Allowed)
                {
                    return RequirePermissionAttribute.Denied(
                        decision.MissingPermission ?? Core.Domain.Security.SystemPermissions.ReportsView,
                        decision.PolicyId,
                        decision.PolicyVersion);
                }
            }

            var summary = _dashboardService.GetDashboardSummary(
                from,
                to,
                topReceivables,
                topPayables,
                recentActivity);

            return Ok(summary);
        }
    }
}
