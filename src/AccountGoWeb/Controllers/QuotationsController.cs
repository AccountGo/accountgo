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
        public QuotationsController(IConfiguration config) {
            _configuration = config;
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

        public async Task<IActionResult> AddSalesQuotation()
        {
            var model = new Models.Sales.SalesQuotations();

            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                
                using (var client = new HttpClient())
                {
                    var baseUri = _configuration["ApiUrl"];
                    client.BaseAddress = new Uri(baseUri);

                    var response = await client.PostAsync("Sales/SaveQuotation", content);

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
