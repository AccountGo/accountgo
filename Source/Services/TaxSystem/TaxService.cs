using Core.Data;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.TaxSystem
{
    public class TaxService : ITaxService
    {
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<Vendor> _vendorRepo;
        private readonly IRepository<Customer> _customerRepo;

        public TaxService(IRepository<Vendor> vendorRepo, IRepository<Customer> customerRepo, IRepository<Item> itemRepo)
        {
            _vendorRepo = vendorRepo;
            _customerRepo = customerRepo;
            _itemRepo = itemRepo;
        }

        public List<KeyValuePair<int, decimal>> GetPurchaseTaxes(int vendorId, IEnumerable<PurchaseInvoiceLine> purchaseInvoiceLines)
        {
            var taxes = new List<KeyValuePair<int, decimal>>();
            
            foreach(var line in purchaseInvoiceLines)
            {
                taxes.AddRange(GetPurchaseTaxes(vendorId, line.ItemId, line.Quantity, line.Cost.Value, line.Discount.Value));
            }

            return taxes;
        }

        public List<KeyValuePair<int, decimal>> GetPurchaseTaxes(int vendorId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            decimal taxAmount = 0, amountXquantity = 0, discountAmount = 0, subTotalAmount = 0;

            var taxes = new List<KeyValuePair<int, decimal>>();
            var item = _itemRepo.GetById(itemId);

            amountXquantity = amount * quantity;

            if (discount > 0)
                discountAmount = (discount / 100) * amountXquantity;

            subTotalAmount = amountXquantity - discountAmount;

            if (item.ItemTaxGroup != null)
            {
                foreach (var tax in item.ItemTaxGroup.ItemTaxGroupTax)
                {
                    if (!tax.IsExempt)
                    {
                        taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Tax.Rate / 100)));
                        taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
                    }
                }
            }

            var vendor = _vendorRepo.GetById(vendorId);

            if (vendor != null && vendor.TaxGroup != null)
            {
                foreach (var tax in vendor.TaxGroup.TaxGroupTax)
                {
                    if (taxes.Any(t => t.Key == tax.Id))
                        continue;

                    taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Tax.Rate / 100)));
                    taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
                }
            }

            return taxes;
        }

        public List<KeyValuePair<int, decimal>> GetSalesTaxes(int customerId, IEnumerable<SalesInvoiceLine> salesInvoiceLines)
        {
            var taxes = new List<KeyValuePair<int, decimal>>();

            foreach (var line in salesInvoiceLines)
            {
                taxes.AddRange(GetSalesTaxes(customerId, line.ItemId, line.Quantity, line.Amount, line.Discount));
            }

            return taxes;
        }

        public List<KeyValuePair<int, decimal>> GetSalesTaxes(int customerId, int itemId, decimal quantity, decimal amount, decimal discount)
        {
            decimal taxAmount = 0, amountXquantity = 0, discountAmount = 0, subTotalAmount = 0;

            var item = _itemRepo.GetById(itemId);
            var customer = _customerRepo.GetById(customerId);
            var taxes = new List<KeyValuePair<int, decimal>>();

            amountXquantity = amount * quantity;

            if (discount > 0)
                discountAmount = (discount / 100) * amountXquantity;

            subTotalAmount = amountXquantity - discountAmount;

            if (item.ItemTaxGroup != null)
            {
                foreach (var tax in item.ItemTaxGroup.ItemTaxGroupTax)
                {
                    if (!tax.IsExempt)
                    {
                        taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Tax.Rate / 100)));
                        taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
                    }
                }
            }

            if (customer != null && customer.TaxGroup != null)
            {
                foreach (var tax in customer.TaxGroup.TaxGroupTax)
                {
                    if (taxes.Any(t => t.Key == tax.Id))
                        continue;

                    taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Tax.Rate / 100)));
                    taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
                }
            }

            return taxes;
        }

        public decimal PriceBeforeTax(int itemId)
        {
            throw new NotImplementedException();
        }

        public decimal PriceAfterTax(int itemId)
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
    }
}
