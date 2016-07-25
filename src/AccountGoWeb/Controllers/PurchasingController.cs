using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    public class PurchasingController : Controller
    {
        private readonly IConfiguration _config;

        public PurchasingController(IConfiguration config)
        {
            _config = config;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> PurchaseOrders()
        {
            ViewBag.PageContentHeader = "Purchase Orders";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "purchasing/purchaseorders");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult AddPurchaseOrder()
        {
            ViewBag.PageContentHeader = "Add Purchase Order";

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> PurchaseInvoices()
        {
            ViewBag.PageContentHeader = "Purchase Invoices";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "purchasing/purchaseinvoices");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        public IActionResult AddPurchaseInvoice()
        {
            ViewBag.PageContentHeader = "Add Purchase Invoice";

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> Vendors()
        {
            ViewBag.PageContentHeader = "Vendors";
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "purchasing/vendors");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }
        public async System.Threading.Tasks.Task<IActionResult> Vendor(int id)
        {
            ViewBag.PageContentHeader = "Vendor Card";

            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "purchasing/vendor?id=" + id);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var model = Newtonsoft.Json.JsonConvert.DeserializeObject(responseJson);
                    return View(model);
                }

                return View();
            }
        }
    }
}
