using Microsoft.AspNet.Mvc;
using System.Collections.Generic;
using System.Linq;
using WebApp.Models.Sales;

namespace WebApp.Controllers
{
    public class SalesController : Controller
    {
        public IActionResult Quotations()
        {
            return View();
        }
        public IActionResult SalesOrders()
        {
            ViewBag.PageContentHeader = "Sales Orders";

            return View();
        }

        public IActionResult AddSalesOrder()
        {
            ViewBag.PageContentHeader = "Add Sales Order";

            return View();
        }

        [HttpPost]
        public IActionResult AddSalesOrder(SalesOrder model)
        {
            return Ok();
        }

        public IActionResult GetAddSalesOrderModel()
        {
            var model = new SalesOrder();

            model.Customers = new HashSet<Microsoft.AspNet.Mvc.Rendering.SelectListItem>();
            model.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "0", Text = "Choose customer..." });
            model.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "1", Text = "John Doe" });
            model.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "2", Text = "Joe Blogs" });
            model.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "3", Text = "Mary Walter" });

            model.Items = new HashSet<Microsoft.AspNet.Mvc.Rendering.SelectListItem>();
            model.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "0", Text = "Choose item..." });
            model.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "1", Text = "Mouse" });
            model.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "2", Text = "Keyboard" });
            model.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "3", Text = "Monitor" });

            return new ObjectResult(model);
        }
    }
}
