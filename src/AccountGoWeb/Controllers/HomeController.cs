using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
