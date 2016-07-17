using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    public class SalesController : Controller
    {
        private readonly IConfiguration _config;

        public SalesController(IConfiguration config)
        {
            _config = config;
        }

        public IActionResult Index()
        {
            return RedirectToAction("SalesOrders");
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesOrders()
        {
            ViewBag.PageContentHeader = "Sales Orders";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/salesorders");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult AddSalesOrder()
        {
            ViewBag.PageContentHeader = "Add Sales Order";

            return View();
        }

        [HttpPost]
        public IActionResult AddSalesOrder(object Dto)
        {
            return Ok();
        }

        public IActionResult GetAddSalesOrderDto()
        {
            var Dto = new object();

            //Dto.Customers = new HashSet<Microsoft.AspNet.Mvc.Rendering.SelectListItem>();
            //Dto.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "0", Text = "Choose customer..." });
            //Dto.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "1", Text = "John Doe" });
            //Dto.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "2", Text = "Joe Blogs" });
            //Dto.Customers.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "3", Text = "Mary Walter" });

            //Dto.Items = new HashSet<Microsoft.AspNet.Mvc.Rendering.SelectListItem>();
            //Dto.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "0", Text = "Choose item..." });
            //Dto.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "1", Text = "Mouse" });
            //Dto.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "2", Text = "Keyboard" });
            //Dto.Items.Add(new Microsoft.AspNet.Mvc.Rendering.SelectListItem() { Value = "3", Text = "Monitor" });

            return new ObjectResult(Dto);
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesInvoices()
        {
            ViewBag.PageContentHeader = "Sales Invoices";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/salesinvoices");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult AddSalesInvoice()
        {
            ViewBag.PageContentHeader = "Add Sales Invoice";

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesReceipts()
        {
            ViewBag.PageContentHeader = "Sales Receipts";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/salesreceipts");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult AddSalesReceipt()
        {
            ViewBag.PageContentHeader = "Add Sales Receipt";

            return View();
        }
    }
}
