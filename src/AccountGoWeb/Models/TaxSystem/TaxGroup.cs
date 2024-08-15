using System.ComponentModel.DataAnnotations;

namespace AccountGoWeb.Models.TaxSystem
{
    public class TaxGroup : BaseViewModel
    {
        [Required(ErrorMessage = "The Tax Group Name field is Required.")]
        [StringLength(50)]
        public string? Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }

        public IList<TaxGroupTax> Taxes { get; set; }

        public TaxGroup()
        {
            Taxes = new List<TaxGroupTax>();
        }
    }
}
