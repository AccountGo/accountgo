using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;

namespace Dto.Inventory.Request
{
    public class CreateItem
    {
        public int Id { get; set; }
        /// <summary>
        /// Mapped to Code
        /// </summary>
        public string SKU { get; set; }
        /// <summary>
        /// Mapped To Description
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; }

        public string PurchaseDescription { get; set; }

        public string SellDescription { get; set; }

        [Range(1, double.MaxValue, ErrorMessage = "The value must be > 0")]
        [Required(ErrorMessage = "Required")]
        public decimal Cost { get; set; }

        [Range(1, double.MaxValue, ErrorMessage = "The value must be > 0")]
        [Required(ErrorMessage = "Required")]
        public decimal Price { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "The value must be >= 0")]
        public double ReorderPoint { get;set; }

        [Range(0, double.MaxValue, ErrorMessage = "The value must be >= 0")]
        public decimal InitialQtyOnHand { get;set; }

        [Required(ErrorMessage = "Required")]
        public int? ItemCategoryId { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? SmallestMeasurementId { get; set; }

        public int? SellMeasurementId { get; set; }

        public int? PurchaseMeasurementId { get; set; }

        public int? PreferredVendorId { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? ItemTaxGroupId { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? SalesAccountId { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? InventoryAccountId { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? CostOfGoodsSoldAccountId { get; set; }

        public int? InventoryAdjustmentAccountId { get; set; }

        public void FillOptionalFields()
        {
            PurchaseDescription = Name;
            SellDescription = Name;
            SmallestMeasurementId = SellMeasurementId;
            PurchaseMeasurementId = SellMeasurementId;
        }
    
    }
}
