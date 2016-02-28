namespace Model.TaxSystem
{
    public class ItemTaxGroup : BaseModel
    {
        public string Name { get; set; }
        public bool IsFullyExempt { get; set; }
    }
}
