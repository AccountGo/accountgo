namespace LibraryGDB.Models.Purchasing;

public class Payment
{
    public int InvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public int VendorId { get; set; }
    public string? VendorName { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get { return InvoiceAmount - AmountPaid; } }

    [AmountToPayValidation]
    public decimal AmountToPay { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int? AccountId { get; set; }
    public System.DateTime Date { get; set; }
}
