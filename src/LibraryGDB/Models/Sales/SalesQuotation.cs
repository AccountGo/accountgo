using System;
using System.ComponentModel.DataAnnotations;
using LibraryGDB.Validation;

namespace LibraryGDB.Models.Sales
{
    public class SalesQuotations
    {
        [Required]
        public int CustomerId { get; set; }

        [Required]
        public int PaymentTermId { get; set; }

        [Required]
        public int ItemId { get; set; }

        [Required]
        public int Quantity { get; set; }

        [GreaterThanZero(ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public DateTime Date { get; set; }

        public decimal Discount { get; set; }

        public SalesQuotations()
        {
            Date = DateTime.Now;
        }
    }
}
