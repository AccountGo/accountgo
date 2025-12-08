using Core.Domain.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Donations
{
    [Table("DonationInvoiceHeader")]
    public partial class DonationInvoiceHeader : BaseEntity
    {
        public DonationInvoiceHeader()
        {
            DonationInvoiceLines = new HashSet<DonationInvoiceLine>();
        }

        public int DonorId { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public string ReferenceNo { get; set; }
        public string Purpose { get; set; }
        public bool IsTaxReceiptIssued { get; set; }
        public string TaxReceiptNo { get; set; }
        public bool Posted { get; set; }

        public virtual Sales.Customer Donor { get; set; }
        public virtual ICollection<DonationInvoiceLine> DonationInvoiceLines { get; set; }

        public decimal ComputeTotalAmount()
        {
            decimal totalAmount = 0;
            foreach (var line in DonationInvoiceLines)
            {
                totalAmount += line.Quantity * line.Amount;
            }
            return totalAmount;
        }
    }
}
