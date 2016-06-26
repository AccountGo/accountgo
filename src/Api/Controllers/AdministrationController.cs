using Microsoft.AspNetCore.Mvc;
using Services.Administration;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class AdministrationController : Controller
    {
        private readonly IAdministrationService _adminService;

        public AdministrationController(IAdministrationService adminService)
        {
            _adminService = adminService;
        }
    }
}
