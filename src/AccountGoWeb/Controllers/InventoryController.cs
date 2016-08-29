using Dto.Inventory;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class InventoryController : BaseController
    {
        public InventoryController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }

        public async System.Threading.Tasks.Task<IActionResult> Items()
        {
            ViewBag.PageContentHeader = "Items";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
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

        public async System.Threading.Tasks.Task<IActionResult> ICJ()
        {
            ViewBag.PageContentHeader = "Inventory Control Journal";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "inventory/icj");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        public IActionResult Item(int id = -1)
        {
            Item itemModel = null;
            if (id == -1)
            {
                ViewBag.PageContentHeader = "Item Customer";
                itemModel = new Item();
                itemModel.No = new System.Random().Next(1, 99999).ToString(); // TODO: Replace with system generated numbering.
            }
            else
            {
                ViewBag.PageContentHeader = "Item Card";
                itemModel = GetAsync<Item>("inventory/item?id=" + id).Result;
            }

            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();
            ViewBag.Measurements = Models.SelectListItemHelper.UnitOfMeasurements();
            ViewBag.ItemCategories = Models.SelectListItemHelper.ItemCategories();

            return View(itemModel);
        }

        public IActionResult SaveItem(Item itemModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(itemModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = PostAsync("inventory/saveitem", content);

                return RedirectToAction("Items");
            }

            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();
            ViewBag.Measurements = Models.SelectListItemHelper.UnitOfMeasurements();
            ViewBag.ItemCategories = Models.SelectListItemHelper.ItemCategories();


            if (itemModel.Id > 0)
                ViewBag.PageContentHeader = "Item Item";
            else
                ViewBag.PageContentHeader = "New Card";

            return View("Item", itemModel);
        }
    }
}
