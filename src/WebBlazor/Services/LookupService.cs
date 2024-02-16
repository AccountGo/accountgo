using Blazored.LocalStorage;
using Blazored.Toast.Services;
using Dto.Financial;
using Dto.Inventory;
using Dto.Purchasing.Response;
using System.Net.Http.Headers;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Services
{
    public class LookupService(HttpClient httpClient) : ILookupService
    {
        const string commonBaseUrl = "/api/lookup";

        public async Task<IEnumerable<BaseAccount>> GetAccounts()
        {
            var httpResponse = await httpClient.GetAsync($"{commonBaseUrl}/accounts");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<BaseAccount>();
            }

            return Generics.DeserializeJsonString<IEnumerable<BaseAccount>>(await httpResponse.Content.ReadAsStringAsync());
        }

        public async Task<IEnumerable<ItemCategory>> GetItemCategories()
        {
            var httpResponse = await httpClient.GetAsync($"{commonBaseUrl}/itemcategories");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<ItemCategory>();
            }

            return Generics.DeserializeJsonString<IEnumerable<ItemCategory>>(await httpResponse.Content.ReadAsStringAsync());
        }

        public async Task<IEnumerable<Measurement>> GetMeasurements()
        {
            var httpResponse = await httpClient.GetAsync($"{commonBaseUrl}/measurements");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<Measurement>();
            }

            return Generics.DeserializeJsonString<IEnumerable<Measurement>>(await httpResponse.Content.ReadAsStringAsync());
        }

        public async Task<IEnumerable<GetVendorResponse>> GetVendors()
        {
            var httpResponse = await httpClient.GetAsync($"{commonBaseUrl}/vendors");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<GetVendorResponse>();
            }

            return Generics.DeserializeJsonString<IEnumerable<GetVendorResponse>>(await httpResponse.Content.ReadAsStringAsync());
        }
    }
}
