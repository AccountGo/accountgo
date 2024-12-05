using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class GoodController : Controller
    {
        protected IConfiguration? _configuration;

        protected HttpResponseMessage Get(string uri)
        {
            using (var client = CreateAuthorizedHttpClient())
            {
                string? baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                return client.GetAsync(baseUri + uri).Result;
            }
        }

        protected HttpResponseMessage Post(string uri, StringContent data)
        {
            using (var client = CreateAuthorizedHttpClient())
            {
                string? baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                return client.PostAsync(baseUri + uri, data).Result;
            }
        }

        protected async System.Threading.Tasks.Task<T> GetAsync<T>(string uri)
        {
            using (var client = CreateAuthorizedHttpClient())
            {
                string? baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + uri);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseJson)!;
                }
                return default!;
            }
        }

        protected async System.Threading.Tasks.Task<string> PostAsync(string uri, StringContent data)
        {
            using (var client = CreateAuthorizedHttpClient())
            {
                string? baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.PostAsync(baseUri + uri, data);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return responseJson;
                }
                return string.Empty;
            }
        }

        private HttpClient CreateAuthorizedHttpClient()
        {
            var client = new HttpClient();
            string? token = HttpContext.Session.GetString("AccessToken"); // Ensure the token is stored in the session during login.

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

    }
}
