using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    public class BaseController : Controller
    {
        protected string? GetCurrentUserName()
        {
            var user = HttpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                return user.Identity.Name
                    ?? user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }

            return null;
        }

        protected string? GetUserNameFromRequestHeader() => GetCurrentUserName();
    }
}
