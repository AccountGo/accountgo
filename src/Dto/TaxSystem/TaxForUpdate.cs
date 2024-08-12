using System.ComponentModel.DataAnnotations;

namespace Dto.TaxSystem
{
    public class TaxForUpdate
    {
        public int SalesAccountId { get; set; }
        public int PurchaseAccountId { get; set; }

        public Tax? Tax { get; set; }


        public TaxGroup? TaxGroup { get; set; }
        public ItemTaxGroup? ItemTaxGroup { get; set; }
    }
}
