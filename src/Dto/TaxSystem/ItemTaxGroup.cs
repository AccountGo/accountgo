using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dto.Common;

namespace Dto.TaxSystem
{
    public class ItemTaxGroup : BaseDto
    {
        [PublicId("itg")]
        public override int Id { get; set; }
        public string? Name { get; set; }

        [Display(Name = "Fully Exempt")]
        public bool IsFullyExempt { get; set; }
        public IList<ItemTaxGroupTax> Taxes { get; set; }

        public ItemTaxGroup() {
            Taxes = new List<ItemTaxGroupTax>();
        }
    }
}
