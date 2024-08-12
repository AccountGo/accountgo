using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.TaxSystem
{
    public class TaxGroup : BaseDto
    {
        public string? Description { get; set; }

        [Display (Name="Apply to shipping")]
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
        public IList<TaxGroupTax> Taxes { get; set; }

        public TaxGroup() {
            Taxes = new List<TaxGroupTax>();
        }
    }
}
