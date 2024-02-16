using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Api.Controllers
{
    [Authorize]
    public class BaseController : Controller
    {
        protected string GetUserNameFromRequestHeader()
        {
            string username = string.Empty;
            string authorization = string.Empty;
            Microsoft.Extensions.Primitives.StringValues val = new Microsoft.Extensions.Primitives.StringValues();

            if (HttpContext.Request.Headers.TryGetValue("UserName", out val))
                username = val[0].ToString();

            return username;
        }
    }
}
