using Blazored.LocalStorage;
using Blazored.Toast.Services;
using Dto.Common.Response;
using Dto.Financial;
using Dto.Inventory.Response;
using Dto.Purchasing.Response;
using Dto.TaxSystem;
using System.Net.Http.Headers;
using WebBlazor.Rx;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Services
{
    public class LookupService(HttpClient httpClient, LookupRx lookupRx) : ILookupService
    {
        const string commonBaseUrl = "/api/lookup";

        async Task<IEnumerable<T>> GetLookup<T>(string endpoint)
        {
            var httpResponse = await httpClient.GetAsync($"{commonBaseUrl}/{endpoint}");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<T>();
            }

            return Generics.DeserializeJsonString<IEnumerable<T>>(await httpResponse.Content.ReadAsStringAsync());
        }

        public async Task<IEnumerable<BaseAccount>> GetAccounts()
        {
            var lookup = lookupRx.ChartOfAcctsLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }

            var res = await GetLookup<BaseAccount>("accounts");
            lookupRx.ChartOfAcctsLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<GetItem>> GetItems()
        {
            var lookup = lookupRx.ItemsLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }

            var res = await GetLookup<GetItem>("items");
            lookupRx.ItemsLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<GetItemCategory>> GetItemCategories()
        {
            var lookup = lookupRx.ItemCategoriesLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }
            var res = await GetLookup<GetItemCategory>("itemcategories");
            lookupRx.ItemCategoriesLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<GetMeasurement>> GetMeasurements()
        {
            var lookup = lookupRx.MeasurementsLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }
            var res = await GetLookup<GetMeasurement>("measurements");
            lookupRx.MeasurementsLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<GetVendor>> GetVendors()
        {
            var lookup = lookupRx.VendorsLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }
            var res =  await GetLookup<GetVendor>("vendors");
            lookupRx.VendorsLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<GetPaymentTerm>> GetPaymentTerms()
        {
            var lookup = lookupRx.PaymentTermsLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }
            var res = await GetLookup<GetPaymentTerm>("paymentterms");
            lookupRx.PaymentTermsLookup.OnNext(res);
            return res;
        }

        public async Task<IEnumerable<ItemTaxGroup>> GetItemTaxGroups()
        {
            var lookup = lookupRx.ItemTaxGroupLookup.Value;

            if (lookup.Any())
            {
                return lookup;
            }
            var res = await GetLookup<ItemTaxGroup>("itemtaxgroups");
            lookupRx.ItemTaxGroupLookup.OnNext(res);
            return res;
        }
    }
}
