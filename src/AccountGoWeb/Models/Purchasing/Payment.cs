namespace AccountGoWeb.Models.Purchasing
{
    public class Payment
    {
        public int InvoiceId { get; set; }
        public int VendorId { get; set; }
        public double InvoiceAmount { get; set; }
        public double AmountToPay { get; set; }
        public int AccountId { get; set; }
        public System.DateTime Date { get; set; }
    }
}
