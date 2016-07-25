namespace Dto.TaxSystem
{
    public class TaxGroup : BaseDto
    {
        public string Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
    }
}
