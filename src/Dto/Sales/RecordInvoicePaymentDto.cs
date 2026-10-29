using System;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class RecordInvoicePaymentDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Range(0.01, 100000000, ErrorMessage = "Payment amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        public string? PaymentMethod { get; set; } = "Bank Transfer";

        public string? ReferenceNo { get; set; }

        public string? Notes { get; set; }
    }
}
