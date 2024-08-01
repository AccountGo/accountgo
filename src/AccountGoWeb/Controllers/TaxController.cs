using Dto.TaxSystem;
using Microsoft.AspNetCore.Mvc;
using System;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class TaxController : BaseController
    {
        public TaxController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _baseConfig = config;
        }

        public IActionResult Index()
        {
            return RedirectToAction("taxes");
        }

        public async Task<IActionResult> Taxes()
        {
            ViewBag.PageContentHeader = "Tax";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "tax/taxes");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var taxSystemDto = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.TaxSystemDto>(responseJson);
                    var taxSystemViewModel = new Models.TaxSystem.TaxSystemViewModel();
                    taxSystemViewModel.Taxes = taxSystemDto!.Taxes;
                    taxSystemViewModel.ItemTaxGroups = taxSystemDto.ItemTaxGroups;
                    taxSystemViewModel.TaxGroups = taxSystemDto.TaxGroups;

                    return View(taxSystemViewModel);
                }
            }

            return View();
        }

        public IActionResult AddNewTax()
        {
            ViewBag.PageContentHeader = "Add New Tax";

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View();
        }

        [HttpPost]
        public IActionResult AddNewTax(TaxForCreation taxForCreationDto)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForCreationDto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = Post("Tax/addnewtax", content);
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

        public async Task<IActionResult> DeleteTax(int taxId)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Tax/deletetax?id=" + taxId);

                if(response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

    }
}
