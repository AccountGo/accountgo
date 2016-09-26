using System;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class SalesOrder : BaseDto
    {
        public string No { get; set; }
        public int? CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public decimal? Amount { get { return GetTotalAmount(); } }
        public string CustomerNo { get; set; }
        public string CustomerName { get; set; }
        public int StatusId { get; set; }

        public int? QuotationId { get; set; }
        public IList<SalesOrderLine> SalesOrderLines { get; set; }

        public SalesOrder()
        {
            SalesOrderLines = new List<SalesOrderLine>();
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
                decimal quantityXamount = (line.Amount.Value * line.Quantity.Value);
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
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public string MeasurementDescription { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
        public decimal? RemainingQtyToInvoice { get; set; }
    }
}
