using Microsoft.AspNetCore.Mvc;
using Model.Inventory;
using Services.Administration;
using Services.Inventory;
using System.Collections.Generic;
using System.Linq;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class InventoryController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IInventoryService _inventoryService;

        public InventoryController(IAdministrationService adminService,
            IInventoryService inventoryService)
        {
            _adminService = adminService;
            _inventoryService = inventoryService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Items()
        {
            var items = _inventoryService.GetAllItems();
            ICollection<Item> itemsDto = new HashSet<Item>();

            foreach (var item in items)
                itemsDto.Add(new Item() { No = item.No, Description = item.Description });

            return new ObjectResult(itemsDto.AsEnumerable());
        }
    }
}
