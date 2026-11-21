using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dto.Common;

namespace Dto.TaxSystem
{
    public class TaxGroup : BaseDto
    {
        [PublicId("txg")]
        public override int Id { get; set; }
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
