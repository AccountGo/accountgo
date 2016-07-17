namespace Dto.TaxSystem
{
    public class Tax : BaseDto
    {
        public string TaxName { get; set; }
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
    }

    public class TaxSystemDto {
        public System.Collections.Generic.IEnumerable<Tax> Taxes { get; set; }
        public System.Collections.Generic.IEnumerable<TaxGroup> TaxGroups { get; set; }
        public System.Collections.Generic.IEnumerable<ItemTaxGroup> ItemTaxGroups { get; set; }
    }
}
