using AccountGoWeb.Models;
using Dto.Sales;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class SalesController : GoodController
    {
        // private readonly IConfiguration _configuration;
        private readonly ILogger<SalesController> _logger;

        public SalesController(IConfiguration config, ILogger<SalesController> logger)
        {
            _configuration = config;
            Models.SelectListItemHelper._config = config;
            _logger = logger;
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
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
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
            SalesOrder salesOrderModel = new SalesOrder();
            salesOrderModel.SalesOrderLines = new List<SalesOrderLine> { new SalesOrderLine {
                Amount = 0,
                Discount = 0,
                ItemId = 1,
                Quantity = 1,
            } };
            salesOrderModel.No = new System.Random().Next(1, 99999).ToString();

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesOrderModel);
        }

        [HttpPost]
        public IActionResult AddSalesOrder(SalesOrder Dto, string addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                Dto.SalesOrderLines.Add(new SalesOrderLine
                {
                    Amount = 0,
                    Quantity = 1,
                    Discount = 0,
                    ItemId = 1,
                    MeasurementId = 1,
                });

                ViewBag.Customers = Models.SelectListItemHelper.Customers();
                ViewBag.Items = Models.SelectListItemHelper.Items();
                ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
                ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

                return View(Dto);
            }
            else if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(Dto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = Post("Sales/addsalesorder", content);
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("salesorders");
            }
            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return RedirectToAction("salesorders");
        }

        public IActionResult SalesOrder(int id)
        {
            ViewBag.PageContentHeader = "Sales Order";
            SalesOrder? salesOrderModel = null;
            if (id == -1)
            {
                ViewBag.PageContentHeader = "Add Sales Order";
                return View("AddSalesOrder");

            }
            else
            {
                salesOrderModel = GetAsync<SalesOrder>("Sales/SalesOrder?id=" + id).Result;
                ViewBag.CustomerName = salesOrderModel.CustomerName;
                ViewBag.OrderDate = salesOrderModel.OrderDate;
                ViewBag.SalesOrderLines = salesOrderModel.SalesOrderLines;
                ViewBag.TotalAmount = salesOrderModel.Amount;
            }

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesOrderModel);
        }

        public IActionResult SalesInvoice(int id)
        {
            ViewBag.PageContentHeader = "Sales Invoice";
            SalesInvoice? salesInvoiceModel = null;

            if (id == 0)
            {
                ViewBag.PageContentHeader = "Add Sales Invoice";
                return View("AddSalesInvoice");
            }
            else
            {
                salesInvoiceModel = GetAsync<SalesInvoice>("Sales/SalesInvoice?id=" + id).Result;
                ViewBag.Id = salesInvoiceModel.Id;
                ViewBag.CustomerName = salesInvoiceModel.CustomerName;
                ViewBag.InvoiceDate = salesInvoiceModel.InvoiceDate;
                ViewBag.SalesInvoiceLines = salesInvoiceModel.SalesInvoiceLines;
                ViewBag.TotalAmount = salesInvoiceModel.Amount;
            }

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View("SalesInvoice", salesInvoiceModel);
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesInvoices()
        {
            ViewBag.PageContentHeader = "Sales Invoices";
            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/salesinvoices");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }

                @ViewBag.Customers = Models.SelectListItemHelper.Customers();
                @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
                @ViewBag.Items = Models.SelectListItemHelper.Items();
                @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();
            }
            return View();
        }

        [HttpGet]
        public IActionResult AddSalesInvoice()
        {
            ViewBag.PageContentHeader = "Add Sales Invoice";

            SalesInvoice salesInvoiceModel = new SalesInvoice();
            salesInvoiceModel.SalesInvoiceLines = new List<SalesInvoiceLine> { new SalesInvoiceLine {
                Amount = 0,
                Discount = 0,
                ItemId = 1,
                Quantity = 1,
            } };
            salesInvoiceModel.No = new System.Random().Next(1, 99999).ToString(); // TODO: Replace with system generated numbering.

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesInvoiceModel);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> AddSalesInvoice(SalesInvoice Dto, string? addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                Dto.SalesInvoiceLines!.Add(new SalesInvoiceLine
                {
                    Amount = 0,
                    Quantity = 1,
                    Discount = 0,
                    ItemId = 1,
                    MeasurementId = 1,
                });

                ViewBag.Customers = Models.SelectListItemHelper.Customers();
                ViewBag.Items = Models.SelectListItemHelper.Items();
                ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
                ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

                return View(Dto);
            }
            else if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(Dto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                _logger.LogInformation("AddSalesInvoice: " + await content.ReadAsStringAsync());
                var response = Post("Sales/SaveSalesInvoice", content);
                _logger.LogInformation("AddSalesInvoice response: " + response.ToString());
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("salesinvoices");
            }

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> SalesReceipts()
        {
            ViewBag.PageContentHeader = "Sales Receipts";
            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
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

        [HttpGet]
        public IActionResult AddReceipt()
        {

            var model = new Models.Sales.AddReceipt();

            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                var response = Post("Sales/SaveReceipt", content);
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("salesreceipts");
            }

            ViewBag.PageContentHeader = "New Receipt";

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
                if (response.IsSuccessStatusCode)
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
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
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
            Customer? customerModel = null;
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

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> SaveSalesInvoice(SalesInvoice salesInvoiceModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(salesInvoiceModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                string ReadAsStringAsync = await content.ReadAsStringAsync();
                _logger.LogInformation("SaveSalesInvoice: " + ReadAsStringAsync);
                var response = Post("Sales/SaveSalesInvoice", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("SalesInvoices");
                }
            }
            ViewBag.Customers = SelectListItemHelper.Customers();
            ViewBag.PaymentTerms = SelectListItemHelper.PaymentTerms();
            ViewBag.Items = SelectListItemHelper.Items();
            ViewBag.Measurements = SelectListItemHelper.Measurements();

            return View("SalesInvoice", salesInvoiceModel);
        }

        public async Task<IActionResult> SaveCustomer(Customer customerModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(customerModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                string ReadAsStringAsync = await content.ReadAsStringAsync();
                var response = await PostAsync("Sales/SaveCustomer", content);
                return RedirectToAction("Customers");
            }
            else
            {
                ViewBag.Accounts = SelectListItemHelper.Accounts();
                ViewBag.TaxGroups = SelectListItemHelper.TaxGroups();
                ViewBag.PaymentTerms = SelectListItemHelper.PaymentTerms();
            }

            if (customerModel.Id == -1)
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

            foreach (var invoice in invoices)
            {
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
                if (model.IsValid())
                {
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
