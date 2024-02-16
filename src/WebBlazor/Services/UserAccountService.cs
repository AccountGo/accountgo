using Blazored.LocalStorage;
using Blazored.Toast.Services;
using Dto.Authentication;
using Dto.Contracts;
using static Dto.Response.ServiceResponses;

namespace WebBlazor.Services
{
    public class UserAccountService(HttpClient httpClient) : IUserAccount
    {
        private const string baseUrl = "/api/useraccount";
        public async Task<GeneralResponse> CreateAccount(UserDTO userDTO)
        {
            var response = await httpClient
                 .PostAsync($"{baseUrl}/register",
                 Generics.GenerateStringContent(
                 Generics.SerializeObj(userDTO)));

            var apiResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var error = Generics.DeserializeJsonString<GeneralResponse>(apiResponse);
                return error;
            }

            return Generics.DeserializeJsonString<GeneralResponse>(apiResponse);
        }

        public async Task<LoginResponse> LoginAccount(LoginDTO loginDTO)
        {
            var response = await httpClient
               .PostAsync($"{baseUrl}/signin", 
               Generics.GenerateStringContent(
               Generics.SerializeObj(loginDTO)));

            var apiResponse = await response.Content.ReadAsStringAsync();

            return Generics.DeserializeJsonString<LoginResponse>(apiResponse);
        }
    }
}
