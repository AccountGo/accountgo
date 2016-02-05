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

        public InventoryService(IRepository<Item> itemRepo, 
            IRepository<Measurement> measurementRepo, 
            IRepository<InventoryControlJournal> icjRepo,
            IRepository<ItemCategory> itemCategoryRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<Bank> bankRepo
            )
            : base(sequenceNumberRepo, null, null, bankRepo)
        {
            _itemRepo = itemRepo;
            _measurementRepo = measurementRepo;
            _icjRepo = icjRepo;
            _itemCategoryRepo = itemCategoryRepo;
            _sequenceNumberRepo = sequenceNumberRepo;
            _bankRepo = bankRepo;
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
            var query = from item in _itemRepo.Table select item;
            var items = query.ToList();
            return items;
        }

        public Item GetItemById(int id)
        {
            var query = from item in _itemRepo.Table
                        where item.Id == id
                        select item;
            return query.FirstOrDefault();
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
            var query = from f in _icjRepo.Table
                        select f;
            return query.AsEnumerable();
        }

        public Item GetItemByNo(string itemNo)
        {
            var query = from item in _itemRepo.Table
                        where item.No == itemNo
                        select item;
            return query.FirstOrDefault();
        }
    }
}
