using System;
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
                return category;

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

        /// <summary>A new service item with the workspace defaults, not yet saved.</summary>
        public static Item NewItem(IInventoryService inventoryService, Services.Financial.IFinancialService financialService, string name)
        {
            var measurementId = MeasurementId(inventoryService);
            return new Item
            {
                ItemCategoryId = ServiceCategory(inventoryService, financialService, measurementId).Id,
                Description = name,
                SellDescription = name,
                SmallestMeasurementId = measurementId,
                SellMeasurementId = measurementId
            };
        }
    }
}
