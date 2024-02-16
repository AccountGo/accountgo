using Blazored.LocalStorage;
using Blazored.Toast.Services;
using Dto.Financial;
using System;
using System.Net.Http.Headers;
using System.Text.Json;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Services
{
    public class FinancialsService(HttpClient httpClient)
    {
        private const string financialBaseUrl = "/api/financial";

    }
}
