using Dto.Financial;
using Dto.TaxSystem;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection.Metadata;
using WebBlazor.Services.Contracts;
using Blazored.LocalStorage;
using Blazored.Toast.Services;

namespace WebBlazor.Services
{
    public class TaxService(HttpClient httpClient, ILocalStorageService localStore, IToastService toasService) : ITaxService
    {
        const string baseUrl = "/api/tax";
        public  async Task<IEnumerable<ItemTaxGroup>> GetItemTaxGroups()
        {
            var requestMessgage = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/itemtaxgroups");

            var token = await localStore.GetItemAsync<string>("token");
            requestMessgage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await httpClient.SendAsync(requestMessgage);

            if (!httpResponse.IsSuccessStatusCode)
            {
                toasService.ShowError("Unable to retrieve data. Try again later.");
                return Enumerable.Empty<ItemTaxGroup>();
            }

            return Generics.DeserializeJsonString<IEnumerable<ItemTaxGroup>>(await httpResponse.Content.ReadAsStringAsync());
        }
    }
}
