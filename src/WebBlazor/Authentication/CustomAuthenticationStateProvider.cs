using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Security.Claims;

namespace WebBlazor.Authentication
{
    public class CustomAuthenticationStateProvider(ILocalStorageService localStorageService, HttpClient httpClient) : AuthenticationStateProvider
    {
        private ClaimsPrincipal anonymous = new(new ClaimsIdentity());
        public async override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                string stringToken = await localStorageService.GetItemAsStringAsync("token");

                if (string.IsNullOrWhiteSpace(stringToken))
                    return await Task.FromResult(new AuthenticationState(anonymous));

                var claims = Generics.ParseClaimsFromJwt(stringToken);

                var expiry = claims.Where(claim => claim.Type.Equals("exp")).FirstOrDefault();
                if (expiry == null)
                    return await Task.FromResult(new AuthenticationState(anonymous));

                // The exp field is in Unix time
                var datetime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiry.Value));
                if (datetime.UtcDateTime <= DateTime.UtcNow)
                    return await Task.FromResult(new AuthenticationState(anonymous)); ;

                var userClaims = Generics.GetUserClaimsFromToken(stringToken);

                var claimsPrincipal = Generics.SetClaimPrincipal(userClaims);

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", stringToken);
                return await Task.FromResult(new AuthenticationState(claimsPrincipal));
            }
            catch
            {
                return await Task.FromResult(new AuthenticationState(anonymous));
            }
        }

        public async Task UpdateAuthenticationState(string? token)
        {
            ClaimsPrincipal claimsPrincipal = new();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var userSession = Generics.GetUserClaimsFromToken(token);
                claimsPrincipal = Generics.SetClaimPrincipal(userSession);
                await localStorageService.SetItemAsStringAsync("token", token);
            }
            else
            {
                claimsPrincipal = anonymous;
                await localStorageService.RemoveItemAsync("token");
            }
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
        }
    }

}
