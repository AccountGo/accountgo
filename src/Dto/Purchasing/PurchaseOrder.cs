using System;

namespace Dto.Purchasing
{
    public class PurchaseOrder : BaseDto
    {
        public int? PurchaseInvoiceHeaderId { get; set; }
        public int? PaymentTermId { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }

        public DateTime OrderDate { get; set; }
        public decimal Amount { get; set; }
        public bool Completed { get; set; }
        public System.Collections.Generic.IList<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public PurchaseOrder()
        {
            PurchaseOrderLines = new System.Collections.Generic.List<PurchaseOrderLine>();
        }
    }

    public class PurchaseOrderLine : BaseDto
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
    }
}
