using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Donations
{
    public class DonationInvoice : BaseDto
    {
        public string? No { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "Select a donor from the Donors module.")]
        public int DonorId { get; set; }
        public DateTime DonationDate { get; set; }
        public string? DonorName { get; set; }
        public string? DonorEmail { get; set; }
        [Range(typeof(decimal), "0.01", "9999999999999999.99", ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }
        [Required(ErrorMessage = "Select a payment type."),
         RegularExpression("^(Cash|Cheque|Credit Card|Debit Card|E-Transfer)$", ErrorMessage = "Select Cash, Cheque, Credit Card, Debit Card or E-Transfer.")]
        public string PaymentType { get; set; } = string.Empty;
        [Required(ErrorMessage = "Select a GL account with a code between 41000 and 41999."),
         Range(1, int.MaxValue, ErrorMessage = "Select a GL account with a code between 41000 and 41999.")]
        public int? GlAccountId { get; set; }
        public string GlAccountCode { get; set; } = string.Empty;
        public string GlAccountName { get; set; } = string.Empty;
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
