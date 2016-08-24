using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace AccountGoWeb.Controllers
{
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
