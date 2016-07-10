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
            ViewBag.PageContentHeader = "Sales Quotations";

            return View();
        }

        public IActionResult AddSalesQuotation()
        {
            ViewBag.PageContentHeader = "Add Sales Quotation";

            return View();
        }
    }
}
