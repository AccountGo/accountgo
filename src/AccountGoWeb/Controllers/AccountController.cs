using AccountGoWeb.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AccountGoWeb.Controllers
{
    public class AccountController : BaseController
    {
        public AccountController(IConfiguration config)
        {
            _baseConfig = config;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult SignIn(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel() { Email = "admin@accountgo.ph", Password = "P@ssword1" });
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> SignIn(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                HttpResponseMessage responseSignIn = Post("account/signin", content);
                Newtonsoft.Json.Linq.JObject resultSignIn = Newtonsoft.Json.Linq.JObject.Parse(responseSignIn.Content.ReadAsStringAsync().Result);

                if (resultSignIn["result"] != null)
                {
                    var user = await GetAsync<Dto.Security.User>("administration/getuser?username=" + model.Email);

                    var claims = new List<Claim>();
                    claims.Add(new Claim(ClaimTypes.IsPersistent, model.RememberMe.ToString()));
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Email));
                    claims.Add(new Claim(ClaimTypes.Email, user.Email));

                    string firstName = user.FirstName != null ? user.FirstName : "";
                    string lastName = user.LastName != null ? user.LastName : "";

                    claims.Add(new Claim(ClaimTypes.GivenName, firstName));
                    claims.Add(new Claim(ClaimTypes.Surname, lastName));
                    claims.Add(new Claim(ClaimTypes.Name, firstName + " " + lastName));

                    foreach(var role in user.Roles)
                        claims.Add(new Claim(ClaimTypes.Role, role.Name));

                    claims.Add(new Claim(ClaimTypes.UserData, Newtonsoft.Json.JsonConvert.SerializeObject(user)));

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
        public IActionResult Unauthorize()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult Register(RegisterViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            try
            {
                if (ModelState.IsValid)
                {
                    var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                    var content = new StringContent(serialize);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    HttpResponseMessage responseAddNewUser = Post("account/addnewuser", content);
                    Newtonsoft.Json.Linq.JObject resultAddNewUser = Newtonsoft.Json.Linq.JObject.Parse(responseAddNewUser.Content.ReadAsStringAsync().Result);

                    HttpResponseMessage responseInitialized = null;
                    Newtonsoft.Json.Linq.JObject resultInitialized = null;
                    if ((bool)resultAddNewUser["succeeded"])
                    {
                        responseInitialized = Get("administration/initializedcompany");
                        resultInitialized = Newtonsoft.Json.Linq.JObject.Parse((responseInitialized.Content.ReadAsStringAsync().Result));
                        return RedirectToAction(nameof(AccountController.SignIn), "Account");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, resultAddNewUser["errors"][0]["description"].ToString());
                        return View(model);
                    }
                }
            }
            catch(Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Please check if your database is ready/published." + ": " + ex.Message);
                return View(model);
            }
            return View(model);
        }

        #region Private Methods
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
        #endregion
    }
}
