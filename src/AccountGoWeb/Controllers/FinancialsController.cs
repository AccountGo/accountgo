using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class FinancialsController : Controller
    {
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public FinancialsController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _config = config;
        }

        public async System.Threading.Tasks.Task<IActionResult> Accounts()
        {
            ViewBag.PageContentHeader = "Accounts";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/accounts");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }
    }
}
