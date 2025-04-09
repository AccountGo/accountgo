using AccountGoWeb.Models.TaxSystem;
using AutoMapper;
using DtoTax = Dto.TaxSystem; // Alias for Dto.TaxSystem
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration; // Add this for IConfiguration
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

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
            var baseUri = _baseConfig["ApiUrl"];

            var taxesResponse = await client.GetAsync($"{baseUri}Tax/Taxes");
            var taxGroupsResponse = await client.GetAsync($"{baseUri}Tax/TaxGroups");
            var itemTaxGroupsResponse = await client.GetAsync($"{baseUri}Tax/ItemTaxGroups");

            if (taxesResponse.IsSuccessStatusCode && taxGroupsResponse.IsSuccessStatusCode && itemTaxGroupsResponse.IsSuccessStatusCode)
            {
                var taxes = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.Tax>>(await taxesResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.Tax>();
                var taxGroups = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.TaxGroup>>(await taxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.TaxGroup>();
                var itemTaxGroups = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.ItemTaxGroup>>(await itemTaxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.ItemTaxGroup>();

                var viewModel = new TaxSystemViewModel
                {
                    Taxes = _mapper.Map<IEnumerable<Tax>>(taxes),
                    TaxGroups = _mapper.Map<IEnumerable<TaxGroup>>(taxGroups),
                    ItemTaxGroups = _mapper.Map<IEnumerable<ItemTaxGroup>>(itemTaxGroups)
                };

                return View(viewModel);
            }

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

        public async Task<IActionResult> EditTax(int taxId)
        {
            ViewBag.PageContentHeader = "Edit Tax";

            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            // Fetch the tax details
            var taxResponse = await client.GetAsync($"{baseUri}Tax/Taxes/{taxId}");
            if (!taxResponse.IsSuccessStatusCode)
            {
                ModelState.AddModelError(string.Empty, "Failed to load tax data for editing.");
                return RedirectToAction("Taxes");
            }
            var tax = Newtonsoft.Json.JsonConvert.DeserializeObject<DtoTax.Tax>(await taxResponse.Content.ReadAsStringAsync());

            // Fetch the SalesAccountId and PurchasingAccountId
            var taxAccountsResponse = await client.GetAsync($"{baseUri}Tax/TaxAccounts/{taxId}");
            if (!taxAccountsResponse.IsSuccessStatusCode)
            {
                ModelState.AddModelError(string.Empty, "Failed to load tax accounts.");
                return RedirectToAction("Taxes");
            }
            var taxAccounts = Newtonsoft.Json.JsonConvert.DeserializeObject<TaxAccountsDto>(await taxAccountsResponse.Content.ReadAsStringAsync());

            // Fetch the TaxGroupId
            var taxGroupTaxesResponse = await client.GetAsync($"{baseUri}Tax/TaxGroupTaxes/{taxId}");
            var taxGroupId = taxGroupTaxesResponse.IsSuccessStatusCode
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.TaxGroupTax>>(await taxGroupTaxesResponse.Content.ReadAsStringAsync())
                    .FirstOrDefault()?.TaxGroupId
                : null;

            // Fetch the ItemTaxGroupId
            var itemTaxGroupTaxesResponse = await client.GetAsync($"{baseUri}Tax/ItemTaxGroupTaxes/{taxId}");
            var itemTaxGroupId = itemTaxGroupTaxesResponse.IsSuccessStatusCode
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.ItemTaxGroupTax>>(await itemTaxGroupTaxesResponse.Content.ReadAsStringAsync())
                    .FirstOrDefault()?.ItemTaxGroupId
                : null;

            // Fetch all TaxGroups and ItemTaxGroups
            var taxGroupsResponse = await client.GetAsync($"{baseUri}Tax/TaxGroups");
            var itemTaxGroupsResponse = await client.GetAsync($"{baseUri}Tax/ItemTaxGroups");

            var taxGroups = taxGroupsResponse.IsSuccessStatusCode
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.TaxGroup>>(await taxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.TaxGroup>()
                : new List<DtoTax.TaxGroup>();

            var itemTaxGroups = itemTaxGroupsResponse.IsSuccessStatusCode
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<DtoTax.ItemTaxGroup>>(await itemTaxGroupsResponse.Content.ReadAsStringAsync()) ?? new List<DtoTax.ItemTaxGroup>()
                : new List<DtoTax.ItemTaxGroup>();

            // Map the data to the view model
            Console.WriteLine("Tax object fetched for editing:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(tax));

            var editTaxViewModel = new EditTaxViewModel
            {
                Tax = _mapper.Map<Tax>(tax),
                SalesAccountId = taxAccounts.SalesAccountId,
                PurchaseAccountId = taxAccounts.PurchasingAccountId,
                TaxGroup = _mapper.Map<TaxGroup>(taxGroups.FirstOrDefault(g => g.Id == taxGroupId)),
                ItemTaxGroup = _mapper.Map<ItemTaxGroup>(itemTaxGroups.FirstOrDefault(g => g.Id == itemTaxGroupId))
            };

            ViewBag.TaxGroups = taxGroups.Select(g => new SelectListItem
            {
                Value = g.Id.ToString(),
                Text = g.Description,
                Selected = g.Id == taxGroupId
            });

            ViewBag.ItemTaxGroups = itemTaxGroups.Select(g => new SelectListItem
            {
                Value = g.Id.ToString(),
                Text = g.Name,
                Selected = g.Id == itemTaxGroupId
            });

            return View(editTaxViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> EditTax(EditTaxViewModel editTaxViewModel)
        {
            Console.WriteLine("EditTax method in Web TaxController was called.");
            Console.WriteLine("EditTaxViewModel:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(editTaxViewModel));

            // Ensure all required properties are mapped
            var taxForUpdateDto = new DtoTax.TaxForUpdate
            {
                SalesAccountId = editTaxViewModel.SalesAccountId,
                PurchaseAccountId = editTaxViewModel.PurchaseAccountId,
                Tax = new DtoTax.Tax
                {
                    Id = editTaxViewModel.Tax.Id,
                    TaxName = editTaxViewModel.Tax.TaxName,
                    TaxCode = editTaxViewModel.Tax.TaxCode,
                    Rate = editTaxViewModel.Tax.Rate,
                    IsActive = editTaxViewModel.Tax.IsActive
                },
                TaxGroup = editTaxViewModel.TaxGroup != null
                    ? new DtoTax.TaxGroup
                    {
                        Id = editTaxViewModel.TaxGroup.Id,
                        Description = editTaxViewModel.TaxGroup.Description,
                        TaxAppliedToShipping = editTaxViewModel.TaxGroup.TaxAppliedToShipping,
                        IsActive = editTaxViewModel.TaxGroup.IsActive
                    }
                    : null,
                ItemTaxGroup = editTaxViewModel.ItemTaxGroup != null
                    ? new DtoTax.ItemTaxGroup
                    {
                        Id = editTaxViewModel.ItemTaxGroup.Id,
                        Name = editTaxViewModel.ItemTaxGroup.Name,
                        IsFullyExempt = editTaxViewModel.ItemTaxGroup.IsFullyExempt
                    }
                    : null
            };

            Console.WriteLine("TaxForUpdate DTO:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto));

            using var client = _httpClientFactory.CreateClient();
            var baseUri = _baseConfig["ApiUrl"];

            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto), Encoding.UTF8, "application/json");
            var response = await client.PutAsync($"{baseUri}Tax/EditTax", content);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Taxes");
            }

            ModelState.AddModelError(string.Empty, "Failed to save changes.");
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
