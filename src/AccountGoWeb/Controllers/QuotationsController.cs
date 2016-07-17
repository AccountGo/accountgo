using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AccountGoWeb.Controllers
{
    public class QuotationsController : Controller
    {
        private readonly IConfiguration _config;

        public QuotationsController(IConfiguration config) {
            _config = config;
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
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "sales/quotations");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    try {
                        var quotes = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Dto.Sales.SalesQuotation>>(responseJson);
                        return View(model: responseJson);
                    }
                    catch(System.Exception ex)
                    {
                        System.Diagnostics.Debug.Write(ex.Message);
                    }                    
                }
            }

            return View();
        }

        public IActionResult AddSalesQuotation()
        {
            ViewBag.PageContentHeader = "Add Sales Quotation";

            return View();
        }
    }
}
