using System.ComponentModel.DataAnnotations;

namespace Dto.Inventory
{
    public class Item : BaseDto
    {
        public string No { get; set; }
        [Required]
        public string Code { get; set; }
        [Required]
        public string Description { get; set; }
        public string PurchaseDescription { get; set; }
        public string SellDescription { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Price { get; set; }
        public decimal? QuantityOnHand { get; set; }

        public int? ItemCategoryId { get; set; }
        public int? SmallestMeasurementId { get; set; }
        public int? SellMeasurementId { get; set; }
        public int? PurchaseMeasurementId { get; set; }
        public int? PreferredVendorId { get; set; }
        public int? ItemTaxGroupId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? InventoryAccountId { get; set; }
        public int? CostOfGoodsSoldAccountId { get; set; }
        public int? InventoryAdjustmentAccountId { get; set; }
        public string ItemTaxGroupName { get; set; }
        public string Measurement { get; set; }
    }
}
