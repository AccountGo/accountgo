namespace LibraryGDB.Models.Sales; 
public class SalesQuotations {
    [System.ComponentModel.DataAnnotations.Required]
    public int CustomerId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int PaymentTermId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int ItemId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public int Quantity { get; set; }
    [ExpressiveAnnotations.Attributes.AssertThat("Amount > 0", ErrorMessage = "Amount cannot be zero.")]
    public decimal Amount { get; set; }
    public System.DateTime Date { get; set; }
    public decimal Discount { get; set; }

    public SalesQuotations()
    {
        Date = System.DateTime.Now;
    }

}