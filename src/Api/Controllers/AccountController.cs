using Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }
        
        //[HttpGet]
        //[AllowAnonymous]
        //public IActionResult Register(string returnUrl = null)
        //{
        //    ViewData["ReturnUrl"] = returnUrl;
        //    return View();
        //}
        
        //[HttpPost]
        //[AllowAnonymous]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null)
        //{
        //    ViewData["ReturnUrl"] = returnUrl;
        //    if (ModelState.IsValid)
        //    {
        //        var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
        //        var result = await _userManager.CreateAsync(user, model.Password);
        //        if (result.Succeeded)
        //        {
        //            // For more information on how to enable account confirmation and password reset please visit http://go.microsoft.com/fwlink/?LinkID=532713
        //            // Send an email with this link
        //            //var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        //            //var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: HttpContext.Request.Scheme);
        //            //await _emailSender.SendEmailAsync(model.Email, "Confirm your account",
        //            //    $"Please confirm your account by clicking this link: <a href='{callbackUrl}'>link</a>");
        //            await _signInManager.SignInAsync(user, isPersistent: false);
        //            //_logger.LogInformation(3, "User created a new account with password.");
        //            return RedirectToLocal(returnUrl);
        //        }
        //        //AddErrors(result);
        //    }

        //    // If we got this far, something failed, redisplay form
        //    return View(model);
        //}

        //[HttpGet]
        //[AllowAnonymous]
        //public IActionResult SignIn(string returnUrl = null)
        //{
        //    ViewData["ReturnUrl"] = returnUrl;
        //    return View();
        //}

        //[HttpPost]
        //[AllowAnonymous]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> SignIn(LoginViewModel model, string returnUrl = null)
        //{
        //    ViewData["ReturnUrl"] = returnUrl;

        //    if (ModelState.IsValid)
        //    {
        //        //var result = AcquireToken(model.Email, model.Password);
        //        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

        //        if (result.Succeeded)
        //        {
        //            //var claims = new List<Claim>();
        //            //claims.Add(new Claim(ClaimTypes.IsPersistent, model.RememberMe.ToString()));
        //            //claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        //            //claims.Add(new Claim(ClaimTypes.NameIdentifier, model.Email));
        //            //claims.Add(new Claim(ClaimTypes.Email, model.Email));
                    
        //            //var identity = new ClaimsIdentity(claims, "AuthCookie");
                    
        //            //ClaimsPrincipal principal = new ClaimsPrincipal(new[] { identity });
        //            //HttpContext.User = principal;

        //            //await HttpContext.Authentication.SignInAsync("AuthCookie", principal, new Microsoft.AspNetCore.Http.Authentication.AuthenticationProperties { IsPersistent = model.RememberMe });

        //            return RedirectToLocal(returnUrl);
        //        }
        //        else
        //        {
        //            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        //            return View(model);
        //        }
        //    }

        //    // If we got this far, something failed, redisplay form
        //    return View(model);
        //}

        //public async Task<IActionResult> SignOut()
        //{
        //    //await HttpContext.Authentication.SignOutAsync("AuthCookie");
        //    //return SignedOut();
        //    await _signInManager.SignOutAsync();
        //    return RedirectToAction(nameof(HomeController.Index), "Home");
        //}

        //public IActionResult SignedOut()
        //{
        //    if (HttpContext.User.Identity.IsAuthenticated)
        //    {
        //        return RedirectToAction(nameof(HomeController.Index), "Home");
        //    }

        //    return View();
        //}

        //private IActionResult RedirectToLocal(string returnUrl)
        //{
        //    if (Url.IsLocalUrl(returnUrl))
        //    {
        //        return Redirect(returnUrl);
        //    }
        //    else
        //    {
        //        return RedirectToAction(nameof(HomeController.Index), "Home");
        //    }
        //}
    }
}
