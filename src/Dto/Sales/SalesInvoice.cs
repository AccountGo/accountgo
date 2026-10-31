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
        public int? PaymentTermId { get; set; }
        public int? FromSalesOrderId { get; set; }
        public int? FromSalesDeliveryId { get; set; }
        public string? CustomerName { get; set; }
        /// <summary>Attention line under the company name, e.g. "Attn: Jane Doe".</summary>
        public string? CustomerAttention { get; set; }
        /// <summary>The customer's email as printed on the invoice.</summary>
        public string? CustomerEmail { get; set; }
        /// <summary>Where the invoice is emailed: the company email, else the Bill To contact's when the company has none.</summary>
        public string? RecipientEmail { get; set; }
        /// <summary>The Bill To contact's email, copied on the invoice email; null when it is already the recipient or there is none.</summary>
        public string? RecipientCcEmail { get; set; }
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
        public decimal ShippingHandlingCharge { get; set; }
        /// <summary>Stock warnings from saving or issuing; the invoice was still saved.</summary>
        public List<string> Warnings { get; set; } = new();
        /// <summary>Why an issued invoice couldn't be posted to the ledger; null when it was posted or is a draft.</summary>
        public string? PostingError { get; set; }

        // Standard Invoicing attributes
        public DateTime? DueDate { get; set; }
        public string? Status { get; set; }
        public int? StatusId { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceDue { get; set; }
        public string? Notes { get; set; }
        public string? PaymentInstructions { get; set; }
        /// <summary>
        /// What the PDF, email, and detail page print: the invoice's own text, else the workspace's payment bank details,
        /// else the default text. <see cref="PaymentInstructions"/> stays the stored value so editing never freezes this.
        /// </summary>
        public string? EffectivePaymentInstructions { get; set; }

        public string? PaymentInstructionsToPrint() => EffectivePaymentInstructions ?? PaymentInstructions;
        public string? CustomerAddress { get; set; }
        public string? CustomerPhone { get; set; }
        public SellerInfo Seller { get; set; } = new SellerInfo();
        public DisplayFlags Display { get; set; } = new DisplayFlags();
        public List<InvoicePaymentItemDto>? Payments { get; set; }
        /// <summary>Emails sent to the customer about this invoice, newest first.</summary>
        public List<InvoiceEmailItemDto> Emails { get; set; } = new();

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
            if (SalesInvoiceLines == null) return total;
            foreach (var line in SalesInvoiceLines)
            {
                if (line.Amount is null || line.Quantity is null)
                {
                    continue;
                }

                decimal quantityXamount = (line.Amount!.Value * line.Quantity!.Value);
                decimal discount = 0;
                if (line.Discount.HasValue)
                    discount = (line.Discount.Value / 100) > 0 ? (quantityXamount * (line.Discount.Value / 100)) : 0;
                total += Math.Round(quantityXamount - discount, 2, MidpointRounding.AwayFromZero);
            }
            return total;
        }
    }

    public class SalesInvoiceLine : BaseDto
    {
        public int? ItemId { get; set; }
        [Range(0, 1000000, ErrorMessage = "Quantity must be between 0 and 1000000")]
        public decimal? Quantity { get; set; } = 1;
        [Range(0, 1000000, ErrorMessage = "Amount must be between 0 and 1000000")]
        public decimal? Amount { get; set; }
        [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100")]
        public decimal? Discount { get; set; } = 0;
        public int? MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }
        public decimal? LineTotal { get; set; }
        public decimal? TaxRate { get; set; }
        public int? TaxId { get; set; }
        public decimal? TaxAmount { get; set; }
        /// <summary>Optional revenue account override for free-text lines; ignored for catalog-linked items.</summary>
        public int? SalesAccountId { get; set; }
        public string? SalesAccountCode { get; set; }
        public string? SalesAccountName { get; set; }
    }

    public class InvoiceEmailItemDto
    {
        public string? DispatchId { get; set; }
        public string? FromEmail { get; set; }
        public int Id { get; set; }
        public string Kind { get; set; } = "Invoice";
        public string To { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public string? SentBy { get; set; }
    }

    public class InvoicePaymentItemDto
    {
        public int Id { get; set; }
        public string? ReceiptNo { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Notes { get; set; }
    }

    public class SellerInfo
    {
        public string Name { get; set; } = "";
        public string? TaxId { get; set; }
        public List<string> AddressLines { get; set; } = new List<string>();
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class DisplayFlags
    {
        public bool ShowCustomerEmail { get; set; } = true;
        public bool ShowAttention { get; set; } = true;
        public bool ShowNotes { get; set; } = true;
        public bool ShowPaymentInstructions { get; set; } = true;
    }
}
