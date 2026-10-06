using AccountGoWeb.Models;
using Dto.Donations;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AccountGoWeb.Controllers
{
    public class DonationsController : GoodController
    {
        private readonly ILogger<DonationsController> _logger;

        public DonationsController(IConfiguration config, ILogger<DonationsController> logger)
        {
            _configuration = config;
            Models.SelectListItemHelper._config = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return RedirectToAction("DonationInvoices");
        }

        public async System.Threading.Tasks.Task<IActionResult> DonationInvoices()
        {
            ViewBag.PageContentHeader = "Donations";
            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "donations/donationinvoices");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }

            }
            return View();
        }

        [HttpGet]
        public IActionResult AddDonationInvoice()
        {
            ViewBag.PageContentHeader = "Donation";

            return View();
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> AddDonationInvoice(DonationInvoice Dto, string? addRowBtn)
        {
            if (ModelState.IsValid)
            {
                _logger.LogInformation("Posted value received: {Posted}", Dto.Posted);
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(Dto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                _logger.LogInformation("AddDonationInvoice: " + await content.ReadAsStringAsync());
                var response = Post("Donations/CreateDonationInvoice", content);

                _logger.LogInformation("AddDonationInvoice response: " + response.ToString());
                if (response.IsSuccessStatusCode)
                    return RedirectToAction("donationinvoices");
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to create donation. Status: {Status}, Error: {Error}", response.StatusCode, errorContent);
                    ModelState.AddModelError("", $"Failed to save donation: {response.StatusCode}");
                }
            }
            else
            {
                _logger.LogWarning("ModelState is invalid. Errors: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            return View(Dto);
        }

        public IActionResult DonationInvoice(int id)
        {
            ViewBag.PageContentHeader = "Donation";
            DonationInvoice? donationInvoiceModel = null;

            if (id == 0)
            {
                ViewBag.PageContentHeader = "Donation";
                return View("AddDonationInvoice");
            }
            else
            {
                donationInvoiceModel = GetAsync<DonationInvoice>("Donations/DonationInvoice?id=" + id).Result;

                if (donationInvoiceModel == null)
                {
                    return RedirectToAction("DonationInvoices");
                }

                ViewBag.Id = donationInvoiceModel.Id;
                ViewBag.DonorName = donationInvoiceModel.DonorName;
                ViewBag.DonationDate = donationInvoiceModel.DonationDate;
                ViewBag.DonationInvoiceLines = donationInvoiceModel.DonationInvoiceLines;
                ViewBag.TotalAmount = donationInvoiceModel.Amount;
            }

            return View("DonationInvoice", donationInvoiceModel);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> DonationInvoice(DonationInvoice donationInvoiceModel)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(donationInvoiceModel);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                string ReadAsStringAsync = await content.ReadAsStringAsync();
                _logger.LogInformation("SaveDonationInvoice: " + ReadAsStringAsync);
                var response = Post("Donations/UpdateDonationInvoice", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("DonationInvoices");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to update donation. Status: {Status}, Error: {Error}", response.StatusCode, errorContent);
                    ModelState.AddModelError("", $"Failed to update donation: {response.StatusCode}");
                }
            }
            else
            {
                _logger.LogWarning("ModelState is invalid. Errors: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            ViewBag.TotalAmount = donationInvoiceModel.Amount;

            return View(donationInvoiceModel);
        }

        public async Task<IActionResult> DeleteDonationInvoice(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "donations/deletedonationinvoice?id=" + id);

                if (response.IsSuccessStatusCode)
                    return RedirectToAction("DonationInvoices");
            }

            return RedirectToAction("DonationInvoices");
        }

        public IActionResult DonationInvoicePdf(int id)
        {
            var donationInvoice = GetAsync<DonationInvoice>("Donations/DonationInvoice?id=" + id).Result;
            return View(donationInvoice);
        }
    }
}
