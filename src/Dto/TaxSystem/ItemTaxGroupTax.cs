namespace Dto.TaxSystem
{
    public class ItemTaxGroupTax : BaseDto
    {
        public int TaxId { get; set; }
        public int ItemTaxGroupId { get; set; }
        public bool IsExempt { get; set; }
    }
}
