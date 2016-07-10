using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class PurchasingController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult PurchaseOrders()
        {
            ViewBag.PageContentHeader = "Purchase Orders";

            return View();
        }

        public IActionResult AddPurchaseOrder()
        {
            ViewBag.PageContentHeader = "Add Purchase Order";

            return View();
        }

        public IActionResult PurchaseInvoices()
        {
            ViewBag.PageContentHeader = "Purchase Invoices";

            return View();
        }

        public IActionResult AddPurchaseInvoice()
        {
            ViewBag.PageContentHeader = "Add Purchase Invoice";

            return View();
        }
    }
}
