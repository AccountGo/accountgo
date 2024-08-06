using Dto.TaxSystem;

namespace AccountGoWeb.Models.TaxSystem
{
    public class EditTaxViewModel
    {
        public int SalesAccountId { get; set; }
        public int PurchaseAccountId { get; set; }
        public Tax? Tax { get; set; }
        public TaxGroup? TaxGroup { get; set; }
        public ItemTaxGroup? ItemTaxGroup { get; set; }
    }
}
