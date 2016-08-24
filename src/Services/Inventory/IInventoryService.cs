//-----------------------------------------------------------------------
// <copyright file="IInventoryService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Items;
using System.Collections.Generic;

namespace Services.Inventory
{
    public partial interface IInventoryService
    {
        InventoryControlJournal CreateInventoryControlJournal(int itemId,
            int measurementId,
            DocumentTypes documentType,
            decimal? inQty,
            decimal? outQty,
            decimal? totalCost,
            decimal? totalAmount);

        void AddItem(Item item);
        void UpdateItem(Item item);
        void DeleteItem(int itemId);
        Item GetItemById(int id);
        Item GetItemByNo(string itemNo);
        IEnumerable<Item> GetAllItems();
        IEnumerable<Measurement> GetMeasurements();
        Measurement GetMeasurementById(int id);
        IEnumerable<ItemCategory> GetItemCategories();
        IEnumerable<InventoryControlJournal> GetInventoryControlJournals();
        void SaveMeasurement(Measurement measurement);
        void SaveItemCategory(ItemCategory itemCategory);
    }
}
