using System;
using System.Collections.Generic;

namespace LibraryGDB.Models.Donations
{
    public class DonationInvoiceViewModel
    {
        public int Id { get; set; }
        public string? No { get; set; }
        public int DonorId { get; set; }
        public string? DonorName { get; set; }
        public DateTime DonationDate { get; set; }
        public decimal Amount { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Purpose { get; set; }
        public bool IsTaxReceiptIssued { get; set; }
        public string? TaxReceiptNo { get; set; }
        public bool Posted { get; set; }
        public List<DonationInvoiceLineViewModel>? DonationInvoiceLines { get; set; }
    }

    public class DonationInvoiceLineViewModel
    {
        public int Id { get; set; }
        public int? ItemId { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }
        public int? MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public string? Notes { get; set; }
    }
}
