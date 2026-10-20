using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class BaseController : Controller
    {
        protected IConfiguration? _baseConfig;
        protected IConfiguration? _configuration
        {
            get => _baseConfig;
            set => _baseConfig = value;
        }

        private sealed class RefreshFlightEntry
        {
            public required Task<bool> Task { get; init; }
            public DateTime CompletedOrStartedAt { get; set; } = DateTime.UtcNow;
        }

        private static readonly ConcurrentDictionary<string, RefreshFlightEntry> _activeRefreshes = new();

        protected string? GetAccessToken()
        {
            return HttpContext?.User?.FindFirst("access_token")?.Value;
        }

        protected void AttachAuth(HttpClient client)
        {
            var token = GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        protected async Task<bool> TryRefreshAccessTokenAsync()
        {
            var userId = HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return false;

            var refreshToken = HttpContext?.Request?.Cookies["accountgo_refresh_token"];
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            // Single-flight deduplication + TTL window: coalesce concurrent 401 refresh calls into a single execution
            if (_activeRefreshes.TryGetValue(userId, out var existing))
            {
                if (DateTime.UtcNow - existing.CompletedOrStartedAt < TimeSpan.FromSeconds(5))
                {
                    return await existing.Task;
                }
            }

            var newFlight = new RefreshFlightEntry
            {
                Task = PerformRefreshAsync(userId, refreshToken)
            };

            var entry = _activeRefreshes.AddOrUpdate(userId, newFlight, (_, old) =>
            {
                if (DateTime.UtcNow - old.CompletedOrStartedAt < TimeSpan.FromSeconds(5))
                    return old;
                return newFlight;
            });

            var success = await entry.Task;
            entry.CompletedOrStartedAt = DateTime.UtcNow;

            if (!success)
            {
                _activeRefreshes.TryRemove(userId, out _);
            }

            return success;
        }

        private async Task<bool> PerformRefreshAsync(string userId, string currentRefreshToken)
        {
            try
            {
                using var client = new HttpClient();
                string? baseUri = (_baseConfig ?? _configuration)?["ApiUrl"] ?? "http://localhost:8001/api/";
                var refreshUri = new Uri(new Uri(baseUri), "auth/refresh");

                using var request = new HttpRequestMessage(HttpMethod.Post, refreshUri);
                request.Headers.Add("Cookie", $"accountgo_refresh_token={currentRefreshToken}");

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var authObj = Newtonsoft.Json.Linq.JObject.Parse(responseJson);
                var newAccessToken = authObj["accessToken"]?.ToString();

                if (string.IsNullOrEmpty(newAccessToken))
                    return false;

                // Rotate cookie if API returned a new Set-Cookie header
                if (TryExtractCookieValue(response, "accountgo_refresh_token", out var newRefreshToken))
                {
                    var isHttps = HttpContext?.Request?.IsHttps == true;
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = isHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTime.UtcNow.AddDays(7),
                        Path = "/"
                    };
                    HttpContext?.Response?.Cookies?.Append("accountgo_refresh_token", newRefreshToken, cookieOptions);
                }

                // Update current user identity claims with new access token
                if (HttpContext?.User?.Identity is ClaimsIdentity identity)
                {
                    var existingAccessClaim = identity.FindFirst("access_token");
                    if (existingAccessClaim != null)
                        identity.RemoveClaim(existingAccessClaim);
                    identity.AddClaim(new Claim("access_token", newAccessToken));

                    // Remove any legacy refresh_token claim if present
                    var existingRefreshClaim = identity.FindFirst("refresh_token");
                    if (existingRefreshClaim != null)
                        identity.RemoveClaim(existingRefreshClaim);

                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, HttpContext.User);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryExtractCookieValue(HttpResponseMessage response, string cookieName, out string cookieValue)
        {
            cookieValue = string.Empty;
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var parts = cookie.Split(';');
                    if (parts.Length > 0)
                    {
                        var kv = parts[0].Split('=', 2);
                        if (kv.Length == 2 && kv[0].Trim().Equals(cookieName, StringComparison.OrdinalIgnoreCase))
                        {
                            cookieValue = kv[1].Trim();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        protected async System.Threading.Tasks.Task<T> GetAsync<T>(string uri)
        {
            string responseJson = string.Empty;
            var baseUri = (_baseConfig ?? _configuration)?["ApiUrl"];
            using (var client = new HttpClient())
            {
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                AttachAuth(client);
                var response = await client.GetAsync(baseUri + uri);
                if (response.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAccessTokenAsync())
                {
                    using var retryClient = new HttpClient();
                    retryClient.BaseAddress = new System.Uri(baseUri!);
                    retryClient.DefaultRequestHeaders.Accept.Clear();
                    AttachAuth(retryClient);
                    response = await retryClient.GetAsync(baseUri + uri);
                }

                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseJson)!;
        }

        protected HttpResponseMessage Get(string uri)
        {
            var baseUri = (_baseConfig ?? _configuration)?["ApiUrl"];
            using (var client = new HttpClient())
            {
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                AttachAuth(client);
                var response = client.GetAsync(baseUri + uri).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.Unauthorized && TryRefreshAccessTokenAsync().GetAwaiter().GetResult())
                {
                    using var retryClient = new HttpClient();
                    retryClient.BaseAddress = new System.Uri(baseUri!);
                    retryClient.DefaultRequestHeaders.Accept.Clear();
                    AttachAuth(retryClient);
                    return retryClient.GetAsync(baseUri + uri).GetAwaiter().GetResult();
                }
                return response;
            }
        }

        /*
            This method performs an HTTP DELETE request to the specified URI.
            Used in AuditController to delete auditable entities and attributes.
        */
        protected async System.Threading.Tasks.Task DeleteAsync(string uri)
        {
            var baseUri = (_baseConfig ?? _configuration)?["ApiUrl"];
            using (var client = new HttpClient())
            {
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                AttachAuth(client);

                var response = await client.DeleteAsync(baseUri + uri);
                if (response.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAccessTokenAsync())
                {
                    using var retryClient = new HttpClient();
                    retryClient.BaseAddress = new System.Uri(baseUri!);
                    retryClient.DefaultRequestHeaders.Accept.Clear();
                    AttachAuth(retryClient);
                    response = await retryClient.DeleteAsync(baseUri + uri);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = await response.Content.ReadAsStringAsync();
                    throw new System.Exception($"DELETE {uri} failed: {message}");
                }
            }
        }

        protected async System.Threading.Tasks.Task<string> PostAsync(string uri, StringContent data)
        {
            string responseJson = string.Empty;
            var baseUri = (_baseConfig ?? _configuration)?["ApiUrl"];
            using (var client = new HttpClient())
            {
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                AttachAuth(client);

                var response = await client.PostAsync(baseUri + uri, data);
                if (response.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAccessTokenAsync())
                {
                    using var retryClient = new HttpClient();
                    retryClient.BaseAddress = new System.Uri(baseUri!);
                    retryClient.DefaultRequestHeaders.Accept.Clear();
                    AttachAuth(retryClient);
                    response = await retryClient.PostAsync(baseUri + uri, data);
                }

                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(responseJson)!;
        }

        protected HttpResponseMessage Post(string uri, StringContent data)
        {
            var baseUri = (_baseConfig ?? _configuration)?["ApiUrl"];
            using (var client = new HttpClient())
            {
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                AttachAuth(client);

                var response = client.PostAsync(baseUri + uri, data).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.Unauthorized && TryRefreshAccessTokenAsync().GetAwaiter().GetResult())
                {
                    using var retryClient = new HttpClient();
                    retryClient.BaseAddress = new System.Uri(baseUri!);
                    retryClient.DefaultRequestHeaders.Accept.Clear();
                    retryClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    AttachAuth(retryClient);
                    return retryClient.PostAsync(baseUri + uri, data).GetAwaiter().GetResult();
                }
                return response;
            }
        }

        protected bool HasPermission(string roleOrPermission)
        {
            return HttpContext.User.Identity?.IsAuthenticated == true &&
                   (HttpContext.User.IsInRole(roleOrPermission) ||
                    HttpContext.User.HasClaim(ClaimTypes.Role, roleOrPermission));
        }

        protected string GetCurrentUserName()
        {
            if (HttpContext.User.Identity!.IsAuthenticated)
            {
                var claimsEnumerator = HttpContext.User.Claims.GetEnumerator();
                while (claimsEnumerator.MoveNext())
                {
                    var current = claimsEnumerator.Current;
                    if (current.Type == System.Security.Claims.ClaimTypes.Email)
                    {
                        return current.Value;
                    }
                }
            }
            return string.Empty;
        }
    }
}
