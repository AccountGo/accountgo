using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels.Administration
{
    public class TaxesViewModel
    {
        public TaxesViewModel()
        {
            Taxes = new List<TaxViewModel>();
        }

        public IList<TaxViewModel> Taxes { get; set; }
    }

    public class TaxViewModel
    {
        public int? SalesAccountId { get; set; }
        public int? PurchasingAccountId { get; set; }
        [Required]
        [StringLength(50)]
        public string TaxName { get; set; }
        [Required]
        [StringLength(16)]
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
    }

    public class ItemTaxGroupViewModel
    {
        public ItemTaxGroupViewModel()
        {
            ItemTaxGroupTaxes = new List<ItemTaxGroupTaxViewModel>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsFullyExempt { get; set; }

        public IList<ItemTaxGroupTaxViewModel> ItemTaxGroupTaxes { get; set; }
    }

    public class ItemTaxGroupTaxViewModel
    {
        public int TaxId { get; set; }
        public string TaxName { get; set; }
        public decimal Rate { get; set; }
        public bool IsExempt { get; set; }
    }
}