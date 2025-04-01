using System;
using System.ComponentModel.DataAnnotations;
using LibraryGDB.Validation;

namespace LibraryGDB.Models.Purchasing
{
    public class Payment
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNo { get; set; }
        public int VendorId { get; set; }
        public string? VendorName { get; set; }
        public decimal InvoiceAmount { get; set; }
        public decimal AmountPaid { get; set; }

        public decimal Balance => InvoiceAmount - AmountPaid;

        [ValidAmountToPay(nameof(InvoiceAmount), nameof(AmountPaid))]
        public decimal AmountToPay { get; set; }

        [Required]
        public int? AccountId { get; set; }

        public DateTime Date { get; set; }
    }
}
