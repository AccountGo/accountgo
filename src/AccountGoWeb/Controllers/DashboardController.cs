using Dto.Dashboard;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class DashboardController : BaseController
    {
        private readonly HttpClient _httpClient;

        public DashboardController(IConfiguration config, HttpClient httpClient)
        {
            _baseConfig = config;
            _httpClient = httpClient;
            Models.SelectListItemHelper._config = config;
        }
        // GET: /<controller>/
        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> MonthlySales()
        {
            ViewBag.PageContentHeader = "Dashboard";

            DashboardSummaryDto model;

            try
            {
                model = await _httpClient.GetFromJsonAsync<DashboardSummaryDto>("dashboard/summary")
                    ?? new DashboardSummaryDto();
            }
            catch
            {
                model = new DashboardSummaryDto();
            }

            return View(model);
        }
    }
}
