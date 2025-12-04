using AccountGoWeb.Models.TaxSystem;
using AutoMapper;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Mvc;
using System;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class TaxController : BaseController
    {
        private readonly IMapper _mapper;

        public TaxController(Microsoft.Extensions.Configuration.IConfiguration config, IMapper mapper)
        {
            _baseConfig = config;
            _mapper = mapper;
        }

        public IActionResult Index()
        {
            return View("TaxesBlazor");
        }

        public IActionResult Taxes()
        {
            return View("TaxesBlazor");
        }

        public IActionResult AddNewTax()
        {
            ViewBag.PageContentHeader = "Add New Tax";

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View();
        }

        [HttpPost]
        public IActionResult AddNewTax(TaxForCreation taxForCreationDto)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForCreationDto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = Post("Tax/addnewtax", content);
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> EditTax(int? id)
        {
            ViewBag.PageContentHeader = "Edit Tax";

            if (!id.HasValue)
            {
                TempData["Error"] = "Tax ID is required.";
                return RedirectToAction("Taxes");
            }

            try
            {
                // Fetch the full tax system data from API
                using (var client = new HttpClient())
                {
                    var baseUri = _baseConfig!["ApiUrl"];
                    Console.WriteLine($"Fetching tax data from: {baseUri}tax/taxes");
                    client.BaseAddress = new System.Uri(baseUri!);
                    client.DefaultRequestHeaders.Accept.Clear();
                    var response = await client.GetAsync(baseUri + "tax/taxes");

                    Console.WriteLine($"API Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"API Response received, length: {responseJson.Length}");

                        var taxSystemDto = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.TaxSystemDto>(responseJson);

                        if (taxSystemDto?.Taxes != null)
                        {
                            Console.WriteLine($"Found {taxSystemDto.Taxes.Count()} taxes in response");

                            var taxDto = taxSystemDto.Taxes.FirstOrDefault(t => t.Id == id.Value);
                            if (taxDto == null)
                            {
                                TempData["Error"] = $"Tax with ID {id.Value} not found.";
                                return RedirectToAction("Taxes");
                            }

                            Console.WriteLine($"Found tax: {taxDto.TaxName} (ID: {taxDto.Id})");

                            var editTaxViewModel = new Models.TaxSystem.EditTaxViewModel();
                            editTaxViewModel.Tax = _mapper.Map<Models.TaxSystem.Tax>(taxDto);

                            // Initialize TaxGroup and ItemTaxGroup to avoid null reference
                            editTaxViewModel.TaxGroup = new Models.TaxSystem.TaxGroup();
                            editTaxViewModel.ItemTaxGroup = new Models.TaxSystem.ItemTaxGroup();

                            // Find associated tax group
                            var taxGroupDto = taxSystemDto.TaxGroups?.FirstOrDefault(tg =>
                                tg.Taxes?.Any(t => t.TaxId == id.Value) ?? false);
                            if (taxGroupDto != null)
                            {
                                editTaxViewModel.TaxGroup = _mapper.Map<Models.TaxSystem.TaxGroup>(taxGroupDto);
                                Console.WriteLine($"Found tax group: {taxGroupDto.Description}");
                            }

                            // Find associated item tax group
                            var itemTaxGroupDto = taxSystemDto.ItemTaxGroups?.FirstOrDefault(itg =>
                                itg.Taxes?.Any(t => t.TaxId == id.Value) ?? false);
                            if (itemTaxGroupDto != null)
                            {
                                editTaxViewModel.ItemTaxGroup = _mapper.Map<Models.TaxSystem.ItemTaxGroup>(itemTaxGroupDto);
                                Console.WriteLine($"Found item tax group: {itemTaxGroupDto.Name}");
                            }

                            // Set account IDs (defaulting to standard accounts if not set)
                            editTaxViewModel.SalesAccountId = 20300; // Sales Tax account
                            editTaxViewModel.PurchaseAccountId = 50700; // Purchase Tax account

                            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
                            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

                            Console.WriteLine("Returning EditTax view");
                            return View("EditTax", editTaxViewModel);
                        }
                        else
                        {
                            Console.WriteLine("TaxSystemDto or Taxes is null");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"API call failed with status: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error loading tax for edit: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                TempData["Error"] = $"Error loading tax: {ex.Message}";
            }

            return RedirectToAction("Taxes");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTax(EditTaxViewModel editTaxViewModel)
        {
            if (ModelState.IsValid)
            {
                var taxForUpdateDto = _mapper.Map<Dto.TaxSystem.TaxForUpdate>(editTaxViewModel);

                using (var client = new System.Net.Http.HttpClient())
                {
                    var baseUri = _baseConfig!["ApiUrl"];
                    client.BaseAddress = new System.Uri(baseUri!);
                    client.DefaultRequestHeaders.Accept.Clear();

                    var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto);
                    var content = new StringContent(serialize);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                    var response = await client.PutAsync(baseUri + "Tax/edittax", content);

                    if (response.IsSuccessStatusCode)
                    {
                        return RedirectToAction("Taxes");
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        ModelState.AddModelError(string.Empty, $"Error updating tax: {errorContent}");
                    }
                }
            }

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View(editTaxViewModel);
        }

        public async Task<IActionResult> DeleteTax(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Tax/deletetax?id=" + id);

                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

        public async Task<IActionResult> DeleteTaxGroup(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Tax/deletetaxgroup?id=" + id);

                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

        public async Task<IActionResult> DeleteItemTaxGroup(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Tax/deleteitemtaxgroup?id=" + id);

                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

    }
}
