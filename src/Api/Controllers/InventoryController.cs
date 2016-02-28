using Microsoft.AspNet.Mvc;
using Services.Administration;
using Services.Inventory;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class InventoryController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IInventoryService _inventoryService;

        public InventoryController(IAdministrationService adminService,
            IInventoryService inventoryService)
        {
            _adminService = adminService;
            _inventoryService = inventoryService;
        }
    }
}
