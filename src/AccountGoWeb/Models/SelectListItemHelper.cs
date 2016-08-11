using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net.Http;

namespace AccountGoWeb.Models
{
    public static class SelectListItemHelper
    {
        public static IConfiguration _config;

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Accounts()
        {
            var accounts = GetAsync<IEnumerable<Dto.Financial.Account>>("common/postingaccounts").Result;

            var selectAccounts = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var account in accounts)
                selectAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = account.Id.ToString(), Text = account.AccountName });

            return selectAccounts;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> TaxGroups()
        {
            var taxGroups = GetAsync<IEnumerable<Dto.TaxSystem.TaxGroup>>("tax/taxgroups").Result;
            var selectTaxGroups = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectTaxGroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var taxGroup in taxGroups)
                selectTaxGroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = taxGroup.Id.ToString(), Text = taxGroup.Description });

            return selectTaxGroups;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> ItemTaxGroups()
        {
            var itemtaxgroups = GetAsync<IEnumerable<Dto.TaxSystem.ItemTaxGroup>>("tax/itemtaxgroups").Result;
            var selectitemtaxgroups = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectitemtaxgroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var taxGroup in itemtaxgroups)
                selectitemtaxgroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = taxGroup.Id.ToString(), Text = taxGroup.Name });

            return selectitemtaxgroups;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> PaymentTerms()
        {
            var paymentTerms = GetAsync<IEnumerable<Dto.TaxSystem.TaxGroup>>("common/paymentterms").Result;
            var selectPaymentTerms = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectPaymentTerms.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var term in paymentTerms)
                selectPaymentTerms.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = term.Id.ToString(), Text = term.Description });

            return selectPaymentTerms;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> UnitOfMeasurements()
        {
            var uoms = GetAsync<IEnumerable<Dto.TaxSystem.TaxGroup>>("common/measurements").Result;
            var selectUOMS = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectUOMS.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in uoms)
                selectUOMS.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Description });

            return selectUOMS;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> ItemCategories()
        {
            var categories = GetAsync<IEnumerable<Dto.Inventory.ItemCategory>>("common/itemcategories").Result;
            var selectCategories = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectCategories.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in categories)
                selectCategories.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Name });

            return selectCategories;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> CashBanks()
        {
            var cashBanks = GetAsync<IEnumerable<Dto.Financial.Bank>>("common/cashbanks").Result;
            var selectCashBanks = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectCashBanks.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in cashBanks)
                selectCashBanks.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Name });

            return selectCashBanks;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Customers()
        {
            var customers = GetAsync<IEnumerable<Dto.Sales.Customer>>("sales/customers").Result;
            var selectCustomers = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectCustomers.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in customers)
                selectCustomers.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Name });

            return selectCustomers;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Vendors()
        {
            var vendors = GetAsync<IEnumerable<Dto.Purchasing.Vendor>>("purchasing/vendors").Result;
            var selectVendors = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectVendors.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in vendors)
                selectVendors.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Name });

            return selectVendors;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Items()
        {
            var items = GetAsync<IEnumerable<Dto.Inventory.Item>>("inventory/items").Result;
            var selectItems = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectItems.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in items)
                selectItems.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Description });

            return selectItems;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Measurements()
        {
            var measurements = GetAsync<IEnumerable<Dto.Inventory.Measurement>>("inventory/items").Result;
            var selectMeasurements = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectMeasurements.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "", Text = "" });
            foreach (var item in measurements)
                selectMeasurements.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = item.Id.ToString(), Text = item.Description });

            return selectMeasurements;
        }

        #region Private methods
        public static async System.Threading.Tasks.Task<T> GetAsync<T>(string uri)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _config["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + uri);
                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseJson);
        }
        #endregion
    }
}
