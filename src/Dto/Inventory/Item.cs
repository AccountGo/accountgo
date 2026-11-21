using Dto.Common;
﻿using System.ComponentModel.DataAnnotations;

namespace Dto.Inventory
{
    public class Item : BaseDto
    {
        [PublicId("item")]
        public override int Id { get; set; }
        public string? No { get; set; }
        [Required]
        public string? Code { get; set; }
        [Required]
        public string? Description { get; set; }
        public string? PurchaseDescription { get; set; }
        public string? SellDescription { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Price { get; set; }
        public decimal? QuantityOnHand { get; set; }

        [PublicId("icat")]
        public int? ItemCategoryId { get; set; }
        [PublicId("uom")]
        public int? SmallestMeasurementId { get; set; }
        [PublicId("uom")]
        public int? SellMeasurementId { get; set; }
        [PublicId("uom")]
        public int? PurchaseMeasurementId { get; set; }
        [PublicId("ven")]
        public int? PreferredVendorId { get; set; }
        [PublicId("itg")]
        public int? ItemTaxGroupId { get; set; }
        [PublicId("acct")]
        public int? SalesAccountId { get; set; }
        [PublicId("acct")]
        public int? InventoryAccountId { get; set; }
        [PublicId("acct")]
        public int? CostOfGoodsSoldAccountId { get; set; }
        [PublicId("acct")]
        public int? InventoryAdjustmentAccountId { get; set; }
        public string? ItemTaxGroupName { get; set; }
        public string? Measurement { get; set; }
        public bool Archived { get; set; }
    }
}
