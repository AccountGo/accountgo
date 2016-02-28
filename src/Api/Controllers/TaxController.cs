using Microsoft.AspNet.Mvc;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class TaxController : Controller
    {
        private readonly ITaxService _taxService;

        public TaxController(ITaxService taxService)
        {
            _taxService = taxService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Taxes()
        {
            return new ObjectResult(null);
        }
    }
}
