using System;
using Core.Domain;
using Core.Domain.Items;

namespace Services.Inventory
{
    /// <summary>Records stock movements and their inventory adjustments.</summary>
    public interface IStockService
    {
        /// <summary>Receives stock at a cost and updates its moving average.</summary>
        InventoryControlJournal Receive(Item item, decimal quantity, decimal unitCost, DocumentTypes documentType, int? documentId, DateTime date, string memo);

        /// <summary>Issues stock at the item's current moving-average cost.</summary>
        InventoryControlJournal Issue(Item item, decimal quantity, DocumentTypes documentType, int? documentId, DateTime date, string memo);

        /// <summary>Reverses a stock movement with an opposite-direction movement at its original value.</summary>
        InventoryControlJournal Reverse(InventoryControlJournal original, DateTime date, string memo);

        /// <summary>Records a stock adjustment and its balanced general-ledger entry.</summary>
        InventoryAdjustment Adjust(Item item, decimal quantityChange, decimal? unitCost, string reason, DateTime date, string memo);
    }
}
