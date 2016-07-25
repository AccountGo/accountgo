namespace Dto.TaxSystem
{
    public class TaxGroupTax : BaseDto
    {
        public virtual Tax Tax { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
    }
}
