namespace Dto.TaxSystem
{
    public class TaxForCreation
    {
        public int SalesAccountId { get; set; }
        public int PurchaseAccountId { get; set; }

        public string TaxName { get; set; }
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }

        public int TaxGroupId { get; set; }
        public int ItemTaxGroupId { get; set; }
    }
}
