using Microsoft.AspNetCore.Mvc;

namespace SampleModule.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class SampleModuleController : Controller
    {
        public IActionResult Index() => View();
    }
}
