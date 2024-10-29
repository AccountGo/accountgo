using Dto.Sales;
using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
    public class ProposalsController : GoodController
    {
        private readonly ILogger<ProposalsController> _logger;

        public ProposalsController(IConfiguration config, ILogger<ProposalsController> logger)
        {
            _configuration = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return RedirectToAction("proposals");
        }

        public async System.Threading.Tasks.Task<IActionResult> Proposals()
        {
            ViewBag.PageContentHeader = "Sales Proposals";

            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();

                var response = await client.GetAsync(baseUri + "sales/GetSalesProposals");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        [HttpGet]
        public async System.Threading.Tasks.Task<IActionResult> AddSalesProposal()
        {
            ViewBag.PageContentHeader = "Add Sales Proposal";

            SalesProposalForCreation salesProposalModel = new SalesProposalForCreation();
            salesProposalModel.SalesProposalLines = new List<SalesProposalLineForCreation>
            {
                new SalesProposalLineForCreation
                {
                    Amount = 0,
                    Discount = 0,
                    ItemId = 1,
                    Quantity = 1,
                }
            };
            // TODO: Replace with system generated numbering.
            salesProposalModel.No = new System.Random().Next(1, 99999).ToString();

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();

            return View(salesProposalModel);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> AddSalesProposal(SalesProposalForCreation salesProposalForCreationDto, string? addRowBtn)
        {
            if (!string.IsNullOrEmpty(addRowBtn))
            {
                salesProposalForCreationDto.SalesProposalLines!.Add(new SalesProposalLineForCreation
                {
                    Amount = 0,
                    Quantity = 1,
                    Discount = 0,
                    ItemId = 1,
                    MeasurementId = 1,
                });

                ViewBag.Customers = Models.SelectListItemHelper.Customers();
                ViewBag.Items = Models.SelectListItemHelper.Items();
                ViewBag.Measurements = Models.SelectListItemHelper.Measurements();
                ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
         
                return View(salesProposalForCreationDto);
            }
            else if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(salesProposalForCreationDto);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                _logger.LogInformation("AddSalesProposal: " + await content.ReadAsStringAsync());
                var response = Post("Sales/AddSalesProposal", content);
                _logger.LogInformation("AddSalesProposal response: " + response.ToString());

                if (response.IsSuccessStatusCode)
                    return RedirectToAction("Proposals");
            }

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesProposalForCreationDto);
        }

        public async System.Threading.Tasks.Task<IActionResult> ViewSalesProposal(int id)
        {
            ViewBag.PageContentHeader = "View Sales Proposal";
            SalesProposal? salesProposalModel = null;

            salesProposalModel = GetAsync<SalesProposal>("Sales/GetSalesProposalById?id=" + id).Result;

            if (salesProposalModel is null)
            {
                // TODO : Alerts and Error Handling
                throw new NotImplementedException();
            }

            @ViewBag.Customers = Models.SelectListItemHelper.Customers();
            @ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            @ViewBag.Items = Models.SelectListItemHelper.Items();
            @ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesProposalModel);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> ViewSalesProposal(SalesProposalForUpdate salesProposalForUpdate)
        {
            if (ModelState.IsValid)
            {
                var serialize = Newtonsoft.Json.JsonConvert.SerializeObject(salesProposalForUpdate);
                var content = new StringContent(serialize);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                var response = Post("Sales/UpdateSalesProposal", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Proposals");
                }
                else
                {
                    // TODO : Alerts and Error Handling
                    throw new NotImplementedException();
                }
            }

            ViewBag.Customers = Models.SelectListItemHelper.Customers();
            ViewBag.PaymentTerms = Models.SelectListItemHelper.PaymentTerms();
            ViewBag.Items = Models.SelectListItemHelper.Items();
            ViewBag.Measurements = Models.SelectListItemHelper.Measurements();

            return View(salesProposalForUpdate.Id);
        }
        public async System.Threading.Tasks.Task<IActionResult> DeleteSalesProposal(int id)
        {
            using (var client = new HttpClient())
            {
                var baseUri = _configuration!["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri!);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.DeleteAsync(baseUri + "Sales/DeleteSalesProposal?id=" + id);

                if(response.IsSuccessStatusCode)
                    return RedirectToAction("Proposals");
                else
                {
                    // TODO : Alerts and Error Handling
                    throw new NotImplementedException();
                }
            }

            return RedirectToAction("Proposals");
        }
    }
}
