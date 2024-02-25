﻿using Blazored.LocalStorage;
using Blazored.Toast.Services;
using Dto.Authentication;
using Dto.Financial;
using Dto.Inventory.Request;
using Dto.Inventory.Response;
using System.Net.Http.Headers;
using WebBlazor.Services.Contracts;
using static Dto.Response.ServiceResponses;

namespace WebBlazor.Services
{
    public class InventoryService(HttpClient httpClient) : IInventoryService
    {
        private const string baseUrl = "/api/inventory";

        public async Task<IEnumerable<GetItem>> GetItems()
        {
            var httpResponse = await httpClient.GetAsync($"{baseUrl}/items");

            if (!httpResponse.IsSuccessStatusCode)
            {
                return Enumerable.Empty<GetItem>();
            }

            return Generics.DeserializeJsonString<IEnumerable<GetItem>>(await httpResponse.Content.ReadAsStringAsync());
        }

        public async Task<CreatedResponse> CreateItem(CreateItem request)
        {
            var httpResponse = await httpClient
             .PostAsync($"{baseUrl}/saveitem",
             Generics.GenerateStringContent(
             Generics.SerializeObj(request)));

            var apiResponse = await httpResponse.Content.ReadAsStringAsync();

            return Generics.DeserializeJsonString<CreatedResponse>(apiResponse);
        }
    }
}
