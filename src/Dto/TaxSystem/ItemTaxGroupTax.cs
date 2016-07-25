namespace Dto.TaxSystem
{
    public class ItemTaxGroupTax : BaseDto
    {
        public Tax Tax { get; set; }
        public ItemTaxGroupTax ItemTaxGroup { get; set; }
        public bool IsExempt { get; set; }
    }
}
