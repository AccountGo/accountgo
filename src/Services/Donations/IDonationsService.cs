using Core.Domain.Donations;
using Core.Domain.Error;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Donations
{
    public interface IDonationsService
    {
        IEnumerable<DonationInvoiceHeader> GetDonationInvoices();
        DonationInvoiceHeader GetDonationInvoiceById(int id);
        Result<Dto.Donations.DonationInvoice> CreateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto);
        Result<Dto.Donations.DonationInvoice> UpdateDonationInvoice(Dto.Donations.DonationInvoice donationInvoiceDto);
        Task<Result<Dto.Donations.DonationInvoice>> DeleteDonationInvoiceAsync(int id);
    }
}
