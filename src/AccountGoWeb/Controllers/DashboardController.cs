using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class DashboardController : BaseController
    {
        public DashboardController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }
        // GET: /<controller>/
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult MonthlySales()
        {
            ViewBag.ApiMontlySales = _baseConfig["ApiUrl"] + "sales/getmonthlysales";
            return View();
        }
    }
}
