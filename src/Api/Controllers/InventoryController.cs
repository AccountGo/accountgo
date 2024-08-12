﻿using Dto.Inventory;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Inventory;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class InventoryController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<InventoryController> _logger;

        public InventoryController(IAdministrationService adminService,
            IInventoryService inventoryService,
            ILogger<InventoryController> logger)
        {
            _adminService = adminService;
            _inventoryService = inventoryService;
            _logger = logger;
        }

        [HttpPost]
        [Route("SaveItem")]
        public IActionResult SaveItem([FromBody]Item itemDto)
        {
            bool isNew = itemDto.Id == 0;
            Core.Domain.Items.Item? item = null;

            if (isNew)
            {
                item = new Core.Domain.Items.Item();
            }
            else
            {
                item = _inventoryService.GetItemById(itemDto.Id);
            }

            _logger.LogInformation("Item is New: " + isNew);

            item.No = itemDto.No;
            item.Code = itemDto.Code;
            item.Description = itemDto.Description;
            item.SellDescription = itemDto.SellDescription;
            item.PurchaseDescription = itemDto.PurchaseDescription;
            item.Cost = itemDto.Cost;
            item.Price = itemDto.Price;
            item.SmallestMeasurementId = itemDto.SmallestMeasurementId;
            item.SellMeasurementId = itemDto.SellMeasurementId;
            item.PurchaseMeasurementId = itemDto.PurchaseMeasurementId;
            item.ItemCategoryId = itemDto.ItemCategoryId;
            item.ItemTaxGroupId = itemDto.ItemTaxGroupId;
            item.SalesAccountId = itemDto.SalesAccountId;
            item.InventoryAccountId = itemDto.InventoryAccountId;
            item.InventoryAdjustmentAccountId = itemDto.InventoryAdjustmentAccountId;
            item.CostOfGoodsSoldAccountId = itemDto.CostOfGoodsSoldAccountId;
            
            if (isNew)
            {
                _inventoryService.AddItem(item);
            }
            else
            {
                _inventoryService.UpdateItem(item);
            }

            return Ok();
        }

        [HttpGet]
        [Route("items")] // api/Inventory/items
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
                    ItemTaxGroupName = item.ItemTaxGroup == null ? "" : item.ItemTaxGroup.Name,
                    Measurement = item.PurchaseMeasurement == null ? "" : item.PurchaseMeasurement.Description,
                    Cost = item.Cost,
                    Price = item.Price,
                    QuantityOnHand = item.ComputeQuantityOnHand()
                });
            }

            return new ObjectResult(itemsDto.AsEnumerable());
        }

        [HttpGet]
        [Route("Item")]
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
                SellDescription = item.SellDescription,
                PurchaseDescription = item.PurchaseDescription,
                QuantityOnHand = item.ComputeQuantityOnHand(),
                ItemCategoryId = item.ItemCategoryId,
                SmallestMeasurementId = item.SmallestMeasurementId,
                SellMeasurementId = item.SellMeasurementId,
                PurchaseMeasurementId = item.PurchaseMeasurementId,
                PreferredVendorId = item.PreferredVendorId,
                ItemTaxGroupId = item.ItemTaxGroupId,
                SalesAccountId = item.SalesAccountId,
                InventoryAccountId = item.InventoryAccountId,
                CostOfGoodsSoldAccountId = item.CostOfGoodsSoldAccountId,
                InventoryAdjustmentAccountId = item.InventoryAdjustmentAccountId
            };

            return new ObjectResult(itemDto);
        }

        [HttpGet]
        [Route("ICJ")] // api/Inventory/ICJ
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

            _logger.LogInformation("ICJ Count: " + icjDto.Count);
            return new ObjectResult(icjDto.AsEnumerable());
        }    
    }
}
