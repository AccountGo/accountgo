using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    public class BaseController : Controller
    {
        protected string GetUserNameFromRequestHeader()
        {
            string username = string.Empty;
            Microsoft.Extensions.Primitives.StringValues val = new Microsoft.Extensions.Primitives.StringValues();

            if (HttpContext.Request.Headers.TryGetValue("UserName", out val))
                username = val[0].ToString();

            return username;
        }
    }
}
