using Dto.Inventory;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    public class InventoryController : Controller
    {
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public InventoryController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _config = config;
        }

        public async System.Threading.Tasks.Task<IActionResult> Items()
        {
            ViewBag.PageContentHeader = "Items";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _config["ApiUrl"];
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
                var baseUri = _config["ApiUrl"];
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

        public async System.Threading.Tasks.Task<IActionResult> Item(int id)
        {
            ViewBag.PageContentHeader = "Item Card";

            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "inventory/item?id=" + id);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var model = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.Inventory.Item>(responseJson);
                    return View(model);
                }
            }
            return View();
        }

        public IActionResult AddItem()
        {
            ViewBag.PageContentHeader = "New Item";

            return View("Item", new Dto.Inventory.Item());
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> Item(Item model)
        {
            if (model.Id == 0) // New item
            { }
            else
            { }

            return RedirectToAction("Items");
        }
    }
}
