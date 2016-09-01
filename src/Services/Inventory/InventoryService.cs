//-----------------------------------------------------------------------
// <copyright file="InventoryService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Items;
using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Core.Domain.Financials;
using Core.Domain.TaxSystem;

namespace Services.Inventory
{
    public partial class InventoryService : BaseService, IInventoryService
    {
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<InventoryControlJournal> _icjRepo;
        private readonly IRepository<Measurement> _measurementRepo;
        private readonly IRepository<ItemCategory> _itemCategoryRepo;
        private readonly IRepository<SequenceNumber> _sequenceNumberRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<ItemTaxGroup> _itemTaxGroup;

        public InventoryService(IRepository<Item> itemRepo, 
            IRepository<Measurement> measurementRepo, 
            IRepository<InventoryControlJournal> icjRepo,
            IRepository<ItemCategory> itemCategoryRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<Bank> bankRepo,
            IRepository<Account> accountRepo,
            IRepository<ItemTaxGroup> itemTaxGroup
            )
            : base(sequenceNumberRepo, null, null, bankRepo)
        {
            _itemRepo = itemRepo;
            _measurementRepo = measurementRepo;
            _icjRepo = icjRepo;
            _itemCategoryRepo = itemCategoryRepo;
            _sequenceNumberRepo = sequenceNumberRepo;
            _bankRepo = bankRepo;
            _accountRepo = accountRepo;
            _itemTaxGroup = itemTaxGroup;
        }

        public InventoryControlJournal CreateInventoryControlJournal(int itemId, int measurementId, DocumentTypes documentType, decimal? inQty, decimal? outQty, decimal? totalCost, decimal? totalAmount)
        {
            if (!inQty.HasValue && !outQty.HasValue)
                throw new MissingFieldException();

            var icj = new InventoryControlJournal()
            {
                ItemId = itemId,
                MeasurementId = measurementId,
                DocumentType = documentType,
                Date = DateTime.Now,
                INQty = inQty,
                OUTQty = outQty,
                TotalCost = totalCost,
                TotalAmount = totalAmount,
            };
            return icj;
        }

        public void AddItem(Item item)
        {
            item.No = GetNextNumber(SequenceNumberTypes.Item).ToString();

            var sales = _accountRepo.Table.Where(a => a.AccountCode == "40100").FirstOrDefault();
            var inventory = _accountRepo.Table.Where(a => a.AccountCode == "10800").FirstOrDefault();
            var invAdjusment = _accountRepo.Table.Where(a => a.AccountCode == "50500").FirstOrDefault();
            var cogs = _accountRepo.Table.Where(a => a.AccountCode == "50300").FirstOrDefault();
            var assemblyCost = _accountRepo.Table.Where(a => a.AccountCode == "10900").FirstOrDefault();

            item.SalesAccount = sales;
            item.InventoryAccount = inventory;
            item.CostOfGoodsSoldAccount = cogs;
            item.InventoryAdjustmentAccount = invAdjusment;

            item.ItemTaxGroup = _itemTaxGroup.Table.Where(m => m.Name == "Regular").FirstOrDefault();
            
            _itemRepo.Insert(item);
        }

        public void UpdateItem(Item item)
        {
            _itemRepo.Update(item);
        }

        public void DeleteItem(int itemId)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Item> GetAllItems()
        {
            var items = _itemRepo.GetAllIncluding(
                i => i.CostOfGoodsSoldAccount,
                i => i.InventoryAccount,
                i => i.InventoryAdjustmentAccount,
                i => i.SalesAccount,
                i => i.ItemTaxGroup,
                i => i.ItemCategory,
                i => i.InventoryControlJournals,
                i => i.PurchaseMeasurement
                );

            return items;
        }

        public Item GetItemById(int id)
        {
            var item = _itemRepo.GetAllIncluding(
                i => i.CostOfGoodsSoldAccount,
                i => i.InventoryAccount,
                i => i.InventoryAdjustmentAccount,
                i => i.SalesAccount,
                i => i.ItemTaxGroup,
                i => i.ItemCategory,
                i => i.InventoryControlJournals
                )
                .Where(i => i.Id == id)
                .FirstOrDefault();
            
            return item;
        }

        public IEnumerable<Measurement> GetMeasurements()
        {
            var query = from f in _measurementRepo.Table
                        select f;
            return query.AsEnumerable();
        }

        public Measurement GetMeasurementById(int id)
        {
            return _measurementRepo.GetById(id);
        }

        public IEnumerable<ItemCategory> GetItemCategories()
        {
            var query = from f in _itemCategoryRepo.Table
                        select f;
            return query;
        }

        public IEnumerable<InventoryControlJournal> GetInventoryControlJournals()
        {
            var query = _icjRepo.GetAllIncluding(m => m.Measurement, i => i.Item);
            return query.AsEnumerable();
        }

        public Item GetItemByNo(string itemNo)
        {
            var query = from item in _itemRepo.Table
                        where item.No == itemNo
                        select item;
            return query.FirstOrDefault();
        }

        public void SaveMeasurement(Measurement measurement)
        {
            if (measurement.Id == 0)
                _measurementRepo.Insert(measurement);
            else
                _measurementRepo.Update(measurement);
        }

        public void SaveItemCategory(ItemCategory itemCategory)
        {
            foreach(var item in itemCategory.Items)
            {
                if (item.Id == 0)
                    item.No = GetNextNumber(SequenceNumberTypes.Item).ToString();
            }

            if (itemCategory.Id == 0)
                _itemCategoryRepo.Insert(itemCategory);
            else
                _itemCategoryRepo.Update(itemCategory);
        }
    }
}
