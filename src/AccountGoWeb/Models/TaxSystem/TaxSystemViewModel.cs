using Dto.TaxSystem;

namespace AccountGoWeb.Models.TaxSystem
{
    public class TaxSystemViewModel
    {
        public System.Collections.Generic.IEnumerable<Tax> Taxes { get; set; }
        public System.Collections.Generic.IEnumerable<TaxGroup> TaxGroups { get; set; }
        public System.Collections.Generic.IEnumerable<ItemTaxGroup> ItemTaxGroups { get; set; }
    }
}
