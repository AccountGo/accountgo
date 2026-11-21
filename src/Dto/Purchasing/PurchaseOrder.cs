using System;
using Dto.Common;

namespace Dto.Purchasing
{
    public class PurchaseOrder : BaseDto
    {
        [PublicId("po")]
        public override int Id { get; set; }
        public string? No { get; set; }
        [PublicId("term")]
        public int? PaymentTermId { get; set; }
        [PublicId("ven")]
        public int VendorId { get; set; }
        public string? VendorName { get; set; }

        public DateTime OrderDate { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public bool Completed { get; set; }
        public string? ReferenceNo { get; set; }
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
                decimal quantityXamount = (line.Amount!.Value * line.Quantity!.Value);
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
        [PublicId("pol")]
        public override int Id { get; set; }
        [PublicId("item")]
        public int? ItemId { get; set; }
        [PublicId("txg")]
        public int? TaxGroupId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
        public decimal? RemainingQtyToInvoice { get; set; }
    }
}
