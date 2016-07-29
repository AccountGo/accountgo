namespace AccountGoWeb.Models.Purchasing
{
    public class Payment
    {
        public int InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }        
        public decimal InvoiceAmount { get; set; }
        [ExpressiveAnnotations.Attributes.AssertThat("AmountToPay <= InvoiceAmount", ErrorMessage = "Amount to pay cannot be greater than invoice amount.")]
        [ExpressiveAnnotations.Attributes.AssertThat("AmountToPay > 0", ErrorMessage = "Amount to pay cannot be zero.")]
        public decimal AmountToPay { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? AccountId { get; set; }
        public System.DateTime Date { get; set; }
    }
}
