using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Donations
{
    public class DonationInvoice : BaseDto
    {
        public string? No { get; set; }
        [Required(ErrorMessage = "Donor is required")]
        public int DonorId { get; set; }
        public DateTime DonationDate { get; set; }
        public string? DonorName { get; set; }
        public string? DonorEmail { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public string? ReferenceNo { get; set; }
        public bool Posted { get; set; }
        public bool? ReadyForPosting { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyEmail { get; set; }
        public bool IsTaxReceiptIssued { get; set; }
        public string? TaxReceiptNo { get; set; }
        public string? Purpose { get; set; }
        public IList<DonationInvoiceLine>? DonationInvoiceLines { get; set; }

        public DonationInvoice()
        {
            DonationInvoiceLines = new List<DonationInvoiceLine>();
            DonationDate = DateTime.Now;
            IsTaxReceiptIssued = false;
        }

        private decimal GetTotalAmount()
        {
            decimal total = 0;
            foreach (var line in DonationInvoiceLines!)
            {
                if (line.Amount is null || line.Quantity is null)
                {
                    continue;
                }

                decimal quantityXamount = (line.Amount!.Value * line.Quantity!.Value);
                total += quantityXamount;
            }
            return total;
        }
    }

    public class DonationInvoiceLine : BaseDto
    {
        [Required(ErrorMessage = "Item is required")]
        public int? ItemId { get; set; }
        [Required(ErrorMessage = "Quantity is required")]
        [Range(0, 1000000, ErrorMessage = "Quantity must be between 0 and 1000000")]
        public decimal? Quantity { get; set; }
        [Required(ErrorMessage = "Amount is required")]
        [Range(0, 1000000, ErrorMessage = "Amount must be between 0 and 1000000")]
        public decimal? Amount { get; set; }
        [Required(ErrorMessage = "Measurement is required")]
        public int? MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }
        public string? Notes { get; set; }
    }
}
