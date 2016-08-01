using Dto.Administration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AccountGoWeb.Controllers
{
    public class AdministrationController : Controller
    {
        private readonly IConfiguration _config;
        public AdministrationController(IConfiguration config) {
            _config = config;
        }        

        public IActionResult Company()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Company(Company model)
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }
    }
}
