using System;

namespace Dto.Purchasing
{
    public class PurchaseOrder : BaseDto
    {
        public string No { get; set; }
        public int? PaymentTermId { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }

        public DateTime OrderDate { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public bool Completed { get; set; }
        public string ReferenceNo { get; set; }
        public int StatusId { get; set; }

        public System.Collections.Generic.IList<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public PurchaseOrder()
        {
            PurchaseOrderLines = new System.Collections.Generic.List<PurchaseOrderLine>();
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountLessTax();
        }

        private decimal GetTotalAmountLessTax()
        {
            decimal total = 0;
            foreach (var line in PurchaseOrderLines)
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

    public class PurchaseOrderLine : BaseDto
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
        public decimal? RemainingQtyToInvoice { get; set; }
    }
}
