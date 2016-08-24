using AccountGoWeb.Models.Financial;
using Dto.Administration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace AccountGoWeb.Controllers
{
    public class AdministrationController : BaseController
    {
        public AdministrationController(IConfiguration config) {
            _baseConfig = config;
            Models.SelectListItemHelper._config = config;
        }        

        public IActionResult Company()
        {
            ViewBag.PageContentHeader = "Company";
            var model = GetAsync<Company>("administration/company").Result;
            if (model == null)
                model = new Dto.Administration.Company();
            return View(model);
        }

        [HttpPost]
        public IActionResult Company(Company model)
        {
            ViewBag.PageContentHeader = "Company";
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(model);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = PostAsync("administration/savecompany", content);

                return View(model);
            }            
            return View(model);
        }

        public IActionResult Settings()
        {
            ViewBag.PageContentHeader = "Setup and Configuration";
            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            return View();
        }

        [HttpPost]
        public IActionResult SaveSettings(GeneralLedgerSetting model)
        {
            if(ModelState.IsValid)
            {

            }
            ViewBag.Accounts = Models.SelectListItemHelper.Accounts();
            ViewBag.PageContentHeader = "Setup and Configuration";
            return RedirectToAction(nameof(AdministrationController.Settings));
        }
    }
}
