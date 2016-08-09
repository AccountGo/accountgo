using System;

namespace Dto.Purchasing
{
    public class PurchaseInvoice : BaseDto
    {
        public int? PurchaseOrderHeaderId { get; set; }
        public string InvoiceNo { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal Amount { get; set; }
        public bool IsPaid { get; set; }
        public int? FromPurchaseOrderId { get; set; }
        public System.Collections.Generic.IList<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }

        public PurchaseInvoice()
        {
            PurchaseInvoiceLines = new System.Collections.Generic.List<PurchaseInvoiceLine>();
        }
    }

    public class PurchaseInvoiceLine : BaseDto
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
    }
}
