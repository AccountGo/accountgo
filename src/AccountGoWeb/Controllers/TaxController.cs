using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
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
