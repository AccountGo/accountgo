namespace Dto.Inventory.Response
{
    public class GetItemResponse
    {
        public int Id { get; set; }
        /// <summary>
        /// Mapped to No
        /// </summary>
        public string ProductNo { get; set; }
        /// <summary>
        /// Mapped to Description
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Mapped to code
        /// </summary>
        public string SKU { get; set; }
        public string ItemTaxGroup { get; set; }
        public string Measurement { get; set; }
        public decimal Cost { get; set; }
        public decimal Price { get;set; }
        public decimal QtyOnHand { get; set; }
        //Id = item.Id,
        //            Code = item.Code,
        //            Description = item.Description,
        //            ItemTaxGroupName = item.ItemTaxGroup == null ? "" : item.ItemTaxGroup.Name,
        //            Measurement = item.PurchaseMeasurement == null ? "" : item.PurchaseMeasurement.Description,
        //            Cost = item.Cost,
        //            Price = item.Price,
        //            QuantityOnHand = item.ComputeQuantityOnHand()
    }
}
