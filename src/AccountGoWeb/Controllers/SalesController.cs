using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
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

        public IActionResult AddReceipt()
        {
            ViewBag.PageContentHeader = "Add Receipt";

            var model = new Models.Sales.AddReceipt();

            var customers = GetAsync<IEnumerable<Dto.Sales.Customer>>("sales/customers").Result;
            
            ViewBag.Customers = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

            foreach (var customer in customers)
                ViewBag.Customers.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = customer.Id.ToString(), Text = customer.Name });

            var accounts = GetAsync<IEnumerable<Dto.Financial.Account>>("financials/accounts").Result;

            ViewBag.DebitAccounts = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

            foreach (var account in accounts.Where(a => a.IsCash == true))
                ViewBag.DebitAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = account.Id.ToString(), Text = account.AccountName });

            ViewBag.CreditAccounts = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();

            foreach (var account in accounts)
                ViewBag.CreditAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = account.Id.ToString(), Text = account.AccountName });

            return View(model);
        }

        [HttpPost]
        public IActionResult AddReceipt(Models.Sales.AddReceipt model)
        {
            if (ModelState.IsValid)
            { }

            return RedirectToAction("salesreceipts");
        }


        public async System.Threading.Tasks.Task<IActionResult> Customers()
        {
            ViewBag.PageContentHeader = "Customers";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/customers");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> Customer(int id)
        {
            ViewBag.PageContentHeader = "Customer Card";

            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/customer?id=" + id);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var model = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.Sales.Customer>(responseJson);
                    return View(model);
                }
            }
            return View();
        }

        #region Private methods
        public async System.Threading.Tasks.Task<T> GetAsync<T>(string uri)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + uri);
                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseJson);
        }
        #endregion
    }
}
