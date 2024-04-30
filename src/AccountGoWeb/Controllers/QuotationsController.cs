using Dto.Sales;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class QuotationsController : GoodController
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<QuotationsController> _logger;
        public QuotationsController(IConfiguration config, ILogger<QuotationsController> logger)
        {
            _configuration = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return RedirectToAction("quotations");
        }

        public async System.Threading.Tasks.Task<IActionResult> Quotations()
        {
            ViewBag.PageContentHeader = "Quotations";

            using (var client = new HttpClient())
            {
                var baseUri = _configuration["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/quotations");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        [HttpGet]
        public IActionResult AddSalesQuotation()
        {
            ViewBag.PageContentHeader = "Add Sales Quotation";

            SalesQuotation model = new SalesQuotation();
            model.SalesQuotationLines = new List<SalesQuotationLine> { new SalesQuotationLine {
                Amount = 0,
                Quantity = 1,
                Discount = 0,
                ItemId = 1,
                MeasurementId = 1,
            } };
            model.No = new System.Random().Next(1, 99999).ToString(); // TODO: Replace with system generated numbering.

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddSalesQuotation(Dto.Sales.SalesQuotation model, string addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                _logger.LogInformation("Add Row Button Clicked");
                model.SalesQuotationLines.Add(new SalesQuotationLine
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

                return View(model);
            }
            else if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                _logger.LogInformation("Quotation ID is is : " + model.Id);
                using (var client = new HttpClient())
                {
                    var baseUri = _configuration["ApiUrl"];
                    client.BaseAddress = new Uri(baseUri!);
                    var response = await client.PostAsync("sales/savequotation", content);

                    if (response.IsSuccessStatusCode)
                        return RedirectToAction("quotations");
                }
            }

            return View();
        }

        [HttpGet]
        public IActionResult Quotation(int id)
        {
            ViewBag.PageContentHeader = "Sales";

            SalesQuotation? model = null;

            if (id == 0)
            {
                ViewBag.PageContentHeader = "Add Sales Quotation";
                return View("AddSalesQuotation");
            }
            else
            {
                model = GetAsync<SalesQuotation>("Sales/Quotation?id=" + id).Result;
                @ViewBag.Id = model.Id;
                @ViewBag.QuotationDate = model.QuotationDate;
                @ViewBag.CustomerName = model.CustomerName;
                @ViewBag.PaymentTermId = model.PaymentTermId;
                @ViewBag.SalesQuotationLines = model.SalesQuotationLines;
                @ViewBag.TotalAmount = model.Amount;
            }

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(model);
        }
    }
}
