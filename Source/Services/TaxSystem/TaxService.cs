using Core.Data;
using Core.Domain.Items;
using System;
using System.Collections.Generic;

namespace Services.TaxSystem
{
    public class TaxService : ITaxService
    {
        private readonly IRepository<Item> _itemRepo;

        public TaxService()
        {

        }

        public List<KeyValuePair<int, decimal>> GetPurchaseTaxes(int vendorId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            throw new NotImplementedException();
        }

        public decimal GetItemCost(int itemId)
        {
            throw new NotImplementedException();
        }

        public decimal GetItemPrice(int itemId)
        {
            throw new NotImplementedException();
        }

        public List<KeyValuePair<int, decimal>> GetSalesTaxes(int customerId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            throw new NotImplementedException();
        }

        public decimal PriceBeforeTax(int itemId)
        {
            throw new NotImplementedException();
        }

        public decimal PriceAfterTax(int itemId)
        {
            throw new NotImplementedException();
        }
    }
}
