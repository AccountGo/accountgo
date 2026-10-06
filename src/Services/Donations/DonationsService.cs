using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Donations;
using Core.Domain.Error;
using Core.Domain.Financials;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        private readonly IRepository<Donor> _donorRepo;
        private readonly IRepository<Account> _accountRepo;

        public DonationsService(
            IMapper mapper,
            ILogger<DonationsService> logger,
            IRepository<DonationInvoiceHeader> donationInvoiceRepo,
            IRepository<DonationInvoiceLine> donationInvoiceLineRepo,
            IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo,
            IRepository<Donor> donorRepo,
            IRepository<Account> accountRepo)
            : base(sequenceNumberRepo, generalLedgerSettingRepo, paymentTermRepo, bankRepo)
        {
            _mapper = mapper;
            _logger = logger;
            _donationInvoiceRepo = donationInvoiceRepo;
            _donationInvoiceLineRepo = donationInvoiceLineRepo;
            _donorRepo = donorRepo;
            _accountRepo = accountRepo;
        }

        public IEnumerable<DonationInvoiceHeader> GetDonationInvoices()
        {
            return _donationInvoiceRepo.GetAllIncluding(
                d => d.Donor,
                d => d.GlAccount,
                d => d.DonationInvoiceLines
            );
        }

        public DonationInvoiceHeader GetDonationInvoiceById(int id)
        {
            return _donationInvoiceRepo.GetAllIncluding(
                d => d.Donor,
                d => d.GlAccount,
                d => d.DonationInvoiceLines
            ).FirstOrDefault(d => d.Id == id);
        }

        public Result<Dto.Donations.DonationInvoice> CreateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto)
        {
            try
            {
                var validation = ValidateDonation(donationInvoiceDto);
                if (validation.IsFailure)
                    return validation;

                var donationInvoice = new DonationInvoiceHeader
                {
                    No = GetNextNumber(SequenceNumberTypes.DonationInvoice).ToString(),
                    DonorId = donationInvoiceDto.DonorId,
                    Date = donationInvoiceDto.DonationDate,
                    ReferenceNo = donationInvoiceDto.ReferenceNo,
                    PaymentType = donationInvoiceDto.PaymentType,
                    Amount = donationInvoiceDto.Amount,
                    GlAccountId = donationInvoiceDto.GlAccountId,
                    Purpose = donationInvoiceDto.Purpose,
                    IsTaxReceiptIssued = donationInvoiceDto.IsTaxReceiptIssued,
                    TaxReceiptNo = donationInvoiceDto.TaxReceiptNo,
                    Posted = false,
                    ModifiedBy = "system"
                };

                _donationInvoiceRepo.Insert(donationInvoice);

                donationInvoiceDto.Id = donationInvoice.Id;
                donationInvoiceDto.No = donationInvoice.No;

                _logger.LogInformation("Created donation {DonationNo} with id {Id}", donationInvoice.No, donationInvoice.Id);

                return Result<Dto.Donations.DonationInvoice>.Success(donationInvoiceDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating donation");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("CREATE_ERROR", "The donation could not be saved. Please try again or contact your administrator."));
            }
        }

        public Result<Dto.Donations.DonationInvoice> UpdateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto)
        {
            try
            {
                var validation = ValidateDonation(donationInvoiceDto);
                if (validation.IsFailure)
                    return validation;

                var donationInvoice = GetDonationInvoiceById(donationInvoiceDto.Id);

                if (donationInvoice == null)
                {
                    var message = $"Donation {donationInvoiceDto.Id} not found.";
                    return Result<Dto.Donations.DonationInvoice>.Failure(Error.RecordNotFound(message));
                }

                donationInvoice.DonorId = donationInvoiceDto.DonorId;
                donationInvoice.Date = donationInvoiceDto.DonationDate;
                donationInvoice.ReferenceNo = donationInvoiceDto.ReferenceNo;
                donationInvoice.PaymentType = donationInvoiceDto.PaymentType;
                donationInvoice.Amount = donationInvoiceDto.Amount;
                donationInvoice.GlAccountId = donationInvoiceDto.GlAccountId;
                donationInvoice.Purpose = donationInvoiceDto.Purpose;
                donationInvoice.IsTaxReceiptIssued = donationInvoiceDto.IsTaxReceiptIssued;
                donationInvoice.TaxReceiptNo = donationInvoiceDto.TaxReceiptNo;
                donationInvoice.ModifiedBy = "system";

                _donationInvoiceRepo.Update(donationInvoice);

                _logger.LogInformation("Updated donation {Id}", donationInvoice.Id);

                return Result<Dto.Donations.DonationInvoice>.Success(donationInvoiceDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating donation");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("UPDATE_ERROR", "The donation could not be updated. Please try again or contact your administrator."));
            }
        }

        public async Task<Result<Dto.Donations.DonationInvoice>> DeleteDonationInvoiceAsync(int id)
        {
            try
            {
                var donationInvoice = GetDonationInvoiceById(id);

                if (donationInvoice == null)
                {
                    var message = $"Donation {id} not found.";
                    return Result<Dto.Donations.DonationInvoice>.Failure(Error.RecordNotFound(message));
                }

                await _donationInvoiceRepo.DeleteAsync(donationInvoice);

                _logger.LogInformation("Deleted donation {Id}", id);

                return Result<Dto.Donations.DonationInvoice>.Success(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting donation");
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("DELETE_ERROR", "The donation could not be deleted. Refresh the list and try again."));
            }
        }

        private Result<Dto.Donations.DonationInvoice> ValidateDonation(Dto.Donations.DonationInvoice donation)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(donation, new ValidationContext(donation), errors, true))
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("VALIDATION_ERROR", string.Join(" ", errors.Select(error => error.ErrorMessage))));

            if (_donorRepo.GetById(donation.DonorId) == null)
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("INVALID_DONOR", "The selected donor does not exist. Select a donor from the Donors module."));

            var account = _accountRepo.GetById(donation.GlAccountId.Value);
            int accountCode;
            if (account == null || !int.TryParse(account.AccountCode, out accountCode) || accountCode < 41000 || accountCode > 41999)
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("INVALID_GL_ACCOUNT", "Select a GL account with a code between 41000 and 41999."));

            if (decimal.Round(donation.Amount, 2) != donation.Amount)
                return Result<Dto.Donations.DonationInvoice>.Failure(new Error("INVALID_AMOUNT", "Amount may have no more than two decimal places."));

            return Result<Dto.Donations.DonationInvoice>.Success(donation);
        }
    }
}
