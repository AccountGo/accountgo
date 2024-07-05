using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class HomeController : BaseController
    {
        public HomeController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }
        public IActionResult Index()
        {
            ViewBag.PageContentHeader = "Dashboard";
            ViewBag.ApiMontlySales = _baseConfig!["ApiUrl"] + "sales/getmonthlysales";
            return View();
        }
    }
}
