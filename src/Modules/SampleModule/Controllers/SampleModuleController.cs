using Microsoft.AspNetCore.Mvc;

namespace SampleModule.Controllers
{
    public class SampleModuleController : Controller
    {
        public IActionResult Index() => View();
    }
}
