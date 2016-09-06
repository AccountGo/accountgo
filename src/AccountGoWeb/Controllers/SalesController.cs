using AccountGoWeb.Models;
using Dto.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class SalesController : BaseController
    {
        public SalesController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
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
                var baseUri = _baseConfig["ApiUrl"];
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
        
        public IActionResult SalesOrder(int id)
        {
            ViewBag.PageContentHeader = "Sales Order";
            return View();
        }

        public IActionResult SalesInvoice(int id)
        {
            ViewBag.PageContentHeader = "Sales Invoice";
            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesInvoices()
        {
            ViewBag.PageContentHeader = "Sales Invoices";
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
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
                var baseUri = _baseConfig["ApiUrl"];
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
            ViewBag.PageContentHeader = "New Receipt";

            var model = new Models.Sales.AddReceipt();

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.DebitAccounts = Models.SelectListItemHelper.CashBanks();
            ViewBag.CreditAccounts = Models.SelectListItemHelper.Accounts();
            ViewBag.CustomersDetail = Newtonsoft.Json.JsonConvert.SerializeObject(GetAsync<IEnumerable<Customer>>("sales/customers").Result);

            return View(model);
        }

        [HttpPost]
        public IActionResult AddReceipt(Models.Sales.AddReceipt model)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                var response = Post("sales/savereceipt", content);
                if(response.IsSuccessStatusCode)
                    return RedirectToAction("salesreceipts");                
            }

            ViewBag.PageContentHeader = "New Receipt";

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.DebitAccounts = Models.SelectListItemHelper.CashBanks();
            ViewBag.CreditAccounts = Models.SelectListItemHelper.Accounts();
            ViewBag.CustomersDetail = Newtonsoft.Json.JsonConvert.SerializeObject(GetAsync<IEnumerable<Customer>>("sales/customers").Result);

            return View(model);
        }


        public async System.Threading.Tasks.Task<IActionResult> Customers()
        {
            ViewBag.PageContentHeader = "Customers";
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
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
        
        public IActionResult Customer(int id = -1)
        {
            Customer customerModel = null;
            if (id == -1)
            {
                ViewBag.PageContentHeader = "New Customer";
                customerModel = new Customer();
                customerModel.No = new System.Random().Next(1, 99999).ToString(); // TODO: Replace with system generated numbering.
            }
            else
            {
                ViewBag.PageContentHeader = "Customer Card";
                customerModel = GetAsync<Customer>("sales/customer?id=" + id).Result;
            }

            ViewBag.Accounts = SelectListItemHelper.Accounts();
            ViewBag.TaxGroups = SelectListItemHelper.TaxGroups();
            ViewBag.PaymentTerms = SelectListItemHelper.PaymentTerms();

            return View(customerModel);
        }

        public IActionResult SaveCustomer(Customer customerModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(customerModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                var response = PostAsync("sales/savecustomer", content);

                return RedirectToAction("Customers");
            }
            else {
                ViewBag.Accounts = SelectListItemHelper.Accounts();
                ViewBag.TaxGroups = SelectListItemHelper.TaxGroups();
                ViewBag.PaymentTerms = SelectListItemHelper.PaymentTerms();
            }

            if(customerModel.Id == -1)
                ViewBag.PageContentHeader = "New Customer";
            else
                ViewBag.PageContentHeader = "Customer Card";

            return View("Customer", customerModel);
        }

        public IActionResult CustomerAllocations(int id)
        {
            ViewBag.PageContentHeader = "Customer Allocations";

            return View();
        }

        public IActionResult Allocate(int id)
        {
            ViewBag.PageContentHeader = "Receipt Allocation";

            var model = new Models.Sales.Allocate();

            var receipt = GetAsync<Dto.Sales.SalesReceipt>("sales/salesreceipt?id=" + id).Result;

            ViewBag.CustomerName = receipt.CustomerName;
            ViewBag.ReceiptNo = receipt.ReceiptNo;

            model.CustomerId = receipt.CustomerId;
            model.ReceiptId = receipt.Id;
            model.Date = receipt.ReceiptDate;
            model.Amount = receipt.Amount;
            model.RemainingAmountToAllocate = receipt.RemainingAmountToAllocate;

             var invoices = GetAsync<IEnumerable<Dto.Sales.SalesInvoice>>("sales/customerinvoices?id=" + receipt.CustomerId).Result;

            foreach (var invoice in invoices) {
                if (invoice.Posted && invoice.TotalAllocatedAmount < invoice.Amount)
                {
                    model.AllocationLines.Add(new Models.Sales.AllocationLine()
                    {
                        InvoiceId = invoice.Id,
                        Amount = invoice.Amount,
                        AllocatedAmount = invoice.TotalAllocatedAmount
                    });
                }
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult Allocate(Models.Sales.Allocate model)
        {
            if (ModelState.IsValid)
            {
                if (model.IsValid()) {
                    var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                    var content = new StringContent(serialize);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    var response = Post("sales/saveallocation", content);
                    if (response.IsSuccessStatusCode)
                        return RedirectToAction("salesreceipts");
                }
            }

            var receipt = GetAsync<Dto.Sales.SalesReceipt>("sales/salesreceipt?id=" + model.ReceiptId).Result;
            ViewBag.CustomerName = receipt.CustomerName;
            ViewBag.ReceiptNo = receipt.ReceiptNo;

            return View(model);
        }

        public IActionResult SalesInvoicePdf(int id)
        {
            var invoice = GetAsync<Dto.Sales.SalesInvoice>("sales/salesinvoiceforprinting?id=" + id).Result;
            SalesInvoice salesInvoiceModel = new SalesInvoice();
            salesInvoiceModel.ReferenceNo = invoice.ReferenceNo;
            salesInvoiceModel.No = invoice.No;
            salesInvoiceModel.InvoiceDate = invoice.InvoiceDate;
            salesInvoiceModel.CompanyName = invoice.CompanyName;

            salesInvoiceModel.TotalTax = invoice.TotalTax;
            salesInvoiceModel.TotalAmountAfterTax = invoice.TotalAmountAfterTax;
            salesInvoiceModel.CustomerName = invoice.CustomerName;
            salesInvoiceModel.SalesInvoiceLines = invoice.SalesInvoiceLines;
            return View(salesInvoiceModel);
        }
    }
}
