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
            ViewBag.PageContentHeader = "Donation Invoices";
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

                @ViewBag.Customers = Models.SelectListItemHelper.Customers();
                @ViewBag.Items = Models.SelectListItemHelper.Items();
                @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();
            }
            return View();
        }

        [HttpGet]
        public IActionResult AddDonationInvoice()
        {
            ViewBag.PageContentHeader = "Add Donation Invoice";

            DonationInvoice donationInvoiceModel = new DonationInvoice();
            donationInvoiceModel.DonationInvoiceLines = new List<DonationInvoiceLine> {
                new DonationInvoiceLine {
                    Amount = 0,
                    ItemId = 1,
                    Quantity = 1,
                }
            };
            donationInvoiceModel.No = new System.Random().Next(1, 99999).ToString();

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(donationInvoiceModel);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> AddDonationInvoice(DonationInvoice Dto, string? addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                Dto.DonationInvoiceLines!.Add(new DonationInvoiceLine
                {
                    Amount = 0,
                    Quantity = 1,
                    ItemId = 1,
                    MeasurementId = 1,
                });

                ViewBag.Customers = Models.SelectListItemHelper.Customers();
                ViewBag.Items = Models.SelectListItemHelper.Items();
                ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

                return View(Dto);
            }
            else if (ModelState.IsValid)
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
                    _logger.LogError("Failed to create donation invoice. Status: {Status}, Error: {Error}", response.StatusCode, errorContent);
                    ModelState.AddModelError("", $"Failed to save donation invoice: {response.StatusCode}");
                }
            }
            else
            {
                _logger.LogWarning("ModelState is invalid. Errors: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(Dto);
        }

        public IActionResult DonationInvoice(int id)
        {
            ViewBag.PageContentHeader = "Donation Invoice";
            DonationInvoice? donationInvoiceModel = null;

            if (id == 0)
            {
                ViewBag.PageContentHeader = "Add Donation Invoice";
                return View("AddDonationInvoice");
            }
            else
            {
                donationInvoiceModel = GetAsync<DonationInvoice>("Donations/DonationInvoice?id=" + id).Result;
                ViewBag.Id = donationInvoiceModel.Id;
                ViewBag.DonorName = donationInvoiceModel.DonorName;
                ViewBag.DonationDate = donationInvoiceModel.DonationDate;
                ViewBag.DonationInvoiceLines = donationInvoiceModel.DonationInvoiceLines;
                ViewBag.TotalAmount = donationInvoiceModel.Amount;
            }

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

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
                    _logger.LogError("Failed to update donation invoice. Status: {Status}, Error: {Error}", response.StatusCode, errorContent);
                    ModelState.AddModelError("", $"Failed to update donation invoice: {response.StatusCode}");
                }
            }
            else
            {
                _logger.LogWarning("ModelState is invalid. Errors: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            ViewBag.Customers = SelectListItemHelper.Customers();
            ViewBag.Items = SelectListItemHelper.Items();
            ViewBag.Measurements = SelectListItemHelper.Measurements();
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
