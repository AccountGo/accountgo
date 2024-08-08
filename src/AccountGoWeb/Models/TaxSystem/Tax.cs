using System.ComponentModel.DataAnnotations;

namespace AccountGoWeb.Models.TaxSystem
{
    public class Tax : BaseViewModel
    {
        [Required(ErrorMessage = "The Tax Name field is Required.")]
        [StringLength(50)]
        public string TaxName { get; set; }

        [Required(ErrorMessage = "The Tax Code field is Required.")]
        [StringLength(16)]
        public string TaxCode { get; set; }

        [Range(0, 100, ErrorMessage = "The Rate field must be between 0 and 100.")]
        public decimal Rate { get; set; }

        public bool IsActive { get; set; }
    }
}
