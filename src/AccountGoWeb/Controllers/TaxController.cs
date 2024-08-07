using AccountGoWeb.Models.TaxSystem;
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
            var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForCreationDto);
            var content = new StringContent(serialize);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = Post("Tax/addnewtax", content);
            if (response.IsSuccessStatusCode)
                return RedirectToAction("Taxes");

            return RedirectToAction("Taxes");
        }

        public IActionResult EditTax(string tax, string taxGroup, string itemTaxGroup)
        {
            ViewBag.PageContentHeader = "Edit Tax";
          
            // Mapping Dto to View Model
            var taxObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.Tax>(tax);
            var taxGroupObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.TaxGroup>(taxGroup);
            var itemTaxGroupObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dto.TaxSystem.ItemTaxGroup>(itemTaxGroup);

            var editTaxViewModel = new Models.TaxSystem.EditTaxViewModel();
            editTaxViewModel.Tax = taxObj;
            editTaxViewModel.TaxGroup = taxGroupObj;
            editTaxViewModel.ItemTaxGroup = itemTaxGroupObj;

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View(editTaxViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> EditTax(EditTaxViewModel editTaxViewModel)
        {
            // Mapping View Model to Dto
            var taxForUpdateDto = new Dto.TaxSystem.TaxForUpdate();
            taxForUpdateDto.SalesAccountId = editTaxViewModel.SalesAccountId;
            taxForUpdateDto.PurchaseAccountId = editTaxViewModel.PurchaseAccountId;
            taxForUpdateDto.Tax = editTaxViewModel.Tax;
            taxForUpdateDto.TaxGroup = editTaxViewModel.TaxGroup;
            taxForUpdateDto.ItemTaxGroup = editTaxViewModel.ItemTaxGroup;

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();

                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                var response = await client.PutAsync(baseUri + "Tax/edittax", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Taxes");   
                }
            }

            return RedirectToAction("Taxes");
        }

        public async Task<IActionResult> DeleteTax(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _baseConfig!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Tax/deletetax?id=" + id);

                if(response.IsSuccessStatusCode)
                    return RedirectToAction("Taxes");
            }

            return RedirectToAction("Taxes");
        }

    }
}
