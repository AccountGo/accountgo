using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class TaxController : BaseController
    {
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public TaxController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _config = config;
        }

        public IActionResult Index() {
            return RedirectToAction("taxes");
        }

        public async System.Threading.Tasks.Task<IActionResult> Taxes()
        {
            ViewBag.PageContentHeader = "Tax";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "tax/taxes");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var taxSystemDto = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.TaxSystemDto>(responseJson);
                    var taxSystemViewModel = new Models.TaxSystem.TaxSystemViewModel();
                    taxSystemViewModel.Taxes = taxSystemDto.Taxes;
                    taxSystemViewModel.ItemTaxGroups = taxSystemDto.ItemTaxGroups;
                    taxSystemViewModel.TaxGroups = taxSystemDto.TaxGroups;

                    return View(taxSystemViewModel);
                }
            }

            return View();
        }
    }
}
