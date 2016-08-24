using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class HomeController : BaseController
    {

        public HomeController(IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }
        public IActionResult Index()
        {
            ViewBag.PageContentHeader = "Dashboard";


 
            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> GetMonthlySales()
        {
            ViewBag.PageContentHeader = "Customers";
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "SALES/GetMonthlySales");
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
