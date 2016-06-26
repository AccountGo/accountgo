using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Purchasing;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class PurchasingController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IPurchasingService _purchasingService;
        public PurchasingController(IAdministrationService adminService,
            IPurchasingService purchasingService)
        {
            _adminService = adminService;
            _purchasingService = purchasingService;
        }
    }
}
