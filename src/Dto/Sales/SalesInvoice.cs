using System;
using System.Collections;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class SalesInvoice : BaseDto
    {
        public string No { get; set; }
        public int? CustomerId { get; set; }        
        public DateTime InvoiceDate { get; set; }
        public int? PaymentTermId { get; set; }
        public int? FromSalesOrderId { get; set; }
        public int? FromSalesDeliveryId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public decimal TotalAllocatedAmount { get; set; }
        public string ReferenceNo { get; set; }
        public bool Posted { get; set; }
        public bool? ReadyForPosting { get; set; }
        public string CompanyName { get; set; }
        public string CompanyEmail { get; set; }
        public decimal? TotalAmountAfterTax { get; set; }
        public IList<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public decimal? TotalTax { get; set; }
        public SalesInvoice()
        {
            SalesInvoiceLines = new List<SalesInvoiceLine>();
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountWithoutTax();
        }

        private decimal GetTotalAmountWithoutTax()
        {
            decimal total = 0;
            foreach (var line in SalesInvoiceLines)
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

    public class SalesInvoiceLine : BaseDto
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Amount { get; set; }
        public string MeasurementDescription { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
    }
}
