using System;
using System.Collections.Generic;
using Dto.Common;

namespace Dto.Sales
{
    public class SalesOrder : BaseDto
    {
        [PublicId("so")]
        public override int Id { get; set; }
        public string No { get; set; } = "1";
        [PublicId("cus")]
        public int CustomerId { get; set; } = 1;
        public DateTime OrderDate { get; set; }
        [PublicId("term")]
        public int? PaymentTermId { get; set; } = 1;
        public string ReferenceNo { get; set; } = "1";
        public decimal? Amount { get { return GetTotalAmount(); } }
        public string CustomerNo { get; set; } = "1";
        public string CustomerName { get; set; } = "1";
        public int StatusId { get; set; } = 1;
        [PublicId("quo")]
        public int? QuotationId { get; set; } = 1;
        public IList<SalesOrderLine> SalesOrderLines { get; set; }

        public SalesOrder()
        {
            SalesOrderLines = new List<SalesOrderLine>();
            OrderDate = DateTime.Now; // TODO: Can be set by user
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountLessTax();
        }

        private decimal GetTotalAmountLessTax()
        {
            decimal total = 0;
            foreach (var line in SalesOrderLines)
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

    public class SalesOrderLine : BaseDto
    {
        [PublicId("sol")]
        public override int Id { get; set; }
        [PublicId("item")]
        public int? ItemId { get; set; }
        [PublicId("uom")]
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public string? MeasurementDescription { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }
        public decimal? RemainingQtyToInvoice { get; set; }
    }
}
