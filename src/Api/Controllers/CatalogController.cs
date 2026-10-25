#nullable enable
using System;
using System.Linq;
using Api.Filters;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Administration;
using Services.Financial;
using Services.Inventory;

namespace Api.Controllers
{
    /// <summary>
    /// Products &amp; services: the items invoices (and later bills) are made of. Shared by Sales and Purchases, so it
    /// needs either module rather than Inventory. Saving an item changes only the details below; invoice lines keep
    /// the text they were saved with.
    /// </summary>
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule(SystemModules.Sales, SystemModules.Purchases)]
    public class CatalogController : BaseController
    {
        private const int NameMaxLength = 200;
        private const int CodeMaxLength = 30;

        private readonly IInventoryService _inventoryService;
        private readonly IFinancialService _financialService;
        private readonly IAdministrationService _adminService;
        private readonly IStockService _stockService;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;
        private readonly Api.Data.ApiDbContext _db;

        public CatalogController(IInventoryService inventoryService,
            IFinancialService financialService,
            IAdministrationService adminService,
            IStockService stockService,
            Core.Tenancy.ITenantGuard tenantGuard,
            Api.Data.ApiDbContext db)
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
            _adminService = adminService;
            _stockService = stockService;
            _tenantGuard = tenantGuard;
            _db = db;
        }

        [HttpGet]
        [Route("Items")] // api/Catalog/Items
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Items()
        {
            var items = _db.Items.Include(i => i.ItemCategory).Include(i => i.SalesAccount).OrderBy(i => i.Description).ToList();
            var itemIds = items.Select(i => i.Id).ToArray();
            var itemsWithMovements = _db.InventoryControlJournals
                .Where(j => itemIds.Contains(j.ItemId))
                .Select(j => j.ItemId)
                .Distinct()
                .ToHashSet();
            return Ok(items.Select(item => ToCatalogItem(item, itemsWithMovements.Contains(item.Id))).ToList());
        }

        [HttpGet]
        [Route("Item")] // api/Catalog/Item?id=
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Item(int id)
        {
            var item = _db.Items.Include(i => i.ItemCategory).Include(i => i.SalesAccount).FirstOrDefault(i => i.Id == id);
            if (item == null)
                return NotFound(new { message = "Item not found." });

            var dto = ToCatalogItem(item, _db.InventoryControlJournals.Any(j => j.ItemId == id));
            dto.CanDelete = !IsUsed(id);
            return Ok(dto);
        }

        /// <summary>The accounts a catalog item or invoice line can credit: postable revenue/income accounts.</summary>
        [HttpGet]
        [Route("SalesAccounts")] // api/Catalog/SalesAccounts
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult SalesAccounts() =>
            Ok(_db.Accounts
                .Where(a => (a.AccountClassId == (int)AccountClasses.Revenue || a.AccountCode.StartsWith("4"))
                    && !a.ChildAccounts.Any())
                .OrderBy(a => a.AccountCode)
                .Select(a => new { a.Id, Code = a.AccountCode, Name = a.AccountName })
                .ToList());

        [HttpPost]
        [Route("SaveItem")]
        [RequirePermission(SystemPermissions.InventoryItemsManage)]
        public IActionResult SaveItem([FromBody] Dto.Inventory.CatalogItem itemDto)
        {
            var name = Blank(itemDto.Name);
            if (name == null)
                return BadRequest(new { message = "Enter a name for the item." });
            if (name.Length > NameMaxLength)
                return BadRequest(new { message = $"An item name can be at most {NameMaxLength} characters." });
            var code = Blank(itemDto.Code);
            if (code != null && code.Length > CodeMaxLength)
                return BadRequest(new { message = $"A code can be at most {CodeMaxLength} characters." });
            if (itemDto.Price < 0 || itemDto.Cost < 0)
                return BadRequest(new { message = "Prices and costs can't be negative." });
            if (itemDto.OpeningQuantity < 0)
                return BadRequest(new { message = "Opening quantity can't be negative." });
            if (itemDto.OpeningUnitCost < 0)
                return BadRequest(new { message = "Opening cost can't be negative." });
            if (itemDto.OpeningQuantity > 0 && !itemDto.TrackStock)
                return BadRequest(new { message = "Opening stock can only be recorded for a stock-tracked item." });
            if (itemDto.Id > 0 && (itemDto.OpeningQuantity.HasValue || itemDto.OpeningUnitCost.HasValue))
                return BadRequest(new { message = "Opening stock can only be recorded when adding an item." });
            if (itemDto.OpeningQuantity > 0 && itemDto.OpeningUnitCost == null)
                return BadRequest(new { message = "Enter a cost per unit for opening stock." });
            if (itemDto.OpeningQuantity > 0 && Math.Round(itemDto.OpeningQuantity.Value, 4, MidpointRounding.ToEven) <= 0)
                return BadRequest(new { message = "Opening quantity must be at least 0.0001." });
            if (code != null && _db.Items.AsEnumerable().Any(i => i.Id != itemDto.Id && string.Equals(i.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { message = $"Another item already uses the code {code}." });

            bool isNew = itemDto.Id == 0;
            if (!isNew)
                _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(itemDto.Id, nameof(itemDto.Id));
            if (itemDto.SalesAccountId.HasValue && itemDto.SalesAccountId.Value > 0)
            {
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(itemDto.SalesAccountId.Value, nameof(itemDto.SalesAccountId));
                var salesAccount = _db.Accounts.FirstOrDefault(a => a.Id == itemDto.SalesAccountId.Value);
                bool isPostableRevenue = salesAccount != null
                    && (salesAccount.AccountClassId == (int)AccountClasses.Revenue || salesAccount.AccountCode.StartsWith("4"))
                    && !_db.Accounts.Any(a => a.ParentAccountId == salesAccount.Id);
                if (!isPostableRevenue)
                    return BadRequest(new { message = "The selected sales account must be a postable revenue/income account." });
            }
            var existingStockItem = !isNew && _db.Items
                .Include(i => i.ItemCategory)
                .Any(i => i.Id == itemDto.Id && i.ItemCategory != null && i.ItemCategory.ItemType == ItemTypes.Purchased);
            if (itemDto.TrackStock && !existingStockItem && !_adminService.IsModuleEnabled("inventory"))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Turn on the Inventory module before changing stock tracking for an item." });

            try
            {
                using var tx = _db.Database.BeginTransaction();
                var item = isNew
                    ? Api.Service.CatalogDefaults.NewItem(_inventoryService, _financialService, name, itemDto.TrackStock)
                    : _db.Items.Include(i => i.ItemCategory).Include(i => i.SalesAccount).Single(i => i.Id == itemDto.Id);

                var wasStockItem = !isNew && item.ItemCategory?.ItemType == ItemTypes.Purchased;
                var hasStockMovements = !isNew && _db.InventoryControlJournals.Any(j => j.ItemId == item.Id);
                if (wasStockItem && !itemDto.TrackStock && hasStockMovements)
                    return BadRequest(new { message = "This item already has stock movements, so it can't stop tracking stock. Archive it instead." });

                if (itemDto.TrackStock)
                {
                    var category = Api.Service.CatalogDefaults.StockCategory(
                        _inventoryService, _financialService, Api.Service.CatalogDefaults.MeasurementId(_inventoryService));
                    item.ItemCategory = category;
                    item.ItemCategoryId = category.Id;
                    item.InventoryAccountId = category.InventoryAccountId;
                    item.CostOfGoodsSoldAccountId = category.CostOfGoodsSoldAccountId;
                    item.InventoryAdjustmentAccountId = category.AdjustmentAccountId;
                    item.PurchaseMeasurementId = category.MeasurementId;
                }
                else if (wasStockItem)
                {
                    var measurementId = Api.Service.CatalogDefaults.MeasurementId(_inventoryService);
                    var category = Api.Service.CatalogDefaults.ServiceCategory(_inventoryService, _financialService, measurementId);
                    item.ItemCategory = category;
                    item.ItemCategoryId = category.Id;
                    item.InventoryAccountId = null;
                    item.CostOfGoodsSoldAccountId = null;
                    item.InventoryAdjustmentAccountId = null;
                    item.PurchaseMeasurementId = null;
                }

                if (itemDto.SalesAccountId.HasValue && itemDto.SalesAccountId.Value > 0)
                {
                    item.SalesAccountId = itemDto.SalesAccountId.Value;
                }
                else if (!item.SalesAccountId.HasValue)
                {
                    item.SalesAccountId = item.ItemCategory?.SalesAccountId
                        ?? _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id;
                }

                item.Description = name;
                item.SellDescription = name;
                item.Code = code;
                item.Price = itemDto.Price;
                item.Cost = itemDto.Cost;
                item.ModifiedBy = GetUserNameFromRequestHeader();

                if (isNew)
                    _inventoryService.AddItem(item);
                else
                    _inventoryService.UpdateItem(item);

                if (isNew && itemDto.TrackStock && itemDto.OpeningQuantity > 0)
                {
                    _stockService.Adjust(item, itemDto.OpeningQuantity.Value, itemDto.OpeningUnitCost,
                        "OpeningStock", DateTime.UtcNow.Date, "Opening stock");
                    _db.SaveChanges();
                }

                tx.Commit();
                _db.Entry(item).Reference(i => i.SalesAccount).Load();
                var saved = ToCatalogItem(item, _db.InventoryControlJournals.Any(j => j.ItemId == item.Id));
                saved.CanDelete = isNew || !IsUsed(item.Id);
                return Ok(saved);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("ArchiveItem")]
        [RequirePermission(SystemPermissions.InventoryItemsManage)]
        public IActionResult ArchiveItem(int id, bool archived = true)
        {
            _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(id, nameof(id));
            var item = _db.Items.Include(i => i.ItemCategory).Single(i => i.Id == id);
            item.ArchivedAt = archived ? item.ArchivedAt ?? DateTime.UtcNow : null;
            item.ModifiedBy = GetUserNameFromRequestHeader();
            _inventoryService.UpdateItem(item);

            var dto = ToCatalogItem(item, _db.InventoryControlJournals.Any(j => j.ItemId == id));
            dto.CanDelete = !IsUsed(id);
            return Ok(dto);
        }

        /// <summary>Deletes an item no document uses yet, such as one added by mistake; a used item is archived instead.</summary>
        [HttpDelete]
        [Route("DeleteItem")]
        [RequirePermission(SystemPermissions.InventoryItemsManage)]
        public IActionResult DeleteItem(int id)
        {
            _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(id, nameof(id));
            if (IsUsed(id))
                return BadRequest(new { message = "This item is used on invoices or other documents, so it can't be deleted. Archive it instead." });

            _db.Items.Remove(_db.Items.Single(i => i.Id == id));
            _db.SaveChanges();
            return NoContent();
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static Dto.Inventory.CatalogItem ToCatalogItem(Core.Domain.Items.Item item, bool hasStockMovements = false) => new()
        {
            Id = item.Id,
            Name = item.Description,
            Code = item.Code,
            Price = item.Price,
            Cost = item.Cost,
            Archived = item.ArchivedAt.HasValue,
            TrackStock = item.ItemCategory?.ItemType == ItemTypes.Purchased,
            InventoryAccountId = item.InventoryAccountId,
            SalesAccountId = item.SalesAccountId,
            SalesAccountCode = item.SalesAccount?.AccountCode,
            SalesAccountName = item.SalesAccount?.AccountName,
            QuantityOnHand = item.QuantityOnHand,
            AverageCost = item.AverageCost,
            HasStockMovements = hasStockMovements,
        };

        /// <summary>Whether any document line, receipt or stock movement refers to the item.</summary>
        private bool IsUsed(int itemId) =>
            _db.SalesInvoiceLines.Any(l => l.ItemId == itemId)
            || _db.SalesOrderLines.Any(l => l.ItemId == itemId)
            || _db.SalesQuoteLines.Any(l => l.ItemId == itemId)
            || _db.SalesDeliveryLines.Any(l => l.ItemId == itemId)
            || _db.SalesReceiptLines.Any(l => l.ItemId == itemId)
            || _db.PurchaseOrderLines.Any(l => l.ItemId == itemId)
            || _db.PurchaseReceiptLines.Any(l => l.ItemId == itemId)
            || _db.PurchaseInvoiceLines.Any(l => l.ItemId == itemId)
            || _db.DonationInvoiceLines.Any(l => l.ItemId == itemId)
            || _db.InventoryControlJournals.Any(j => j.ItemId == itemId);
    }
}
