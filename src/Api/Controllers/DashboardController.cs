using Microsoft.AspNetCore.Mvc;
using Services.Dashboard;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class DashboardController : BaseController
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet]
        [Route("Summary")]
        public IActionResult Summary(
            DateTime? from = null,
            DateTime? to = null,
            int topReceivables = 5,
            int topPayables = 5,
            int recentActivity = 5)
        {
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
