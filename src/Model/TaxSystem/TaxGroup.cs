namespace Model.TaxSystem
{
    public class TaxGroup : BaseModel
    {
        public string Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
    }
}
