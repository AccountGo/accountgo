// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Domain.Items;
using Services.Financial;
using Services.Inventory;

namespace Services.Banking
{
    /// <summary>
    /// The workspace's general "Service / Product" item that document lines recorded from a statement line are booked
    /// to. Every invoice and bill line needs an item; the line keeps its own description. Added the first time it is
    /// needed, the same way free-text invoice and bill lines get it.
    /// </summary>
    internal static class StatementGeneralItem
    {
        public const string Name = "Service / Product";

        public static Item Resolve(IInventoryService inventory, IFinancialService financial)
        {
            // A direct lookup: GetAllItems loads every item with its accounts, category and stock journals.
            var item = inventory.GetActiveItemByDescription(Name);
            if (item != null)
                return item;

            var measurements = inventory.GetMeasurements().ToList();
            var measurement = measurements.FirstOrDefault(m => m.Code == "EA") ?? measurements.FirstOrDefault()
                ?? throw new InvalidOperationException("No unit of measure is set up for this workspace.");
            var category = inventory.GetItemCategories().FirstOrDefault(c => c.ItemType == ItemTypes.Service);
            if (category == null)
            {
                category = new ItemCategory
                {
                    Name = "Services",
                    ItemType = ItemTypes.Service,
                    MeasurementId = measurement.Id,
                    SalesAccountId = financial.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id
                };
                inventory.SaveItemCategory(category);
            }

            item = new Item
            {
                ItemCategoryId = category.Id,
                Description = Name,
                SellDescription = Name,
                SmallestMeasurementId = measurement.Id,
                SellMeasurementId = measurement.Id,
                SalesAccountId = category.SalesAccountId
            };
            inventory.AddItem(item);
            return item;
        }
    }
}
