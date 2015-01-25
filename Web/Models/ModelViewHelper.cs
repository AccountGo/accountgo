using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using System;
using System.Collections.Generic;
using System.Web.Mvc;

namespace Web.Models
{
    public sealed class ModelViewHelper
    {
        private static ISalesService _salesService = DependencyResolver.Current.GetService<ISalesService>();
        private static IPurchasingService _purchasingService = DependencyResolver.Current.GetService<IPurchasingService>();
        private static IFinancialService _financialService = DependencyResolver.Current.GetService<IFinancialService>();
        private static IInventoryService _inventoryService = DependencyResolver.Current.GetService<IInventoryService>();

        public static ICollection<SelectListItem> Accounts(IEnumerable<Account> accounts)
        {             
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var account in _financialService.GetAccounts())
                selections.Add(new SelectListItem() { Text = account.AccountName, Value = account.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Items(IEnumerable<Item> items)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var item in _inventoryService.GetAllItems())
                selections.Add(new SelectListItem() { Text = item.Description, Value = item.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Items()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var item in _inventoryService.GetAllItems())
                selections.Add(new SelectListItem() { Text = item.Description, Value = item.No });
            return selections;
        }

        public static ICollection<SelectListItem> Measurements(IEnumerable<Measurement> measurements)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var measurement in _inventoryService.GetMeasurements())
                selections.Add(new SelectListItem() { Text = measurement.Code, Value = measurement.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> ItemCategories(IEnumerable<ItemCategory> itemCategories)
        {
            var selections = new HashSet<SelectListItem>();
            foreach (var itemCategory in _inventoryService.GetItemCategories())
                selections.Add(new SelectListItem() { Text = itemCategory.Name, Value = itemCategory.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Vendors(IEnumerable<Vendor> vendors)
        {
            var selections = new HashSet<SelectListItem>();
            foreach (var vendor in _purchasingService.GetVendors())
                selections.Add(new SelectListItem() { Text = vendor.Name, Value = vendor.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Customers(IEnumerable<Customer> customers)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var customer in _salesService.GetCustomers())
                selections.Add(new SelectListItem() { Text = customer.Name, Value = customer.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Taxes(IEnumerable<Tax> taxes)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var tax in _financialService.GetTaxes())
                selections.Add(new SelectListItem() { Text = tax.TaxCode, Value = tax.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> ItemTaxGroups(IEnumerable<ItemTaxGroup> itemTaxGroups)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var tax in _financialService.GetItemTaxGroups())
                selections.Add(new SelectListItem() { Text = tax.Name, Value = tax.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> TaxGroups(IEnumerable<ItemTaxGroup> taxGroups)
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var tax in _financialService.GetItemTaxGroups())
                selections.Add(new SelectListItem() { Text = tax.Name, Value = tax.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> TransactionTypes()
        {
            var selections = new HashSet<SelectListItem>();
            return selections;
        }

        public static ICollection<SelectListItem> PaymentTerms()
        {
            var selections = new HashSet<SelectListItem>();
            selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var paymentTerm in _salesService.GetPaymentTerms())
                selections.Add(new SelectListItem() { Text = paymentTerm.Description, Value = paymentTerm.Id.ToString() });
            return selections;
        }

        public static ICollection<SelectListItem> Banks()
        {
            var selections = new HashSet<SelectListItem>();
            //selections.Add(new SelectListItem() { Text = string.Empty, Value = "-1", Selected = true });
            foreach (var bank in _financialService.GetCashAndBanks())
                selections.Add(new SelectListItem() { Text = bank.Name, Value = bank.AccountId.Value.ToString() });
            return selections;
        }
    }
}