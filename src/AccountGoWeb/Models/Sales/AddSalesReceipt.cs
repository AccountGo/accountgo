namespace AccountGoWeb.Models.Sales
{
    public class AddReceipt
    {
        public int DebitAccountId { get; set; }
        public int CreditAccountId { get; set; }
        public int CustomerId { get; set; }
        public System.DateTime ReceiptDate {get;set;}
        [ExpressiveAnnotations.Attributes.AssertThat("Amount > 0", ErrorMessage = "Amount cannot be zero.")]
        public decimal Amount { get; set; }

        public AddReceipt()
        {
            ReceiptDate = System.DateTime.Now;
        }
    }
}
