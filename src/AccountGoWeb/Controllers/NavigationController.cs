using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class NavigationController : Controller
    {
        public IActionResult LeftNavigationTest()
        {
            var model = GetNavigationModel();
            return View("_LeftNavigationTest", model);
        }

        private object GetNavigationModel()
        {
            return new object();
        }


    }
}
