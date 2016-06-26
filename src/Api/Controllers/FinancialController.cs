using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class FinancialController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;

        public FinancialController(IAdministrationService adminService,
            IFinancialService financialService)
        {
            _adminService = adminService;
            _financialService = financialService;
        }
    }
}
