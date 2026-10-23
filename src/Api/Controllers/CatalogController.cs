#nullable enable
using System;
using System.Linq;
using Api.Filters;
using Core.Domain.Security;
using Microsoft.AspNetCore.Mvc;
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
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;
        private readonly Api.Data.ApiDbContext _db;

        public CatalogController(IInventoryService inventoryService,
            IFinancialService financialService,
            Core.Tenancy.ITenantGuard tenantGuard,
            Api.Data.ApiDbContext db)
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
            _tenantGuard = tenantGuard;
            _db = db;
        }

        [HttpGet]
        [Route("Items")] // api/Catalog/Items
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Items() =>
            Ok(_db.Items.OrderBy(i => i.Description).AsEnumerable().Select(ToCatalogItem).ToList());

        [HttpGet]
        [Route("Item")] // api/Catalog/Item?id=
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Item(int id)
        {
            var item = _db.Items.FirstOrDefault(i => i.Id == id);
            if (item == null)
                return NotFound(new { message = "Item not found." });

            var dto = ToCatalogItem(item);
            dto.CanDelete = !IsUsed(id);
            return Ok(dto);
        }

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
            if (code != null && _db.Items.AsEnumerable().Any(i => i.Id != itemDto.Id && string.Equals(i.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { message = $"Another item already uses the code {code}." });

            Core.Domain.Items.Item item;
            bool isNew = itemDto.Id == 0;
            if (isNew)
            {
                item = Api.Service.CatalogDefaults.NewItem(_inventoryService, _financialService, name);
            }
            else
            {
                _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(itemDto.Id, nameof(itemDto.Id));
                item = _db.Items.Single(i => i.Id == itemDto.Id);
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

            var saved = ToCatalogItem(item);
            saved.CanDelete = isNew || !IsUsed(item.Id);
            return Ok(saved);
        }

        [HttpPost]
        [Route("ArchiveItem")]
        [RequirePermission(SystemPermissions.InventoryItemsManage)]
        public IActionResult ArchiveItem(int id, bool archived = true)
        {
            _tenantGuard.EnsureOwned<Core.Domain.Items.Item>(id, nameof(id));
            var item = _db.Items.Single(i => i.Id == id);
            item.ArchivedAt = archived ? item.ArchivedAt ?? DateTime.UtcNow : null;
            item.ModifiedBy = GetUserNameFromRequestHeader();
            _inventoryService.UpdateItem(item);

            var dto = ToCatalogItem(item);
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

        private static Dto.Inventory.CatalogItem ToCatalogItem(Core.Domain.Items.Item item) => new()
        {
            Id = item.Id,
            Name = item.Description,
            Code = item.Code,
            Price = item.Price,
            Cost = item.Cost,
            Archived = item.ArchivedAt.HasValue,
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
