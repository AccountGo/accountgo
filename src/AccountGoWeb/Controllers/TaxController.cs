using AccountGoWeb.Models.TaxSystem;
using AutoMapper;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Mvc;
using System;

namespace AccountGoWeb.Controllers
{
    //[Microsoft.AspNetCore.Authorization.Authorize]
    public class TaxController : BaseController
    {
        private readonly IMapper _mapper;

        public TaxController(Microsoft.Extensions.Configuration.IConfiguration config, IMapper mapper)
        {
            _baseConfig = config;
            _mapper = mapper;
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
                    var taxSystemViewModel = _mapper.Map<Models.TaxSystem.TaxSystemViewModel>(taxSystemDto);
                  
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
            editTaxViewModel.Tax = _mapper.Map<Models.TaxSystem.Tax>(taxObj);
            editTaxViewModel.TaxGroup = _mapper.Map<Models.TaxSystem.TaxGroup>(taxGroupObj);
            editTaxViewModel.ItemTaxGroup = _mapper.Map<Models.TaxSystem.ItemTaxGroup>(itemTaxGroupObj);

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View(editTaxViewModel);
        }

        [HttpPost]
        public async Task<IActionResult> EditTax(EditTaxViewModel editTaxViewModel)
        {
            if (ModelState.IsValid)
            {
                var taxForUpdateDto = _mapper.Map<Dto.TaxSystem.TaxForUpdate>(editTaxViewModel); 

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

            @ViewBag.TaxGroups = Models.SelectListItemHelper.TaxGroups();
            @ViewBag.ItemTaxGroups = Models.SelectListItemHelper.ItemTaxGroups();

            return View(editTaxViewModel);
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
