using System;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class SalesQuotation : BaseDto
    {
        public string No { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public DateTime QuotationDate { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public int StatusId { get; set; }

        public string SalesQuoteStatus { get; set; }

        public virtual List<SalesQuotationLine> SalesQuotationLines { get; set; }

        public SalesQuotation()
        {
            SalesQuotationLines = new List<SalesQuotationLine>();
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountLessTax();
        }

        private decimal GetTotalAmountLessTax()
        {
            decimal total = 0;
            foreach (var line in SalesQuotationLines)
            {
                decimal quantityXamount = (line.Amount.Value * line.Quantity.Value);
                decimal discount = 0;
                if(line.Discount.HasValue)
                    discount = (line.Discount.Value / 100) > 0 ? (quantityXamount * (line.Discount.Value / 100)) : 0;
                total += ((line.Amount.Value * line.Quantity.Value) - discount);
            }
            return total;
        }
    }

    public class SalesQuotationLine : BaseDto
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
    }
}
