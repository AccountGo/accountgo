using Core.Data;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain.TaxSystem;
using Core.Domain;

namespace Services.TaxSystem
{
    public class TaxService : ITaxService
    {
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<Vendor> _vendorRepo;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<Party> _partyRepo;
        private readonly IRepository<Tax> _taxRepo;
        private readonly IRepository<TaxGroup> _taxGroupRepo;
        private readonly IRepository<TaxGroupTax> _taxGroupTaxRepo;
        private readonly IRepository<ItemTaxGroup> _itemTaxGroupRep;        
        private readonly IRepository<ItemTaxGroupTax> _itemTaxGroupTaxRepo;

        public TaxService(IRepository<Vendor> vendorRepo,
            IRepository<Customer> customerRepo,
            IRepository<Item> itemRepo,
            IRepository<Tax> taxRepo,
            IRepository<TaxGroup> taxGroupRepo,
            IRepository<TaxGroupTax> taxGroupTaxRepo,
            IRepository<ItemTaxGroupTax> itemTaxGroupTaxRepo,
            IRepository<ItemTaxGroup> itemTaxGroupRepo,
            IRepository<Party> partyRepo)
        {
            _vendorRepo = vendorRepo;
            _customerRepo = customerRepo;
            _itemRepo = itemRepo;
            _taxRepo = taxRepo;
            _taxGroupRepo = taxGroupRepo;
            _itemTaxGroupRep = itemTaxGroupRepo;
            _partyRepo = partyRepo;
            _itemTaxGroupTaxRepo = itemTaxGroupTaxRepo;
            _taxGroupTaxRepo = taxGroupTaxRepo;
        }

        public IEnumerable<Tax> GetTaxes(bool includeInActive = false)
        {
            var taxes = _taxRepo
                .GetAllIncluding(s => s.SalesAccount, p => p.PurchasingAccount)
                .AsEnumerable();

            return taxes;
        }
        public IEnumerable<TaxGroup> GetTaxGroups() {
            var taxGroups = _taxGroupRepo.GetAllIncluding(t => t.TaxGroupTax).AsEnumerable();
            return taxGroups;
        }
        public IEnumerable<ItemTaxGroup> GetItemTaxGroups() {
            var itemTaxGroup = _itemTaxGroupRep.GetAllIncluding(t => t.ItemTaxGroupTax).AsEnumerable();
            return itemTaxGroup;
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

            var intersectionTaxes = GetIntersectionTaxes(itemId, vendorId, Core.Domain.PartyTypes.Vendor);

            foreach (var tax in intersectionTaxes)
            {
                taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Rate / 100)));
                taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
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

            var intersectionTaxes = GetIntersectionTaxes(itemId, customerId, Core.Domain.PartyTypes.Customer);

            foreach(var tax in intersectionTaxes)
            {
                taxAmount = subTotalAmount - (subTotalAmount / (1 + (tax.Rate / 100)));
                taxes.Add(new KeyValuePair<int, decimal>(tax.Id, taxAmount));
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

        /// <summary>
        /// Get the applicable taxes as the intersection of item taxes and customer/vendor taxes.
        /// </summary>
        /// <param name="itemId">Item Id</param>
        /// <param name="partyId">Party Id</param>
        /// <param name="partyType">Type of party i.e. Customer / Vendor</param>
        /// <returns>Taxes</returns>
        public IEnumerable<Tax> GetIntersectionTaxes(int itemId, int partyId, PartyTypes partyType)
        {
            ICollection<TaxGroupTax> partyTaxes = null;
            ICollection<ItemTaxGroupTax> itemTaxes = null;
            IList<Tax> taxes = new List<Tax>();
            object party = null;

            var item = _itemRepo.GetAllIncluding(i => i.ItemTaxGroup,
                i => i.ItemTaxGroup.ItemTaxGroupTax)
                .Where(i => i.Id == itemId)
                .FirstOrDefault();

            if (item == null
                || item.ItemTaxGroup == null
                || item.ItemTaxGroup.ItemTaxGroupTax == null
                || item.ItemTaxGroup.ItemTaxGroupTax.Count == 0)
            {
                //Console.Debug("Item is not configured to compute tax");
                return taxes;
            }

            itemTaxes = item.ItemTaxGroup.ItemTaxGroupTax.Where(t => t.IsExempt == false).ToList();

            if (partyType == PartyTypes.Customer)
            {
                party = _customerRepo.GetAllIncluding(c => c.TaxGroup,
                    c => c.TaxGroup.TaxGroupTax)
                    .Where(c => c.Id == partyId)
                    .FirstOrDefault();

                if (party == null
                    || ((Customer)party).TaxGroup == null
                    || ((Customer)party).TaxGroup.TaxGroupTax == null
                    || ((Customer)party).TaxGroup.TaxGroupTax.Count == 0)
                {
                    //Console.Debug("Customer is not configured to compute tax");
                    return taxes;
                }

                partyTaxes = ((Customer)party).TaxGroup.TaxGroupTax;
            }
            else
            {
                party = _vendorRepo.GetAllIncluding(v => v.TaxGroup,
                    v => v.TaxGroup.TaxGroupTax)
                    .Where(v => v.Id == partyId)
                    .FirstOrDefault();

                if (party == null
                    || ((Vendor)party).TaxGroup == null
                    || ((Vendor)party).TaxGroup.TaxGroupTax == null
                    || ((Vendor)party).TaxGroup.TaxGroupTax.Count == 0)
                {
                    //Console.Debug("Vendor is not configured to compute tax");
                    return taxes;
                }

                partyTaxes = ((Vendor)party).TaxGroup.TaxGroupTax;
            }

            //var intersectionTaxes = from p in partyTaxes
            //             join i in itemTaxes on p.TaxId equals i.TaxId
            //             select new { p, i };

            //if (intersectionTaxes == null || intersectionTaxes.Count() == 0)
            //    return taxes;

            //taxes = from t in intersectionTaxes select t.p.Tax;

            var allTaxes = _taxRepo.GetAllIncluding().ToList();

            foreach (var p in partyTaxes)
            {
                foreach (var i in itemTaxes)
                {
                    if (p.TaxId == i.TaxId)
                    {
                        taxes.Add(allTaxes.Where(t => t.Id == p.TaxId).FirstOrDefault());
                        break;
                    }
                }
            }

            return taxes;
        }

        /// <summary>
        /// Get the applicable taxes as the intersection of item taxes and customer/vendor taxes.
        /// </summary>
        /// <param name="itemId">Item Id</param>
        /// <param name="partyId">Party Id</param>
        /// <returns>Taxes</returns>
        public IEnumerable<Core.Domain.TaxSystem.Tax> GetIntersectionTaxes(int itemId, int partyId)
        {
            ICollection<TaxGroupTax> partyTaxes = null;
            ICollection<ItemTaxGroupTax> itemTaxes = null;
            IList<Tax> taxes = new List<Tax>();
            Party party = null;

            var item = _itemRepo.GetAllIncluding(i => i.ItemTaxGroup,
                i => i.ItemTaxGroup.ItemTaxGroupTax)
                .Where(i => i.Id == itemId)
                .FirstOrDefault();
                        
            if (item == null
                || item.ItemTaxGroup == null
                || item.ItemTaxGroup.ItemTaxGroupTax == null
                || item.ItemTaxGroup.ItemTaxGroupTax.Count == 0)
            {            
                return taxes; // no tax configuration
            }

            itemTaxes = item.ItemTaxGroup.ItemTaxGroupTax.Where(t => t.IsExempt == false).ToList();

            party = _partyRepo.GetAllIncluding()
                .Where(p => p.Id == partyId)
                .FirstOrDefault();

            if (party != null && party.PartyType == PartyTypes.Customer)
            {
                Customer customer = _customerRepo.GetAllIncluding(c => c.TaxGroup,
                    c => c.TaxGroup.TaxGroupTax)
                    .Where(c => c.PartyId == partyId)
                    .FirstOrDefault();

                if (customer == null
                    || customer.TaxGroup == null
                    || customer.TaxGroup.TaxGroupTax == null
                    || customer.TaxGroup.TaxGroupTax.Count == 0)
                {
                    return taxes; // no tax configuration
                }

                partyTaxes = customer.TaxGroup.TaxGroupTax;
            }
            else if (party != null  && party.PartyType == PartyTypes.Vendor)
            {
                Vendor vendor = _vendorRepo.GetAllIncluding(v => v.TaxGroup,
                    v => v.TaxGroup.TaxGroupTax)
                    .Where(v => v.PartyId == partyId)
                    .FirstOrDefault();

                if (party == null
                    || vendor.TaxGroup == null
                    || vendor.TaxGroup.TaxGroupTax == null
                    || vendor.TaxGroup.TaxGroupTax.Count == 0)
                {
                    return taxes; // no tax configuration
                }

                partyTaxes = vendor.TaxGroup.TaxGroupTax;
            }
            else
            {
                // undefined error.
            }

            //var intersectionTaxes = from p in partyTaxes
            //                        join i in itemTaxes on p.TaxId equals i.TaxId
            //                        select new { p, i };

            //if (intersectionTaxes == null || intersectionTaxes.Count() == 0)
            //    return taxes;

            //taxes = from t in intersectionTaxes select t.p.Tax;

            var allTaxes = _taxRepo.GetAllIncluding().ToList();

            foreach(var p in partyTaxes)
            {
                foreach (var i in itemTaxes) {
                    if (p.TaxId == i.TaxId) {
                        taxes.Add(allTaxes.Where(t => t.Id == p.TaxId).FirstOrDefault());
                        break;
                    }
                }
            }

            return taxes;
        }


        public decimal GetSalesLineTaxAmount(decimal quantity, decimal amount, decimal discount, IEnumerable<Tax> taxes)
        {
            decimal lineTaxTotal = 0;
            amount = (amount*quantity) - discount;
            foreach (var tax in taxes)
            {
                lineTaxTotal = lineTaxTotal + (amount - (amount / (1 + (tax.Rate / 100))));
            }
                
            return lineTaxTotal;
        }
    }
}
