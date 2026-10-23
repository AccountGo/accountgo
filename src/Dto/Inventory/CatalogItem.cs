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
        /// <summary>Whether it can still be deleted: no document uses it yet. Only on a single item.</summary>
        public bool CanDelete { get; set; }
    }
}
