using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNet.Mvc;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace WebApp.Controllers
{
    public class FinancialsController : Controller
    {
        public JsonResult Taxes()
        {
            var taxes = new System.Collections.Generic.List<Tax>();

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
            return new JsonResult(taxes);
            //return new ObjectResult(taxes);
        }
    }

    internal class Tax
    {
        public Tax()
        {
        }

        public int Id { get; set; }
        public bool IsActive { get; set; }
        public int Rate { get; set; }
        public string TaxCode { get; set; }
        public string TaxName { get; set; }
    }
}
