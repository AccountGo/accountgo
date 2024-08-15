namespace AccountGoWeb.Models.TaxSystem
{
    public class ItemTaxGroupTax
    {
        public int TaxId { get; set; }
        public int ItemTaxGroupId { get; set; }
        public bool IsExempt { get; set; }
    }
}
