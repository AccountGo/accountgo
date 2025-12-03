using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Donations;
using Core.Domain.Error;
using Core.Domain.Financials;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Donations
{
    public class DonationsService : BaseService, IDonationsService
    {
        private readonly IMapper _mapper;
        private readonly ILogger<DonationsService> _logger;
        private readonly IRepository<DonationInvoiceHeader> _donationInvoiceRepo;
        private readonly IRepository<DonationInvoiceLine> _donationInvoiceLineRepo;

        public DonationsService(
            IMapper mapper,
            ILogger<DonationsService> logger,
            IRepository<DonationInvoiceHeader> donationInvoiceRepo,
            IRepository<DonationInvoiceLine> donationInvoiceLineRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo)
            : base(sequenceNumberRepo, generalLedgerSettingRepo, paymentTermRepo, bankRepo)
        {
            _mapper = mapper;
            _logger = logger;
            _donationInvoiceRepo = donationInvoiceRepo;
            _donationInvoiceLineRepo = donationInvoiceLineRepo;
        }

        public IEnumerable<DonationInvoiceHeader> GetDonationInvoices()
        {
            return _donationInvoiceRepo.GetAllIncluding(
                d => d.Donor,
                d => d.Donor.Party,
                d => d.DonationInvoiceLines
            );
        }

        public DonationInvoiceHeader GetDonationInvoiceById(int id)
        {
            return _donationInvoiceRepo.GetAllIncluding(
                d => d.Donor,
                d => d.Donor.Party,
                d => d.DonationInvoiceLines
            ).FirstOrDefault(d => d.Id == id);
        }

        public Result<Dto.Donations.DonationInvoice> CreateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto)
        {
            try
            {
                var donationInvoice = new DonationInvoiceHeader
                {
                    No = GetNextNumber(SequenceNumberTypes.DonationInvoice).ToString(),
                    DonorId = donationInvoiceDto.DonorId,
                    Date = donationInvoiceDto.DonationDate,
                    ReferenceNo = donationInvoiceDto.ReferenceNo,
                    Purpose = donationInvoiceDto.Purpose,
                    IsTaxReceiptIssued = donationInvoiceDto.IsTaxReceiptIssued,
                    TaxReceiptNo = donationInvoiceDto.TaxReceiptNo,
                    Posted = donationInvoiceDto.Posted,
                    ModifiedBy = "system"
                };

                foreach (var lineDto in donationInvoiceDto.DonationInvoiceLines!)
                {
                    var line = new DonationInvoiceLine
                    {
                        ItemId = lineDto.ItemId.GetValueOrDefault(),
                        MeasurementId = lineDto.MeasurementId.GetValueOrDefault(),
                        Quantity = lineDto.Quantity.GetValueOrDefault(),
                        Amount = lineDto.Amount.GetValueOrDefault(),
                        Notes = lineDto.Notes,
                        ModifiedBy = "system"
                    };
                    donationInvoice.DonationInvoiceLines.Add(line);
                }

                _donationInvoiceRepo.Insert(donationInvoice);

                donationInvoiceDto.Id = donationInvoice.Id;
                donationInvoiceDto.No = donationInvoice.No;

                _logger.LogInformation("Created donation invoice {InvoiceNo} with id {Id}", donationInvoice.No, donationInvoice.Id);

                return Result<Dto.Donations.DonationInvoice>.Success(donationInvoiceDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating donation invoice");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("CREATE_ERROR", ex.Message));
            }
        }

        public Result<Dto.Donations.DonationInvoice> UpdateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto)
        {
            try
            {
                var donationInvoice = GetDonationInvoiceById(donationInvoiceDto.Id);

                if (donationInvoice == null)
                {
                    var message = $"Donation invoice {donationInvoiceDto.Id} not found.";
                    return Result<Dto.Donations.DonationInvoice>.Failure(Error.RecordNotFound(message));
                }

                donationInvoice.DonorId = donationInvoiceDto.DonorId;
                donationInvoice.Date = donationInvoiceDto.DonationDate;
                donationInvoice.ReferenceNo = donationInvoiceDto.ReferenceNo;
                donationInvoice.Purpose = donationInvoiceDto.Purpose;
                donationInvoice.IsTaxReceiptIssued = donationInvoiceDto.IsTaxReceiptIssued;
                donationInvoice.TaxReceiptNo = donationInvoiceDto.TaxReceiptNo;
                donationInvoice.Posted = donationInvoiceDto.Posted;
                donationInvoice.ModifiedBy = "system";

                // Remove deleted lines
                var linesToDelete = donationInvoice.DonationInvoiceLines
                    .Where(line => !donationInvoiceDto.DonationInvoiceLines!.Any(x => x.Id == line.Id))
                    .ToList();

                foreach (var line in linesToDelete)
                {
                    _donationInvoiceLineRepo.Delete(line);
                }

                // Update or add lines
                foreach (var lineDto in donationInvoiceDto.DonationInvoiceLines!)
                {
                    if (lineDto.Id > 0)
                    {
                        var existingLine = donationInvoice.DonationInvoiceLines.FirstOrDefault(l => l.Id == lineDto.Id);
                        if (existingLine != null)
                        {
                            existingLine.ItemId = lineDto.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = lineDto.MeasurementId.GetValueOrDefault();
                            existingLine.Quantity = lineDto.Quantity.GetValueOrDefault();
                            existingLine.Amount = lineDto.Amount.GetValueOrDefault();
                            existingLine.Notes = lineDto.Notes;
                            existingLine.ModifiedBy = "system";
                        }
                    }
                    else
                    {
                        var newLine = new DonationInvoiceLine
                        {
                            DonationInvoiceHeaderId = donationInvoice.Id,
                            ItemId = lineDto.ItemId.GetValueOrDefault(),
                            MeasurementId = lineDto.MeasurementId.GetValueOrDefault(),
                            Quantity = lineDto.Quantity.GetValueOrDefault(),
                            Amount = lineDto.Amount.GetValueOrDefault(),
                            Notes = lineDto.Notes,
                            ModifiedBy = "system"
                        };
                        donationInvoice.DonationInvoiceLines.Add(newLine);
                    }
                }

                _donationInvoiceRepo.Update(donationInvoice);

                _logger.LogInformation("Updated donation invoice {Id}", donationInvoice.Id);

                return Result<Dto.Donations.DonationInvoice>.Success(donationInvoiceDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating donation invoice");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("UPDATE_ERROR", ex.Message));
            }
        }

        public async Task<Result<Dto.Donations.DonationInvoice>> DeleteDonationInvoiceAsync(int id)
        {
            try
            {
                var donationInvoice = GetDonationInvoiceById(id);

                if (donationInvoice == null)
                {
                    var message = $"Donation invoice {id} not found.";
                    return Result<Dto.Donations.DonationInvoice>.Failure(Error.RecordNotFound(message));
                }

                await _donationInvoiceRepo.DeleteAsync(donationInvoice);

                _logger.LogInformation("Deleted donation invoice {Id}", id);

                return Result<Dto.Donations.DonationInvoice>.Success(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting donation invoice");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("DELETE_ERROR", ex.Message));
            }
        }
    }
}
