using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    [Route("donors")]
    public class DonorsController : Controller
    {
        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("new")]
        [HttpGet("{id:int:min(1)}")]
        public IActionResult Donor(int id = 0)
        {
            return View(id);
        }
    }
}