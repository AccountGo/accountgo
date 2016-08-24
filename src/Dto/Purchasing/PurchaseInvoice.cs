using System;

namespace Dto.Purchasing
{
    public class PurchaseInvoice : BaseDto
    {
        public string No { get; set; }
        public int? PurchaseOrderHeaderId { get; set; }
        public string VendorInvoiceNo { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public decimal AmountPaid { get; set; }
        public bool IsPaid { get; set; }
        public bool Posted { get; set; }
        public int? FromPurchaseOrderId { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public bool? ReadyForPosting { get; set; }
        public System.Collections.Generic.IList<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }

        public PurchaseInvoice()
        {
            PurchaseInvoiceLines = new System.Collections.Generic.List<PurchaseInvoiceLine>();
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountLessTax();
        }

        private decimal GetTotalAmountLessTax()
        {
            decimal total = 0;
            foreach (var line in PurchaseInvoiceLines)
            {
                decimal quantityXamount = (line.Amount.Value * line.Quantity.Value);
                decimal discount = 0;
                if (line.Discount.HasValue)
                    discount = (line.Discount.Value / 100) > 0 ? (quantityXamount * (line.Discount.Value / 100)) : 0;
                total += ((line.Amount.Value * line.Quantity.Value) - discount);
            }
            return total;
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
