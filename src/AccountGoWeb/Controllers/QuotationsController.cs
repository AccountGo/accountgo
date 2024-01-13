using AccountGoWeb.Models;
using Dto.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class QuotationsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<QuotationsController> _logger;
        public QuotationsController(IConfiguration config, ILogger<QuotationsController> logger) {
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
                client.BaseAddress = new System.Uri(baseUri);
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

        public async Task<IActionResult> AddSalesQuotation(Models.Sales.SalesQuotations model)
        {

            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                
                using (var client = new HttpClient())
                {
                    var baseUri = _configuration["ApiUrl"];
                    client.BaseAddress = new Uri(baseUri);
                    _logger.LogInformation("AddSalesQuotation Content: " + await content.ReadAsStringAsync());

                    var response = await client.PostAsync("sales/savequotation", content);
                    _logger.LogInformation($"AddSalesQuotation Response: { response.ToString()}");

                    if (response.IsSuccessStatusCode)
                        return RedirectToAction("quotations");
                }
            }

            ViewBag.PageContentHeader = "Add Sales Quotation";

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();

            return View();
        }

        public IActionResult Quotation()
        {
            ViewBag.PageContentHeader = "Sales Quotation";

            return View();
        }
    }
}
