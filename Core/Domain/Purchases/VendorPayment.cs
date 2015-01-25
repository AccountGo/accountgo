using Core.Domain.Financials;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{

    [Table("VendorPayment")]
    public partial class VendorPayment : BaseEntity
    {        
        public VendorPayment()
        { }

        public int VendorId { get; set; }
        public int? PurchaseInvoiceHeaderId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Vendor Vendor { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual PurchaseInvoiceHeader PurchaseInvoiceHeader { get; set; }
    }
}
