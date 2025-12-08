using Dto.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class InventoryController : BaseController
    {
        private readonly ILogger<InventoryController> _logger;

        public InventoryController(Microsoft.Extensions.Configuration.IConfiguration config,
            ILogger<InventoryController> logger)
        {
            _baseConfig = config;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Existing Index logic
            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "inventory/items");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }
            return View();
        }

        [HttpGet]  // ← ADD THIS for initial page load
        public IActionResult AddItem()
        {
            var itemModel = new Item
            {
                Id = 0
            };

            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();
            ViewBag.Measurements = Models.SelectListItemHelper.UnitOfMeasurements();
            ViewBag.ItemCategories = Models.SelectListItemHelper.ItemCategories();
            ViewBag.PreferredVendorId = Models.SelectListItemHelper.Vendors();
            ViewBag.PageContentHeader = "New Item";

            return View("addItem", itemModel);
        }

        [HttpPost]  // ← Keep this for form submission
        public async Task<IActionResult> AddItem(Item itemModel, string? addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                itemModel.Id = 0; // Reset for new row
                ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
                ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();
                ViewBag.Measurements = Models.SelectListItemHelper.UnitOfMeasurements();
                ViewBag.ItemCategories = Models.SelectListItemHelper.ItemCategories();
                ViewBag.PreferredVendorId = Models.SelectListItemHelper.Vendors();
                ViewBag.PageContentHeader = "New Item";
                return View("addItem", itemModel);
            }

            return await SaveItem(itemModel);
        }

        [HttpPost]
        public async Task<IActionResult> SaveItem(Item itemModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(itemModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                try
                {
                    var response = Post("inventory/saveitem", content);

                    _logger.LogInformation($"SaveItem Response Status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Item saved successfully: {itemModel.Description}");
                        return RedirectToAction("Index");
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"API Error {response.StatusCode}: {errorContent}");

                        try
                        {
                            dynamic errors = Newtonsoft.Json.JsonConvert.DeserializeObject(errorContent);
                            if (errors is Newtonsoft.Json.Linq.JArray errorArray)
                            {
                                foreach (var error in errorArray)
                                {
                                    ModelState.AddModelError("", error.ToString());
                                }
                            }
                            else if (errors is string errorString)
                            {
                                ModelState.AddModelError("", errorString);
                            }
                            else
                            {
                                ModelState.AddModelError("", $"API Error: {response.StatusCode}");
                            }
                        }
                        catch
                        {
                            ModelState.AddModelError("", $"Failed to save item. Server error: {response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"SaveItem Exception: {ex.Message}\n{ex.StackTrace}");
                    ModelState.AddModelError("", $"Error: {ex.Message}");
                }
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors);
                foreach (var error in errors)
                {
                    _logger.LogWarning($"Validation Error: {error.ErrorMessage}");
                }
            }

            // Return to form with errors displayed
            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();
            ViewBag.Measurements = Models.SelectListItemHelper.UnitOfMeasurements();
            ViewBag.ItemCategories = Models.SelectListItemHelper.ItemCategories();
            ViewBag.PreferredVendorId = Models.SelectListItemHelper.Vendors();
            ViewBag.PageContentHeader = itemModel.Id > 0 ? "Edit Item" : "New Item";

            return View("addItem", itemModel);
        }
    }
}