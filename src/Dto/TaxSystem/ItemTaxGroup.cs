using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.TaxSystem
{
    public class ItemTaxGroup : BaseDto
    {
        public string? Name { get; set; }

        [Display(Name = "Fully Exempt")]
        public bool IsFullyExempt { get; set; }
        public IList<ItemTaxGroupTax> Taxes { get; set; }

        public ItemTaxGroup() {
            Taxes = new List<ItemTaxGroupTax>();
        }
    }
}
