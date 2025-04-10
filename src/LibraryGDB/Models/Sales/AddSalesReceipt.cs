using System;
using System.ComponentModel.DataAnnotations;
using LibraryGDB.Validation;

namespace LibraryGDB.Models.Sales
{
    public class AddReceipt
    {
        [Required]
        public int? AccountToDebitId { get; set; }

        [Required]
        public int? AccountToCreditId { get; set; }

        [Required]
        public int? CustomerId { get; set; }

        public DateTime ReceiptDate { get; set; }

        [PositiveAmount]
        public decimal Amount { get; set; }

        public AddReceipt()
        {
            ReceiptDate = DateTime.Now;
        }
    }
}
