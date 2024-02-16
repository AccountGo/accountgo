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
using System.Data;

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
        readonly IEfTransaction _dbTransaction;

        public InventoryService(IRepository<Item> itemRepo, 
            IRepository<Measurement> measurementRepo, 
            IRepository<InventoryControlJournal> icjRepo,
            IRepository<ItemCategory> itemCategoryRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<Bank> bankRepo,
            IRepository<Account> accountRepo,
            IRepository<ItemTaxGroup> itemTaxGroup,
            IEfTransaction dbTransaction
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
            _dbTransaction = dbTransaction;
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

        public int AddItem(Item item, decimal initialQuantityOnhand)
        {
            using (_dbTransaction.BeginTransaction())
            {
                try
                {
                    item.No = GetNextNumber(SequenceNumberTypes.Item);

                    _itemRepo.Insert(item);

                    var invJournalCtrl = CreateInventoryControlJournal(item.Id,
                        item.SmallestMeasurementId.Value,
                        DocumentTypes.InitialInventory,
                        inQty: initialQuantityOnhand,
                        outQty: 0,
                        totalCost: initialQuantityOnhand * item.Cost,
                        totalAmount: initialQuantityOnhand * item.Price);

                    _icjRepo.Insert(invJournalCtrl);

                    _dbTransaction.Commit();

                    return item.Id;
                }
                catch (Exception ex)
                {
                    //TODO: Implement error logging
                    Console.WriteLine(ex.ToString());
                    _dbTransaction.Rollback();

                    return -1;
                }
            }

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
                    item.No = GetNextNumber(SequenceNumberTypes.Item);
            }

            if (itemCategory.Id == 0)
                _itemCategoryRepo.Insert(itemCategory);
            else
                _itemCategoryRepo.Update(itemCategory);
        }
    }
}
