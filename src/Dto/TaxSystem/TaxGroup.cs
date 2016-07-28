using System.Collections.Generic;

namespace Dto.TaxSystem
{
    public class TaxGroup : BaseDto
    {
        public string Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
        public IList<TaxGroupTax> Taxes { get; set; }

        public TaxGroup() {
            Taxes = new List<TaxGroupTax>();
        }
    }
}
