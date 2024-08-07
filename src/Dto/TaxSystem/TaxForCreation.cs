using System.ComponentModel.DataAnnotations;

namespace Dto.TaxSystem
{
    public class TaxForCreation
    {
        public int SalesAccountId { get; set; }
        public int PurchaseAccountId { get; set; }

        [Required(ErrorMessage = "Tax Name is a required field.")]
        [MaxLength(20, ErrorMessage = "Tax Name must be less than 20 characters.")]
        public string TaxName { get; set; }

        [Required(ErrorMessage = "Tax Code is a required field.")]
        [MaxLength(20, ErrorMessage = "Tax Code must be less than 20 characters.")]
        public string TaxCode { get; set; }

        [Range(0, 100, ErrorMessage = "Rate must be between 0 and 100.")]
        public decimal Rate { get; set; }

        public bool IsActive { get; set; }

        public TaxGroup TaxGroup { get; set; }
        public ItemTaxGroup ItemTaxGroup { get; set; }
    }
}
