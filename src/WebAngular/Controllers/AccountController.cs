using Microsoft.AspNet.Authentication.Cookies;
using Microsoft.AspNet.Authentication.OpenIdConnect;
using Microsoft.AspNet.Http.Authentication;
using Microsoft.AspNet.Mvc;
using System.Threading.Tasks;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace WebAngular.Controllers
{
    public class AccountController : Controller
    {
        public IActionResult SignIn()
        {
            return new ChallengeResult(
                OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" });
        }

        public async Task<IActionResult> SignOut()
        {
            var callbackUrl = Url.Action("SignOutCallback", "Account", values: null, protocol: Request.Scheme);
            await HttpContext.Authentication.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.Authentication.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = callbackUrl });
            return new EmptyResult();
        }

        public IActionResult SignOutCallback()
        {
            if (HttpContext.User.Identity.IsAuthenticated)
            {
                // Redirect to home page if the user is authenticated.
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }

            return View();
        }
    }
}
