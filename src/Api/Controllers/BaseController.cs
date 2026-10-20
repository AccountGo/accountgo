using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    public class BaseController : Controller
    {
        protected string GetCurrentUserName()
        {
            var user = HttpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                return user.Identity.Name
                    ?? user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? "authenticated-user";
            }

            if (HttpContext.Request.Headers.TryGetValue("UserName", out var val) && !string.IsNullOrWhiteSpace(val[0]))
                return val[0]!;

            return "anonymous";
        }

        protected string GetUserNameFromRequestHeader() => GetCurrentUserName();
    }
}
