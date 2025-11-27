using Api.ActionFilters;
using Dto.Donations;
using Microsoft.AspNetCore.Mvc;
using Services.Donations;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class DonationsController : BaseController
    {
        private readonly IDonationsService _donationsService;
        private readonly ILogger<DonationsController> _logger;

        public DonationsController(IDonationsService donationsService, ILogger<DonationsController> logger)
        {
            _donationsService = donationsService;
            _logger = logger;
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
                    DonorName = invoice.Donor?.Party?.Name,
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
                DonorName = invoice.Donor?.Party?.Name,
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
    }
}
