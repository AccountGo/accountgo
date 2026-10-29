using System.Text.Json.Serialization;

namespace Dto.Inventory
{
    /// <summary>
    /// A product or service as the Products &amp; services page shows and edits it. Accounts, tax group and units keep
    /// their values when it is saved.
    /// </summary>
    public class CatalogItem
    {
        public int Id { get; set; }
        /// <summary>The name, which is also the line text when it is picked for an invoice.</summary>
        public string? Name { get; set; }
        /// <summary>Optional short code, e.g. a SKU; unique in the workspace.</summary>
        public string? Code { get; set; }
        /// <summary>Default selling price.</summary>
        public decimal? Price { get; set; }
        /// <summary>Default purchase cost.</summary>
        public decimal? Cost { get; set; }
        public bool Archived { get; set; }
        /// <summary>Whether this item belongs to a Purchased category and uses stock tracking.</summary>
        public bool TrackStock { get; set; }
        /// <summary>The stock item's Inventory account; null for non-stock items.</summary>
        public int? InventoryAccountId { get; set; }
        /// <summary>The account credited when this item is sold; defaults to 40100 (Sales).</summary>
        public int? SalesAccountId { get; set; }
        /// <summary>Code of the sales account, for display.</summary>
        public string? SalesAccountCode { get; set; }
        /// <summary>Name of the sales account, for display.</summary>
        public string? SalesAccountName { get; set; }
        /// <summary>The cached stock quantity, in the item's purchase unit.</summary>
        public decimal QuantityOnHand { get; set; }
        /// <summary>The stock item's current moving-average unit cost.</summary>
        public decimal AverageCost { get; set; }
        /// <summary>Whether the item has at least one stock movement.</summary>
        public bool HasStockMovements { get; set; }
        /// <summary>Optional opening quantity accepted only when creating a stock item.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? OpeningQuantity { get; set; }
        /// <summary>Required when a positive opening quantity is provided.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? OpeningUnitCost { get; set; }
        /// <summary>Whether it can still be deleted: no document uses it yet. Only on a single item.</summary>
        public bool CanDelete { get; set; }
    }
}
