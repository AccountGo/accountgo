using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class SalesInvoice : BaseDto
    {
        public string? No { get; set; }
        [Required(ErrorMessage = "Customer is required")]
        public int CustomerId { get; set; }        
        public DateTime InvoiceDate { get; set; }
        [Required(ErrorMessage = "Payment Term is required")]
        public int? PaymentTermId { get; set; }
        public int? FromSalesOrderId { get; set; }
        public int? FromSalesDeliveryId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }
        public decimal TotalAllocatedAmount { get; set; }
        public string? ReferenceNo { get; set; }
        public bool Posted { get; set; }
        public bool? ReadyForPosting { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyEmail { get; set; }
        public decimal? TotalAmountAfterTax { get; set; }
        public IList<SalesInvoiceLine>? SalesInvoiceLines { get; set; }
        public decimal? TotalTax { get; set; }
        public SalesInvoice()
        {
            SalesInvoiceLines = new List<SalesInvoiceLine>();
            InvoiceDate = DateTime.Now;
        }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountWithoutTax();
        }

        private decimal GetTotalAmountWithoutTax()
        {
            decimal total = 0;
            foreach (var line in SalesInvoiceLines!)
            {
                if(line.Amount is null  || line.Quantity is null)
                {
                    continue;
                }

                decimal quantityXamount = (line.Amount!.Value * line.Quantity!.Value);
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
        [Required(ErrorMessage = "Item is required")]
        public int? ItemId { get; set; }
        [Required(ErrorMessage = "Quantity is required")]
        [Range(0, 1000000, ErrorMessage = "Quantity must be between 0 and 1000000")]
        public decimal? Quantity { get; set; }
        [Required(ErrorMessage = "Amount is required")]
        [Range(0, 1000000, ErrorMessage = "Amount must be between 0 and 1000000")]
        public decimal? Amount { get; set; }
        [Required(ErrorMessage = "Discount is required")]
        [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100")]
        public decimal? Discount { get; set; }
        [Required(ErrorMessage = "Measurement is required")]
        public int? MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }
    }
}