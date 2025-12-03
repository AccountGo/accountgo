using System.ComponentModel.DataAnnotations;

namespace LibraryGDB.Models.Sales;

public class AddReceipt
{
    [System.ComponentModel.DataAnnotations.Required]
    public int? AccountToDebitId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int? AccountToCreditId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int? CustomerId { get; set; }
    public System.DateTime ReceiptDate {get;set;}
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount cannot be zero or negative.")]
    public decimal Amount { get; set; }

    public AddReceipt()
    {
        ReceiptDate = System.DateTime.Now;
    }
}
