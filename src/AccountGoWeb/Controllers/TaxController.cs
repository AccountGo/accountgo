using AccountGoWeb.Models.TaxSystem;
using AutoMapper;
using DtoTax = Dto.TaxSystem; // Alias for Dto.TaxSystem
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration; // Add this for IConfiguration
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AccountGoWeb.Controllers
{
    public class TaxController : BaseController
    {
        private readonly IMapper _mapper;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _baseConfig; // Add IConfiguration

        public TaxController(IMapper mapper, IHttpClientFactory httpClientFactory, IConfiguration baseConfig)
        {
            _mapper = mapper;
            _httpClientFactory = httpClientFactory;
            _baseConfig = baseConfig; // Initialize _baseConfig
        }

        public IActionResult Index()
        {
            return RedirectToAction("Taxes");
        }

        public async Task<IActionResult> Taxes()
        {
            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"]; // Ensure _baseConfig is properly initialized

            var taxesResponse = await client.GetAsync($"{baseUri}Tax/Taxes");
            var taxGroupsResponse = await client.GetAsync($"{baseUri}Tax/TaxGroups");
            var itemTaxGroupsResponse = await client.GetAsync($"{baseUri}Tax/ItemTaxGroups");

            if (taxesResponse.IsSuccessStatusCode && taxGroupsResponse.IsSuccessStatusCode && itemTaxGroupsResponse.IsSuccessStatusCode)
            {
                var taxes = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.Tax>>(await taxesResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.Tax>();
                var taxGroups = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.TaxGroup>>(await taxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.TaxGroup>();
                var itemTaxGroups = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.ItemTaxGroup>>(await itemTaxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.ItemTaxGroup>();

                Console.WriteLine($"Taxes Response: {taxesResponse.StatusCode}");
                Console.WriteLine($"Tax Groups Response: {taxGroupsResponse.StatusCode}");
                Console.WriteLine($"Item Tax Groups Response: {itemTaxGroupsResponse.StatusCode}");

                var viewModel = new TaxSystemViewModel
                {
                    Taxes = _mapper.Map<IEnumerable<Tax>>(taxes),
                    TaxGroups = _mapper.Map<IEnumerable<TaxGroup>>(taxGroups),
                    ItemTaxGroups = _mapper.Map<IEnumerable<ItemTaxGroup>>(itemTaxGroups)
                };

                return View(viewModel);
            }

            // Return an empty view model if the API calls fail
            return View(new TaxSystemViewModel
            {
                Taxes = new List<Tax>(),
                TaxGroups = new List<TaxGroup>(),
                ItemTaxGroups = new List<ItemTaxGroup>()
            });
        }

        public IActionResult AddNewTax()
        {
            ViewBag.PageContentHeader = "Add New Tax";

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddNewTax(DtoTax.TaxForCreation taxForCreationDto)
        {
            if (!ModelState.IsValid)
                return View(taxForCreationDto);

            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(taxForCreationDto), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{baseUri}Tax/AddNewTax", content);

            if (response.IsSuccessStatusCode)
                return RedirectToAction("Taxes");

            ModelState.AddModelError(string.Empty, "Failed to add new tax.");
            return View(taxForCreationDto);
        }

        public IActionResult EditTax(string tax, string taxGroup, string itemTaxGroup)
        {
            ViewBag.PageContentHeader = "Edit Tax";

            if (string.IsNullOrEmpty(tax) || string.IsNullOrEmpty(taxGroup) || string.IsNullOrEmpty(itemTaxGroup))
            {
                ModelState.AddModelError(string.Empty, "Invalid data provided for editing tax.");
                return RedirectToAction("Taxes");
            }

            try
            {
                var taxObj = Newtonsoft.Json.JsonConvert.DeserializeObject<DtoTax.Tax>(tax);
                var taxGroupObj = Newtonsoft.Json.JsonConvert.DeserializeObject<DtoTax.TaxGroup>(taxGroup);
                var itemTaxGroupObj = Newtonsoft.Json.JsonConvert.DeserializeObject<DtoTax.ItemTaxGroup>(itemTaxGroup);

                var editTaxViewModel = new EditTaxViewModel
                {
                    Tax = _mapper.Map<Tax>(taxObj),
                    TaxGroup = _mapper.Map<TaxGroup>(taxGroupObj),
                    ItemTaxGroup = _mapper.Map<ItemTaxGroup>(itemTaxGroupObj)
                };

                ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
                ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

                return View(editTaxViewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deserializing tax data: {ex.Message}");
                ModelState.AddModelError(string.Empty, "Failed to load tax data for editing.");
                return RedirectToAction("Taxes");
            }
        }

        [HttpPost]
        public async Task<IActionResult> EditTax(EditTaxViewModel editTaxViewModel)
        {
            if (ModelState.IsValid)
            {
                var taxForUpdateDto = _mapper.Map<DtoTax.TaxForUpdate>(editTaxViewModel);

                using var client = new HttpClient();
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();

                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = await client.PutAsync($"{baseUri}Tax/EditTax", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Taxes");
                }
            }

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View(editTaxViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTax(int id)
        {
            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            var response = await client.DeleteAsync($"{baseUri}Tax/DeleteTax/{id}");
            return RedirectToAction("Taxes");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTaxGroup(int id)
        {
            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            var response = await client.DeleteAsync($"{baseUri}Tax/DeleteTaxGroup/{id}");
            return RedirectToAction("Taxes");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteItemTaxGroup(int id)
        {
            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            var response = await client.DeleteAsync($"{baseUri}Tax/DeleteItemTaxGroup/{id}");
            return RedirectToAction("Taxes");
        }
    }
}
