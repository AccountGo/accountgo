using Microsoft.AspNetCore.Mvc;
using Model.TaxSystem;
using Services.TaxSystem;
using System.Collections.Generic;

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
            var taxes = new List<Tax>();

            taxes.Add(new Tax()
            {
                Id = 1,
                IsActive = true,
                Rate = 12,
                TaxCode = "12",
                TaxName = "12%"
            });

            taxes.Add(new Tax()
            {
                Id = 2,
                IsActive = true,
                Rate = 10,
                TaxCode = "10",
                TaxName = "10%"
            });

            return new ObjectResult(taxes);
        }
    }
}
