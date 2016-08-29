using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class PurchasingController : BaseController
    {
        public PurchasingController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult PurchaseOrders()
        {
            ViewBag.PageContentHeader = "Purchase Orders";

            string purchaseOrders = GetAsync<object>("purchasing/purchaseorders")
                .Result
                .ToString();

            return View(model: purchaseOrders);
        }

        public IActionResult AddPurchaseOrder()
        {
            ViewBag.PageContentHeader = "Add Purchase Order";

            ViewBag.Vendors = Models.SelectListItemHelper.Vendors();

            return View();
        }

        public IActionResult PurchaseOrder(int purchId = 0)
        {
            ViewBag.PageContentHeader = "Purchase Order";

            var purchOrderDto = GetAsync<Dto.Purchasing.PurchaseOrder>("purchasing/purchaseorder?id=" + purchId).Result;

            ViewBag.Vendors = Models.SelectListItemHelper.Vendors();

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> PurchaseInvoices()
        {
            ViewBag.PageContentHeader = "Purchase Invoices";
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
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

        public IActionResult AddPurchaseInvoice(int purchId = 0)
        {
            ViewBag.PageContentHeader = "New Invoice";

            return View();
        }

        public IActionResult PurchaseInvoice(int id)
        {
            ViewBag.PageContentHeader = "Purchase Invoice";

            ViewBag.Vendors = Models.SelectListItemHelper.Vendors();

            return View();
        }

        public IActionResult AddPurchaseReceipt(int purchId = 0)
        {
            ViewBag.PageContentHeader = "New Receipt";

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> Vendors()
        {
            ViewBag.PageContentHeader = "Vendors";
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
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
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(vendorModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = PostAsync("purchasing/savevendor", content);

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

        public IActionResult Payment(int id)
        {
            ViewBag.PageContentHeader = "Make Payment";

            var invoice = GetAsync<Dto.Purchasing.PurchaseInvoice>("purchasing/purchaseinvoice?id=" + id).Result;

            var model = new Models.Purchasing.Payment()
            {
                InvoiceId = invoice.Id,
                InvoiceNo = invoice.No,
                VendorId = invoice.VendorId,
                VendorName = invoice.VendorName,
                InvoiceAmount = invoice.Amount,
                AmountPaid = invoice.AmountPaid,                
                Date = invoice.InvoiceDate
            };

            ViewBag.CashBanks = Models.SelectListItemHelper.CashBanks();

            return View(model);
        }
        
        [HttpPost]
        public IActionResult Payment(Models.Purchasing.Payment model)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                var response = Post("purchasing/savepayment", content);
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("purchaseinvoices");
            }
            ViewBag.PageContentHeader = "Make Payment";
            ViewBag.CashBanks = Models.SelectListItemHelper.CashBanks();
            return View(model);
        }
    }
}
