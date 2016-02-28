using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace Web.ControllersApi
{
    public abstract class BaseApiController : ApiController
    {
        protected override void Initialize(HttpControllerContext controllerContext)
        {
            if (User.Identity.IsAuthenticated)
            {
                IEnumerable<string> permissions = null;

                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(ConfigurationManager.AppSettings["BaseUrl"]);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = client.GetAsync("api/security/GetPermissionsForUser?username=" + User.Identity.Name).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        permissions = response.Content.ReadAsAsync<IEnumerable<string>>().Result;
                    }
                }

                var id = ClaimsPrincipal.Current.Identities.First();

                foreach (var permission in permissions)
                {
                    id.AddClaim(new Claim(ClaimTypes.Role, permission));
                }
            }

            base.Initialize(controllerContext);
        }
    }
}