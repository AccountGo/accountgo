using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class QuotationsController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Quotations");
        }

        public IActionResult Quotations()
        {
            return View();
        }
    }
}
