using AccountGoWeb.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AccountGoWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _config;
        public AccountController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult SignIn(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel() { Email = "admin@accountgo.ph", Password = "Laco4447" });
        }

        [HttpPost]
        [AllowAnonymous]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> SignIn(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (ModelState.IsValid)
            {
                //var result = AcquireToken(model.Email, model.Password);                
                //if (result.Result != null && result.Result.IsSuccessStatusCode)

                //TODO: This is fake login. To be implemented later.

                var result = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                if (result != null && result.IsSuccessStatusCode)
                {
                    var claims = new List<Claim>();
                    claims.Add(new Claim(ClaimTypes.IsPersistent, model.RememberMe.ToString()));
                    claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, model.Email));
                    claims.Add(new Claim(ClaimTypes.Email, model.Email));

                    var identity = new ClaimsIdentity(claims, "AuthCookie");

                    ClaimsPrincipal principal = new ClaimsPrincipal(new[] { identity });
                    HttpContext.User = principal;

                    await HttpContext.Authentication.SignInAsync("AuthCookie", principal, new Microsoft.AspNetCore.Http.Authentication.AuthenticationProperties { IsPersistent = model.RememberMe });

                    return RedirectToLocal(returnUrl);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return View(model);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        public async Task<IActionResult> SignOut()
        {
            await HttpContext.Authentication.SignOutAsync("AuthCookie");

            return SignedOut();
        }

        public IActionResult SignedOut()
        {
            if (HttpContext.User.Identity.IsAuthenticated)
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }

            return View();
        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }

        private async Task<HttpResponseMessage> AcquireToken(string username, string password)
        {
            List<KeyValuePair<string, string>> body = new List<KeyValuePair<string, string>>();
            body.Add(new KeyValuePair<string, string>("grant_type", "password"));
            body.Add(new KeyValuePair<string, string>("scope", "openid"));
            body.Add(new KeyValuePair<string, string>("resource", _config["Authentication:AzureAD:Resource"]));
            body.Add(new KeyValuePair<string, string>("client_id", _config["Authentication:AzureAD:NativeAppClientId"]));
            body.Add(new KeyValuePair<string, string>("username", username));
            body.Add(new KeyValuePair<string, string>("password", password));

            string url = string.Format("https://login.microsoftonline.com/{0}/oauth2/token", _config["Authentication:AzureAD:TenantId"]);
            HttpResponseMessage response = null;
            using (HttpClient httpClient = new HttpClient())
            {
                HttpContent content = new FormUrlEncodedContent(body);
                response = httpClient.PostAsync(url, content).Result;
                if (response.IsSuccessStatusCode)
                {
                    Stream data = await response.Content.ReadAsStreamAsync();
                }
            }
            return response;
        }
    }
}
