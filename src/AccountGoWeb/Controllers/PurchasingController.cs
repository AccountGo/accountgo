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
            Models.SelectListItemHelper._config = _config;
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
        public IActionResult Vendor(int id = -1)
        {
            Dto.Purchasing.Vendor vendorModel = null;
            if (id == -1)
            {
                ViewBag.PageContentHeader = "New Vendor";
                vendorModel = new Dto.Purchasing.Vendor();
                vendorModel.No = new System.Random().Next(1, 99999).ToString(); // TODO: Replace with system generated numbering.
            }
            else
            {
                ViewBag.PageContentHeader = "Vendor Card";
                vendorModel = GetAsync<Dto.Purchasing.Vendor>("purchasing/vendor?id=" + id).Result;
            }

            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();

            return View(vendorModel);
        }

        public IActionResult SaveVendor(Dto.Purchasing.Vendor vendorModel)
        {
            if (ModelState.IsValid)
            {
                return RedirectToAction("Vendors");
            }
            else
            {
                ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
                ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
                ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            }

            if (vendorModel.Id == -1)
                ViewBag.PageContentHeader = "New Vendor";
            else
                ViewBag.PageContentHeader = "Vendor Card";

            return View("Vendor", vendorModel);
        }

        public IActionResult Payment(int invoiceId)
        {
            ViewBag.PageContentHeader = "Make Payment";
            var model = new Models.Purchasing.Payment();
            return View(model);
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
