using Microsoft.AspNetCore.Mvc;
using Dto.Inventory;
using Services.Administration;
using Services.Inventory;
using System.Collections.Generic;
using System.Linq;
using Dto.Inventory.Request;
using static Dto.Response.ServiceResponses;
using Azure;
using Dto.Inventory.Response;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class InventoryController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IInventoryService _inventoryService;

        public InventoryController(IAdministrationService adminService,
            IInventoryService inventoryService)
        {
            _adminService = adminService;
            _inventoryService = inventoryService;
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveItem([FromBody]CreateItemRequest request)
        {
            bool isNew = request.Id == 0;
            Core.Domain.Items.Item item = null;

            if (isNew)
            {
                item = new Core.Domain.Items.Item();
            }
            else
            {
                item = _inventoryService.GetItemById(request.Id);
            }

            item.Code = request.SKU;
            item.Description = request.Name;
            item.SellDescription = request.SellDescription;
            item.PurchaseDescription = request.PurchaseDescription;
            item.Cost = request.Cost;
            item.Price = request.Price;
            item.ReorderPoint = (decimal)request.ReorderPoint;
            item.SmallestMeasurementId = request.SmallestMeasurementId;
            item.SellMeasurementId = request.SellMeasurementId;
            item.PurchaseMeasurementId = request.PurchaseMeasurementId;
            item.ItemCategoryId = request.ItemCategoryId;
            item.ItemTaxGroupId = request.ItemTaxGroupId;
            item.SalesAccountId = request.SalesAccountId;
            item.InventoryAccountId = request.InventoryAccountId;
            item.InventoryAdjustmentAccountId = request.InventoryAdjustmentAccountId;
            item.CostOfGoodsSoldAccountId = request.CostOfGoodsSoldAccountId;

            var response = new CreatedResponse(false, "-1", "Something went wrong");

            if (isNew)
            {
                int createdId = _inventoryService.AddItem(item, request.InitialQtyOnHand);

                if(createdId > 0)
                {
                   response = new CreatedResponse(true, createdId.ToString(), "Created");
                }
            }
            else
            {
                _inventoryService.UpdateItem(item);

                return Ok();
            }

            return response.Flag ? Ok(response) : BadRequest(response); 
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Items()
        {
            var items = _inventoryService.GetAllItems();
            
            ICollection<GetItemResponse> itemsDto = new HashSet<GetItemResponse>();

            foreach (var item in items)
            {
                itemsDto.Add(new GetItemResponse()
                {

                    Id = item.Id,
                    Name = item.Description,
                    ProductNo = item.No,
                    SKU = item.Code,
                    ItemTaxGroup = item.ItemTaxGroup == null ? "" : item.ItemTaxGroup.Name,
                    Measurement = item.PurchaseMeasurement == null ? "" : item.PurchaseMeasurement.Description,
                    Cost = item.Cost ?? 0,
                    Price = item.Price ?? 0,
                    QtyOnHand = item.ComputeQuantityOnHand()
                });
            }

            return Ok(itemsDto);
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
