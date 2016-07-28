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
            var accounts = GetAsync<IEnumerable<Dto.Financial.Account>>("financials/accounts").Result;

            var selectAccounts = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "-1", Text = "" });
            foreach (var account in accounts)
                selectAccounts.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = account.Id.ToString(), Text = account.AccountName });

            return selectAccounts;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> TaxGroups()
        {
            var taxGroups = GetAsync<IEnumerable<Dto.TaxSystem.TaxGroup>>("tax/taxgroups").Result;
            var selectTaxGroups = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectTaxGroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "-1", Text = "" });
            foreach (var taxGroup in taxGroups)
                selectTaxGroups.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = taxGroup.Id.ToString(), Text = taxGroup.Description });

            return selectTaxGroups;
        }

        public static IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> PaymentTerms()
        {
            var paymentTerms = GetAsync<IEnumerable<Dto.TaxSystem.TaxGroup>>("common/paymentterms").Result;
            var selectPaymentTerms = new HashSet<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            selectPaymentTerms.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = "-1", Text = "" });
            foreach (var term in paymentTerms)
                selectPaymentTerms.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem() { Value = term.Id.ToString(), Text = term.Description });

            return selectPaymentTerms;
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
