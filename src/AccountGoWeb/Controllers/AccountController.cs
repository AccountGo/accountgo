using AccountGoWeb.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AccountGoWeb.Controllers;

    public class AccountController : GoodController
    {
        ILogger<AccountController> _logger;
        public AccountController(IConfiguration config, ILogger<AccountController> logger)
        {
            _configuration = config;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult SignIn(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("account/send-otp")]
        [Route("account/SendOtp")]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpViewModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email))
            {
                return BadRequest(new { error = "Please provide a valid work email address." });
            }

            try
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(new { email = model.Email.Trim() });
                var content = new StringContent(serialize, System.Text.Encoding.UTF8, "application/json");

                using var client = new HttpClient();
                string baseUri = _configuration!["ApiUrl"] ?? "http://localhost:8001/api/";
                var response = await client.PostAsync(baseUri + "auth/send-otp", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many verification code requests. Please wait a few minutes before trying again." });
                    }

                    try
                    {
                        var problem = Newtonsoft.Json.Linq.JObject.Parse(responseBody);
                        var detail = problem["detail"]?.ToString() ?? "Unable to send verification code.";
                        return BadRequest(new { error = detail });
                    }
                    catch
                    {
                        return BadRequest(new { error = !string.IsNullOrWhiteSpace(responseBody) ? responseBody : "Unable to send verification code." });
                    }
                }

                return Content(responseBody, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching OTP to {Email}", model.Email);
                return BadRequest(new { error = "Unable to connect to authentication service. Please ensure API is running." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("account/verify-otp")]
        [Route("account/VerifyOtp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpViewModel model, string? returnUrl = null)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Otp))
            {
                return BadRequest(new { error = "Both email and 6-digit verification code are required." });
            }

            try
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(new { email = model.Email.Trim(), otp = model.Otp.Trim() });
                var content = new StringContent(serialize, System.Text.Encoding.UTF8, "application/json");

                using var client = new HttpClient();
                string baseUri = _configuration!["ApiUrl"] ?? "http://localhost:8001/api/";
                var response = await client.PostAsync(baseUri + "auth/verify-otp", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many verification attempts. Please wait a few minutes before trying again." });
                    }

                    try
                    {
                        var problem = Newtonsoft.Json.Linq.JObject.Parse(responseBody);
                        var detail = problem["detail"]?.ToString() ?? "Invalid or expired verification code.";
                        return BadRequest(new { error = detail });
                    }
                    catch
                    {
                        return BadRequest(new { error = "Invalid or expired code." });
                    }
                }

                var authObj = Newtonsoft.Json.Linq.JObject.Parse(responseBody);
                var accessToken = authObj["accessToken"]?.ToString();
                var userObj = authObj["user"];

                if (string.IsNullOrEmpty(accessToken) || userObj == null)
                {
                    return BadRequest(new { error = "Failed to parse authentication response." });
                }

                var email = userObj["email"]?.ToString() ?? model.Email;
                var firstName = userObj["firstName"]?.ToString() ?? string.Empty;
                var lastName = userObj["lastName"]?.ToString() ?? string.Empty;
                var fullName = $"{firstName} {lastName}".Trim();
                if (string.IsNullOrEmpty(fullName)) fullName = email;

                if (TryExtractCookieValue(response, "accountgo_refresh_token", out var rawRefreshToken))
                {
                    Response.Cookies.Append("accountgo_refresh_token", rawRefreshToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTime.UtcNow.AddDays(7),
                        Path = "/"
                    });
                }

                var claims = new List<Claim>
                {
                    new Claim("access_token", accessToken),
                    new Claim(ClaimTypes.NameIdentifier, userObj["id"]?.ToString() ?? email),
                    new Claim(ClaimTypes.Email, email),
                    new Claim(ClaimTypes.GivenName, firstName),
                    new Claim(ClaimTypes.Surname, lastName),
                    new Claim(ClaimTypes.Name, fullName)
                };

                var roles = userObj["roles"] as Newtonsoft.Json.Linq.JArray;
                if (roles != null)
                {
                    foreach (var role in roles)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
                    }
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(new[] { identity });

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                var targetUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
                return Ok(new { success = true, redirectUrl = targetUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying OTP for {Email}", model.Email);
                return BadRequest(new { error = "Authentication service error. Please try again." });
            }
        }

        public async Task<IActionResult> Logout()
        {
            Response.Cookies.Delete("accountgo_refresh_token");
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
                    Newtonsoft.Json.Linq.JObject resultAddNewUser = Newtonsoft.Json.Linq.JObject.Parse(responseAddNewUser.Content.ReadAsStringAsync().Result);

                    HttpResponseMessage? responseInitialized = null;
                    Newtonsoft.Json.Linq.JObject? resultInitialized = null;
                    if ((bool)resultAddNewUser["succeeded"]!)
                    {
                        responseInitialized = Get("administration/initializedcompany");
                        resultInitialized = Newtonsoft.Json.Linq.JObject.Parse((responseInitialized.Content.ReadAsStringAsync().Result));
                        return RedirectToAction(nameof(AccountController.SignIn), "Account");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, resultAddNewUser["errors"]![0]!["description"]!.ToString());
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

