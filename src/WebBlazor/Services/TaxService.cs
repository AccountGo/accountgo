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

    }
}
