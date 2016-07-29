namespace AccountGoWeb.Models.Purchasing
{
    public class Payment
    {
        public int InvoiceId { get; set; }
        public string InvoiceNo { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public decimal InvoiceAmount { get; set; }
        public decimal AmountToPay { get; set; }
        public int AccountId { get; set; }
        public System.DateTime Date { get; set; }
    }
}
