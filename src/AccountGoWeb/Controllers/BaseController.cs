using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    public class BaseController : Controller
    {
        protected IConfiguration _baseConfig;

        protected async System.Threading.Tasks.Task<T> GetAsync<T>(string uri)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + uri);
                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseJson);
        }

        protected HttpResponseMessage Get(string uri)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = client.GetAsync(baseUri + uri);
                return response.Result;
            }
        }

        protected async System.Threading.Tasks.Task<string> PostAsync(string uri, StringContent data)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("UserName", GetCurrentUserName());

                var response = await client.PostAsync(baseUri + uri, data);
                if (response.IsSuccessStatusCode)
                {
                    responseJson = await response.Content.ReadAsStringAsync();
                }
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(responseJson);
        }

        protected HttpResponseMessage Post(string uri, StringContent data)
        {
            string responseJson = string.Empty;
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("UserName", GetCurrentUserName());

                var response = client.PostAsync(baseUri + uri, data);
                return response.Result;
            }
        }

        protected bool HasPermission(string permission)
        {
            if (HttpContext.User.Identity.IsAuthenticated)
            {
                System.Collections.Generic.IList<string> permissions = new System.Collections.Generic.List<string>();

                var claimsEnumerator = HttpContext.User.Claims.GetEnumerator();
                while(claimsEnumerator.MoveNext())
                {
                    var current = claimsEnumerator.Current;
                    if (current.Type == System.Security.Claims.ClaimTypes.UserData)
                    {
                        Newtonsoft.Json.Linq.JObject userData = Newtonsoft.Json.Linq.JObject.Parse(current.Value);
                        foreach(var r in userData["Roles"])
                        {
                            foreach(var p in r["Permissions"])
                            {
                                permissions.Add(p["Name"].ToString());
                            }
                        }
                    }
                }

                if (permissions.Contains(permission))
                    return true;
            }
            return false;
        }

        protected string GetCurrentUserName()
        {
            if (HttpContext.User.Identity.IsAuthenticated)
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
