using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            ViewBag.PageContentHeader = "Dashboard";

            return View();
        }
    }
}
