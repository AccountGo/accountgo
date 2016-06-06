using Microsoft.AspNet.Mvc;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace WebApp.Controllers
{
    public class TaxController : Controller
    {
        public IActionResult Taxes()
        {
            ViewBag.PageContentHeader = "Taxes";
            ViewBag.Desc = "Tax Rates";
            return View();
        }
    }
}
