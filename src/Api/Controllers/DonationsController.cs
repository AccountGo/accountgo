using Api.ActionFilters;
using Dto.Donations;
using Microsoft.AspNetCore.Mvc;
using Services.Donations;
using Services.Financial;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class DonationsController : BaseController
    {
        private readonly IDonationsService _donationsService;
        private readonly ILogger<DonationsController> _logger;
        private readonly IFinancialService _financialService;

        public DonationsController(IDonationsService donationsService, ILogger<DonationsController> logger, IFinancialService financialService)
        {
            _donationsService = donationsService;
            _logger = logger;
            _financialService = financialService;
        }

        [HttpGet("DonationAccounts")]
        public IActionResult DonationAccounts()
        {
            var accounts = _financialService.GetAccounts().AsEnumerable()
                .Where(account => int.TryParse(account.AccountCode, out var code) && code >= 41000 && code <= 41999)
                .OrderBy(account => account.AccountCode)
                .Select(account => new Dto.Financial.Account
                {
                    Id = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName
                }).ToList();
            return Ok(accounts);
        }

        [HttpGet]
        [Route("DonationInvoices")]
        public IActionResult DonationInvoices()
        {
            _logger.LogInformation("Getting all donation invoices");

            var donationInvoices = _donationsService.GetDonationInvoices();
            IList<DonationInvoice> donationInvoicesDto = new List<DonationInvoice>();

            foreach (var invoice in donationInvoices)
            {
                var invoiceDto = new DonationInvoice
                {
                    Id = invoice.Id,
                    No = invoice.No,
                    DonorId = invoice.DonorId,
                    DonorName = GetDonorName(invoice.Donor),
                    Amount = invoice.Amount,
                    PaymentType = invoice.PaymentType,
                    GlAccountId = invoice.GlAccountId,
                    GlAccountCode = invoice.GlAccount?.AccountCode ?? string.Empty,
                    GlAccountName = invoice.GlAccount?.AccountName ?? string.Empty,
                    DonationDate = invoice.Date,
                    ReferenceNo = invoice.ReferenceNo,
                    Purpose = invoice.Purpose,
                    IsTaxReceiptIssued = invoice.IsTaxReceiptIssued,
                    TaxReceiptNo = invoice.TaxReceiptNo,
                    Posted = invoice.Posted
                };

                foreach (var line in invoice.DonationInvoiceLines)
                {
                    var lineDto = new DonationInvoiceLine
                    {
                        Id = line.Id,
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Amount = line.Amount,
                        Notes = line.Notes
                    };
                    invoiceDto.DonationInvoiceLines!.Add(lineDto);
                }

                donationInvoicesDto.Add(invoiceDto);
            }

            return Ok(donationInvoicesDto);
        }

        [HttpGet]
        [Route("DonationInvoice")]
        public IActionResult DonationInvoice(int id)
        {
            _logger.LogInformation("Getting donation invoice with id: {Id}", id);

            var invoice = _donationsService.GetDonationInvoiceById(id);

            if (invoice == null)
                return NotFound();

            var invoiceDto = new DonationInvoice
            {
                Id = invoice.Id,
                No = invoice.No,
                DonorId = invoice.DonorId,
                DonorName = GetDonorName(invoice.Donor),
                Amount = invoice.Amount,
                PaymentType = invoice.PaymentType,
                GlAccountId = invoice.GlAccountId,
                GlAccountCode = invoice.GlAccount?.AccountCode ?? string.Empty,
                GlAccountName = invoice.GlAccount?.AccountName ?? string.Empty,
                DonationDate = invoice.Date,
                ReferenceNo = invoice.ReferenceNo,
                Purpose = invoice.Purpose,
                IsTaxReceiptIssued = invoice.IsTaxReceiptIssued,
                TaxReceiptNo = invoice.TaxReceiptNo,
                Posted = invoice.Posted
            };

            foreach (var line in invoice.DonationInvoiceLines)
            {
                var lineDto = new DonationInvoiceLine
                {
                    Id = line.Id,
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    Quantity = line.Quantity,
                    Amount = line.Amount,
                    Notes = line.Notes
                };
                invoiceDto.DonationInvoiceLines!.Add(lineDto);
            }

            return Ok(invoiceDto);
        }

        [HttpPost("CreateDonationInvoice")]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult CreateDonationInvoice([FromBody] DonationInvoice donationInvoiceDto)
        {
            var result = _donationsService.CreateDonationInvoice(donationInvoiceDto);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(result.Value);
        }

        [HttpPost("UpdateDonationInvoice")]
        [ServiceFilter(typeof(ValidationFilterAttribute))]
        public IActionResult UpdateDonationInvoice([FromBody] DonationInvoice donationInvoiceDto)
        {
            var result = _donationsService.UpdateDonationInvoice(donationInvoiceDto);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(result.Value);
        }

        [HttpDelete("DeleteDonationInvoice")]
        public async Task<IActionResult> DeleteDonationInvoice(int id)
        {
            var result = await _donationsService.DeleteDonationInvoiceAsync(id);

            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return NoContent();
        }

        private static string GetDonorName(Core.Domain.Donations.Donor donor)
        {
            if (donor == null) return string.Empty;
            return donor.DonorType == "Company" ? donor.CompanyName : (donor.FirstName + " " + donor.LastName).Trim();
        }
    }
}
