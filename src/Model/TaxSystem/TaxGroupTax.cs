namespace Model.TaxSystem
{
    public class TaxGroupTax : BaseModel
    {
        public virtual Tax Tax { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
    }
}
