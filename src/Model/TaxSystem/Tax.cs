namespace Model.TaxSystem
{
    public class Tax : BaseModel
    {
        public string TaxName { get; set; }
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
    }
}
