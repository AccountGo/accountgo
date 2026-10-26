using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.Financials;

namespace Core.Domain.Items
{
    /// <summary>A stock count, write-off, or opening balance that updates inventory and its general ledger value.</summary>
    [Table("InventoryAdjustment")]
    public partial class InventoryAdjustment : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        [Required]
        public string No { get; set; }
        public DateTime Date { get; set; }
        public int ItemId { get; set; }
        public decimal QuantityChange { get; set; }
        public decimal? UnitCost { get; set; }
        [Required]
        public string Reason { get; set; }
        public string Memo { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public int? InventoryControlJournalId { get; set; }

        public virtual Item Item { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual InventoryControlJournal InventoryControlJournal { get; set; }
    }
}
