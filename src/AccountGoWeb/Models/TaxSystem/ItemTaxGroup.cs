using System.ComponentModel.DataAnnotations;

namespace AccountGoWeb.Models.TaxSystem
{
    public class ItemTaxGroup : BaseViewModel
    {
        [Required(ErrorMessage = "The Tax Group Name field is Required.")]
        [StringLength(50)]
        public string Name { get; set; }

        [Display(Name = "Fully Exempt")]
        public bool IsFullyExempt { get; set; }
        public IList<ItemTaxGroupTax> Taxes { get; set; }

        public ItemTaxGroup()
        {
            Taxes = new List<ItemTaxGroupTax>();
        }
    }
}
