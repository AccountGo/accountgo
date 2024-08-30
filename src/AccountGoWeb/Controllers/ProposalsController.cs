using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class ProposalsController : GoodController
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProposalsController> _logger;

        public ProposalsController(IConfiguration config, ILogger<ProposalsController> logger)
        {
            _configuration = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return RedirectToAction("proposals");
        }

        public async System.Threading.Tasks.Task<IActionResult> Proposals()
        {
            ViewBag.PageContentHeader = "Proposals";

            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
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

    }
}
