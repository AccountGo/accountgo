using AccountGoWeb.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AccountGoWeb.Controllers
{
    public class AccountController : GoodController
    {
        public AccountController(IConfiguration config)
        {
            _configuration = config;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult SignIn(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel() { Email = "admin@accountgo.ph", Password = "P@ssword1" });
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> SignIn(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                HttpResponseMessage responseSignIn = Post("account/signin", content);
                // Newtonsoft.Json.Linq.JObject resultSignIn = Newtonsoft.Json.Linq.JObject.Parse(responseSignIn.Content.ReadAsStringAsync().Result);

                // if (resultSignIn["result"] != null)
                if (responseSignIn.IsSuccessStatusCode)
                {
                    var responseBody = await responseSignIn.Content.ReadAsStringAsync();

                    var tokenResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<LoginResponseModel>(responseBody);

                    if (tokenResponse != null)
                    {
                        // Save tokens or use them as needed
                        Console.WriteLine($"AccessToken: {tokenResponse.AccessToken}");
                        Console.WriteLine($"RefreshToken: {tokenResponse.RefreshToken}");

                        // Example: Set claims and authenticate user
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, model.Email!),
                            new Claim(ClaimTypes.Email, model.Email!),
                            new Claim("AccessToken", tokenResponse.AccessToken),
                            new Claim("RefreshToken", tokenResponse.RefreshToken)
                        };

                        var identity = new ClaimsIdentity(claims, "AuthCookie");
                        var principal = new ClaimsPrincipal(new[] { identity });

                        HttpContext.User = principal;

                        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                        return RedirectToLocal(returnUrl!);
                    }
                      
                    // var user = await GetAsync<Dto.Security.User>("administration/getuser?username=" + model.Email);

                    // var claims = new List<Claim>();
                    // claims.Add(new Claim(ClaimTypes.IsPersistent, model.RememberMe.ToString()));
                    // claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Email!));
                    // claims.Add(new Claim(ClaimTypes.Email, user.Email!));

                    // string firstName = user.FirstName != null ? user.FirstName : "";
                    // string lastName = user.LastName != null ? user.LastName : "";

                    // claims.Add(new Claim(ClaimTypes.GivenName, firstName));
                    // claims.Add(new Claim(ClaimTypes.Surname, lastName));
                    // claims.Add(new Claim(ClaimTypes.Name, firstName + " " + lastName));

                    // foreach(var role in user.Roles)
                    //     claims.Add(new Claim(ClaimTypes.Role, role.Name!));

                    // claims.Add(new Claim(ClaimTypes.UserData, Newtonsoft.Json.JsonConvert.SerializeObject(user)));

                    // var identity = new ClaimsIdentity(claims, "AuthCookie");

                    // ClaimsPrincipal principal = new ClaimsPrincipal(new[] { identity });

                    // HttpContext.User = principal;

                    // await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                    // return RedirectToLocal(returnUrl!);
                }
                else
                {
                    // ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    // return View(model);
                    var errorBody = await responseSignIn.Content.ReadAsStringAsync();
                    ModelState.AddModelError(string.Empty, $"Login failed: {errorBody}");
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        public async Task<IActionResult> SignOut()
        {
            await HttpContext.SignOutAsync();

            return SignedOut();
        }

        public IActionResult SignedOut()
        {
            if (HttpContext.User.Identity!.IsAuthenticated)
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
        public IActionResult Register(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult Register(RegisterViewModel model, string? returnUrl = null)
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
                    string responseContent = responseAddNewUser.Content.ReadAsStringAsync().Result;

                    // Try to parse as JObject (success or failure case)
                    Newtonsoft.Json.Linq.JObject? resultObject = null;
                    Newtonsoft.Json.Linq.JArray? resultArray = null;

                    try
                    {
                        resultObject = Newtonsoft.Json.Linq.JObject.Parse(responseContent);
                    }
                    catch (Newtonsoft.Json.JsonReaderException)
                    {
                        // If parsing as JObject fails, try parsing as JArray
                        resultArray = Newtonsoft.Json.Linq.JArray.Parse(responseContent);
                    }

                    // Handle based on the parsed result
                    if (resultObject != null)
                    {
                        // Handle response as a JObject
                        if ((bool)resultObject["succeeded"]!)
                        {
                            HttpResponseMessage responseInitialized = Get("administration/initializedcompany");
                            var resultInitialized = Newtonsoft.Json.Linq.JObject.Parse(responseInitialized.Content.ReadAsStringAsync().Result);
                            return RedirectToAction(nameof(AccountController.SignIn), "Account");
                        }
                        else
                        {
                            var errorDescription = resultObject["errors"]![0]!["description"]!.ToString();
                            ModelState.AddModelError(string.Empty, errorDescription);
                            return View(model);
                        }
                    }
                    else if (resultArray != null)
                    {
                        // Handle response as a JArray (exception case)
                        string errorMessage = string.Join("; ", resultArray.Select(error => error.ToString()));
                        ModelState.AddModelError(string.Empty, errorMessage);
                        return View(model);
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Unexpected response format.");
                        return View(model);
                    }

                    // Newtonsoft.Json.Linq.JObject resultAddNewUser = Newtonsoft.Json.Linq.JObject.Parse(responseAddNewUser.Content.ReadAsStringAsync().Result);
                    // var resultAddNewUser = Newtonsoft.Json.Linq.JArray.Parse(responseAddNewUser.Content.ReadAsStringAsync().Result);

                    // HttpResponseMessage? responseInitialized = null;
                    // Newtonsoft.Json.Linq.JObject? resultInitialized = null;
                    // if ((bool)resultAddNewUser["succeeded"]!)
                    // {
                    //     responseInitialized = Get("administration/initializedcompany");
                    //     resultInitialized = Newtonsoft.Json.Linq.JObject.Parse((responseInitialized.Content.ReadAsStringAsync().Result));
                    //     return RedirectToAction(nameof(AccountController.SignIn), "Account");
                    // }
                    // else
                    // {
                    //     ModelState.AddModelError(string.Empty, resultAddNewUser["errors"]![0]!["description"]!.ToString());
                    //     return View(model);
                    // }

                    // if (resultAddNewUser.Count > 0) // Ensure the array is not empty
                    // {
                    //     var firstResult = resultAddNewUser[0]; // Access the first object in the array

                    //     if ((bool)firstResult["succeeded"]!)
                    //     {
                    //         HttpResponseMessage responseInitialized = Get("administration/initializedcompany");
                    //         var resultInitialized = Newtonsoft.Json.Linq.JObject.Parse(responseInitialized.Content.ReadAsStringAsync().Result);

                    //         return RedirectToAction(nameof(AccountController.SignIn), "Account");
                    //     }
                    //     else
                    //     {
                    //         var errorDescription = firstResult["errors"]![0]!["description"]!.ToString();
                    //         ModelState.AddModelError(string.Empty, errorDescription);
                    //         return View(model);
                    //     }
                    // }
                    // else
                    // {
                    //     ModelState.AddModelError(string.Empty, "API returned an empty response.");
                    //     return View(model);
                    // }
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
