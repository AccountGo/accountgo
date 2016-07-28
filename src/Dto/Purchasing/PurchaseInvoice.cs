using System;

namespace Dto.Purchasing
{
    public class PurchaseInvoice : BaseDto
    {
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal Amount { get; set; }
        public bool IsPaid { get; set; }
    }

    public class PurchaseInvoiceLine : BaseDto
    {
        public decimal Amount { get; set; }
    }
}
