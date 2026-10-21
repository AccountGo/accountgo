using Api.Filters;
using Dto.Inventory;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Inventory;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule("inventory")]
    public class InventoryController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger<InventoryController> _logger;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;

        public InventoryController(IAdministrationService adminService,
            IInventoryService inventoryService,
            ILogger<InventoryController> logger,
            Core.Tenancy.ITenantGuard tenantGuard)
        {
            _adminService = adminService;
            _inventoryService = inventoryService;
            _logger = logger;
            _tenantGuard = tenantGuard;
        }

        [HttpPost]
        [Route("SaveItem")]
        public IActionResult SaveItem([FromBody] Item itemDto)
        {
            try
            {
                _logger.LogInformation($"SaveItem called: Code={itemDto.Code}, Description={itemDto.Description}");

                bool isNew = itemDto.Id == 0;
                Core.Domain.Items.Item? item = null;

                if (isNew)
                {
                    item = new Core.Domain.Items.Item();
                }
                else
                {
                    item = _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(itemDto.Id, nameof(itemDto.Id));
                }

                if (itemDto.SmallestMeasurementId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(itemDto.SmallestMeasurementId, nameof(itemDto.SmallestMeasurementId));
                if (itemDto.SellMeasurementId.HasValue && itemDto.SellMeasurementId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(itemDto.SellMeasurementId.Value, nameof(itemDto.SellMeasurementId));
                if (itemDto.PurchaseMeasurementId.HasValue && itemDto.PurchaseMeasurementId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Items.Measurement>(itemDto.PurchaseMeasurementId.Value, nameof(itemDto.PurchaseMeasurementId));
                if (itemDto.ItemCategoryId > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Items.ItemCategory>(itemDto.ItemCategoryId, nameof(itemDto.ItemCategoryId));
                if (itemDto.ItemTaxGroupId.HasValue && itemDto.ItemTaxGroupId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.TaxSystem.ItemTaxGroup>(itemDto.ItemTaxGroupId.Value, nameof(itemDto.ItemTaxGroupId));
                if (itemDto.SalesAccountId.HasValue && itemDto.SalesAccountId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(itemDto.SalesAccountId.Value, nameof(itemDto.SalesAccountId));
                if (itemDto.InventoryAccountId.HasValue && itemDto.InventoryAccountId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(itemDto.InventoryAccountId.Value, nameof(itemDto.InventoryAccountId));
                if (itemDto.InventoryAdjustmentAccountId.HasValue && itemDto.InventoryAdjustmentAccountId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(itemDto.InventoryAdjustmentAccountId.Value, nameof(itemDto.InventoryAdjustmentAccountId));
                if (itemDto.CostOfGoodsSoldAccountId.HasValue && itemDto.CostOfGoodsSoldAccountId.Value > 0)
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(itemDto.CostOfGoodsSoldAccountId.Value, nameof(itemDto.CostOfGoodsSoldAccountId));

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
                    _logger.LogInformation($"Item created successfully: {item.Id}");
                }
                else
                {
                    _inventoryService.UpdateItem(item);
                    _logger.LogInformation($"Item updated successfully: {item.Id}");
                }

                return Ok(new { id = item.Id, message = "Item saved successfully" });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                _logger.LogError($"SaveItem Error: {ex.Message}\n{ex.StackTrace}");
                return BadRequest(new { error = ex.Message });
            }
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
            if (item == null)
                return NotFound(new { message = $"Item #{id} not found." });

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
                    Item = icj.Item?.Description ?? $"Item {icj.ItemId}",
                    Measurement = icj.Measurement?.Code ?? $"Measurement {icj.MeasurementId}",
                    Date = icj.Date
                });
            }

            _logger.LogInformation("ICJ Count: " + icjDto.Count);
            return Ok(icjDto.AsEnumerable());
        }
    }
}
