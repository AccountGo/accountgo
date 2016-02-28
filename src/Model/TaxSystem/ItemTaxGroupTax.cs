namespace Model.TaxSystem
{
    public class ItemTaxGroupTax : BaseModel
    {
        public Tax Tax { get; set; }
        public ItemTaxGroupTax ItemTaxGroup { get; set; }
        public bool IsExempt { get; set; }
    }
}
