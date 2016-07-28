using System;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class SalesInvoice : BaseDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public double TotalAllocatedAmount { get; set; }

        public IList<SalesInvoiceLine> SalesInvoiceLines { get; set; }
    }

    public class SalesInvoiceLine : BaseDto
    {
        public int ItemId { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
        public int MeasurementId { get; set; }
        public string MeasurementDescription { get; set; }
        public decimal Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
    }
}
