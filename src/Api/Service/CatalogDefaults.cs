using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Items;
using Services.Inventory;

namespace Api.Service
{
    /// <summary>
    /// What a new catalog item gets when it is created from invoicing rather than set up in full: the workspace's
    /// "each" unit and its Services category. Shared by the Products &amp; services page and free-text invoice lines.
    /// </summary>
    public static class CatalogDefaults
    {
        /// <summary>The item free-text invoice lines are booked to; the line keeps the text that was typed.</summary>
        public const string GeneralItemName = "Service / Product";

        public static int MeasurementId(IInventoryService inventoryService)
        {
            var measurements = inventoryService.GetMeasurements().ToList();
            var measurement = measurements.FirstOrDefault(m => m.Code == "EA") ?? measurements.FirstOrDefault();
            return measurement?.Id ?? throw new Exception("No unit of measure is set up for this workspace.");
        }

        public static ItemCategory ServiceCategory(IInventoryService inventoryService, Services.Financial.IFinancialService financialService, int measurementId)
        {
            var category = inventoryService.GetItemCategories().FirstOrDefault(c => c.ItemType == ItemTypes.Service);
            if (category != null)
            {
                if (!category.SalesAccountId.HasValue)
                {
                    category.SalesAccountId = financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id;
                    if (category.SalesAccountId.HasValue)
                        inventoryService.SaveItemCategory(category);
                }
                return category;
            }

            category = new ItemCategory
            {
                Name = "Services",
                ItemType = ItemTypes.Service,
                MeasurementId = measurementId,
                SalesAccountId = financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id
            };
            inventoryService.SaveItemCategory(category);
            return category;
        }

        /// <summary>The stock category and four ledger accounts used by new stock items.</summary>
        public static ItemCategory StockCategory(IInventoryService inventoryService, Services.Financial.IFinancialService financialService, int measurementId)
        {
            var accounts = financialService.GetAccounts().ToList();
            int AccountId(string code, string name) =>
                accounts.FirstOrDefault(a => a.AccountCode == code)?.Id
                ?? throw new InvalidOperationException($"The workspace has no {name} account ({code}).");

            var category = inventoryService.GetItemCategories()
                .FirstOrDefault(c => c.ItemType == ItemTypes.Purchased && c.Name == "Stock items");
            var isNew = category == null;
            category ??= new ItemCategory { Name = "Stock items", ItemType = ItemTypes.Purchased };

            var salesAccountId = AccountId("40100", "Sales");
            var inventoryAccountId = AccountId("10800", "Inventory");
            var cogsAccountId = AccountId("50300", "Cost of Goods Sold");
            var adjustmentAccountId = AccountId("50310", "Inventory Adjustments");
            var changed = isNew
                || category.MeasurementId != measurementId
                || category.SalesAccountId != salesAccountId
                || category.InventoryAccountId != inventoryAccountId
                || category.CostOfGoodsSoldAccountId != cogsAccountId
                || category.AdjustmentAccountId != adjustmentAccountId;

            category.Name = "Stock items";
            category.ItemType = ItemTypes.Purchased;
            category.MeasurementId = measurementId;
            category.SalesAccountId = salesAccountId;
            category.InventoryAccountId = inventoryAccountId;
            category.CostOfGoodsSoldAccountId = cogsAccountId;
            category.AdjustmentAccountId = adjustmentAccountId;
            if (changed)
                inventoryService.SaveItemCategory(category);
            return category;
        }

        /// <summary>A new service or stock item with the workspace defaults, not yet saved.</summary>
        public static Item NewItem(IInventoryService inventoryService, Services.Financial.IFinancialService financialService, string name, bool trackStock = false)
        {
            var measurementId = MeasurementId(inventoryService);
            var category = trackStock
                ? StockCategory(inventoryService, financialService, measurementId)
                : ServiceCategory(inventoryService, financialService, measurementId);
            var item = new Item
            {
                ItemCategoryId = category.Id,
                Description = name,
                SellDescription = name,
                SmallestMeasurementId = measurementId,
                SellMeasurementId = measurementId,
                SalesAccountId = category.SalesAccountId
            };
            if (trackStock)
            {
                item.ItemCategory = category;
                item.ItemCategoryId = category.Id;
                item.SalesAccountId = category.SalesAccountId;
                item.InventoryAccountId = category.InventoryAccountId;
                item.CostOfGoodsSoldAccountId = category.CostOfGoodsSoldAccountId;
                item.InventoryAdjustmentAccountId = category.AdjustmentAccountId;
                item.PurchaseMeasurementId = measurementId;
            }
            return item;
        }

        /// <summary>
        /// The item a line typed without picking from the catalog is booked to: an active catalog item named exactly as
        /// typed, or else the workspace's general item, added the first time it is needed. The line keeps its own text
        /// either way, so typing doesn't add an item to the catalog. Shared by invoices and bills.
        /// </summary>
        public static Item ResolveFreeTextItem(IInventoryService inventoryService, Services.Financial.IFinancialService financialService,
            List<Item> items, string? description)
        {
            var text = description?.Trim();
            var item = string.IsNullOrEmpty(text) ? null
                : items.FirstOrDefault(i => i.ArchivedAt == null && string.Equals(i.Description, text, StringComparison.OrdinalIgnoreCase));
            item ??= items.FirstOrDefault(i => string.Equals(i.Description, GeneralItemName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                return item;

            item = NewItem(inventoryService, financialService, GeneralItemName);
            inventoryService.AddItem(item);
            items.Add(item);
            return item;
        }
    }
}
