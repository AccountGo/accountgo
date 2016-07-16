namespace Model.TaxSystem
{
    public class Tax : BaseModel
    {
        public string TaxName { get; set; }
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
    }

    public class TaxSystemModel {
        public System.Collections.Generic.IEnumerable<Tax> Taxes { get; set; }
        public System.Collections.Generic.IEnumerable<TaxGroup> TaxGroups { get; set; }
        public System.Collections.Generic.IEnumerable<ItemTaxGroup> ItemTaxGroups { get; set; }
    }
}
