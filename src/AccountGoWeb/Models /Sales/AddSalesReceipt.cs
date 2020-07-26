namespace AccountGoWeb.Models.Sales
{
    public class AddReceipt
    {
        [System.ComponentModel.DataAnnotations.Required]
        public int? AccountToDebitId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? AccountToCreditId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? CustomerId { get; set; }
        public System.DateTime ReceiptDate {get;set;}
        [ExpressiveAnnotations.Attributes.AssertThat("Amount > 0", ErrorMessage = "Amount cannot be zero.")]
        public decimal Amount { get; set; }

        public AddReceipt()
        {
            ReceiptDate = System.DateTime.Now;
        }
    }
}
