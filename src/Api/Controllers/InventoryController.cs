using Microsoft.AspNetCore.Mvc;
using Dto.Inventory;
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
            {
                itemsDto.Add(new Item()
                {
                    Id = item.Id,
                    Code = item.Code,
                    Description = item.Description,
                    Cost = item.Cost,
                    Price = item.Price,
                    QuantityOnHand = item.ComputeQuantityOnHand()
                });
            }

            return new ObjectResult(itemsDto.AsEnumerable());
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Item(int id)
        {
            var item = _inventoryService.GetItemById(id);
            var itemDto = new Item()
            {
                Id = item.Id,
                Code = item.Code,
                Description = item.Description,
                Cost = item.Cost,
                Price = item.Price,
                QuantityOnHand = item.ComputeQuantityOnHand()
            };

            return new ObjectResult(itemDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult ICJ()
        {
            var invControlJournals = _inventoryService.GetInventoryControlJournals();
            var icjDto = new List<InventoryControlJournal>();
            foreach (var icj in invControlJournals)
            {
                icjDto.Add(new InventoryControlJournal()
                {
                    Id = icj.Id,
                    In = icj.INQty,
                    Out = icj.OUTQty,
                    Item = icj.Item.Description,
                    Measurement = icj.Measurement.Code,
                    Date = icj.Date
                });
            }
            return new ObjectResult(icjDto.AsEnumerable());
        }    
    }
}
