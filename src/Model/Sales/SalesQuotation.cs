using System;
using System.Collections.Generic;

namespace Model.Sales
{
    public class SalesQuotation
    {
        public int CustomerId { get; set; }
        public DateTime QuotationDate { get; set; }
        public int PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public virtual IList<SalesQuotationLine> SalesQuotationLines { get; set; }
    }

    public class SalesQuotationLine
    {
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }
    }
}
