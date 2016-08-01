using System.Collections.Generic;

namespace Dto.TaxSystem
{
    public class ItemTaxGroup : BaseDto
    {
        public string Name { get; set; }
        public bool IsFullyExempt { get; set; }
        public IList<ItemTaxGroupTax> Taxes { get; set; }

        public ItemTaxGroup() {
            Taxes = new List<ItemTaxGroupTax>();
        }
    }
}
