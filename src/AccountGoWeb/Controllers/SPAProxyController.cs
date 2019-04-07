using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccountGoWeb.Controllers
{
    [Route("[controller]")]
    public class SPAProxyController : BaseController
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IConfiguration _configuration;

        public SPAProxyController(IHttpClientFactory clientFactory, IConfiguration configuration)
        {
            _clientFactory = clientFactory;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetAsync(string endpoint)
        {
            var url = _configuration["ApiUrl"] + endpoint;
            try
            {
                var client = _clientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                using (var response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, new System.Threading.CancellationToken(false))
                        .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var respData = await response.Content.ReadAsStringAsync();
                    return Ok(respData);
                }
            }
            catch (HttpRequestException rEx)
            {
                System.Console.WriteLine($"Error occured: {rEx.Message}");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"Error occured: {ex.Message}");
            };
            return BadRequest();
        }

        [HttpPost]
        public async Task<IActionResult> PostAsync(string endpoint, [FromBody]JObject body)
        {
            var url = _configuration["ApiUrl"] + endpoint;
            try
            {
                var client = _clientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                JsonSerializerSettings jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                var json = JsonConvert.SerializeObject(body, Formatting.Indented, jsonSettings);
                using (var stringContent = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    request.Content = stringContent;
                    
                    using (var response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, new System.Threading.CancellationToken(false))
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var respData = await response.Content.ReadAsStringAsync();
                        return Ok(respData);
                    }
                }
            }
            catch(HttpRequestException rEx)
            {
                System.Console.WriteLine($"Error occured: {rEx.Message}");
            }
            catch(System.Exception ex)
            {   
                System.Console.WriteLine($"Error occured: {ex.Message}");
            }
            return BadRequest();
        }
    }
}