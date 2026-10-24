using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Services.Financial;

namespace Services.Inventory
{
    /// <summary>
    /// Records stock movements at moving-average cost. Cost follows the order movements are posted; back-dating a
    /// document does not re-cost later sales.
    /// </summary>
    public class StockService : BaseService, IStockService
    {
        private readonly IRepository<Item> _itemRepository;
        private readonly IFinancialService _financialService;

        public StockService(
            IRepository<Item> itemRepository,
            IRepository<SequenceNumber> sequenceNumberRepository,
            IRepository<Bank> bankRepository,
            IFinancialService financialService)
            : base(sequenceNumberRepository, null, null, bankRepository)
        {
            _itemRepository = itemRepository ?? throw new ArgumentNullException(nameof(itemRepository));
            _financialService = financialService ?? throw new ArgumentNullException(nameof(financialService));
        }

        /// <inheritdoc />
        public InventoryControlJournal Receive(Item item, decimal quantity, decimal unitCost, DocumentTypes documentType, int? documentId, DateTime date, string memo)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Received stock quantity must be more than zero.");
            if (unitCost < 0)
                throw new ArgumentOutOfRangeException(nameof(unitCost), "Unit cost can't be negative.");

            quantity = RoundQuantity(quantity);
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Received stock quantity must be more than zero at four decimal places.");
            unitCost = RoundUnitCost(unitCost);
            var quantityBefore = item.QuantityOnHand;
            var quantityAfter = RoundQuantity(quantityBefore + quantity);
            var totalCost = RoundMoney(quantity * unitCost);

            item.AverageCost = quantityAfter > 0 && quantityBefore > 0
                ? RoundUnitCost(((quantityBefore * item.AverageCost) + (quantity * unitCost)) / quantityAfter)
                : unitCost;
            item.QuantityOnHand = quantityAfter;

            var movement = new InventoryControlJournal
            {
                Item = item,
                ItemId = item.Id,
                MeasurementId = item.PurchaseMeasurementId ?? item.SmallestMeasurementId ?? item.SellMeasurementId ?? 1,
                DocumentType = documentType,
                DocumentId = documentId,
                INQty = quantity,
                Date = date,
                UnitCost = unitCost,
                TotalCost = totalCost,
                Memo = memo,
                IsReverse = false
            };
            item.InventoryControlJournals.Add(movement);
            return movement;
        }

        /// <inheritdoc />
        public InventoryControlJournal Issue(Item item, decimal quantity, DocumentTypes documentType, int? documentId, DateTime date, string memo)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Issued stock quantity must be more than zero.");

            quantity = RoundQuantity(quantity);
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Issued stock quantity must be more than zero at four decimal places.");
            var unitCost = RoundUnitCost(item.AverageCost);
            var totalCost = RoundMoney(quantity * unitCost);
            item.QuantityOnHand = RoundQuantity(item.QuantityOnHand - quantity);

            var movement = new InventoryControlJournal
            {
                Item = item,
                ItemId = item.Id,
                MeasurementId = item.PurchaseMeasurementId ?? item.SmallestMeasurementId ?? item.SellMeasurementId ?? 1,
                DocumentType = documentType,
                DocumentId = documentId,
                OUTQty = quantity,
                Date = date,
                UnitCost = unitCost,
                TotalCost = totalCost,
                Memo = memo,
                IsReverse = false
            };
            item.InventoryControlJournals.Add(movement);
            return movement;
        }

        /// <inheritdoc />
        public InventoryControlJournal Reverse(InventoryControlJournal original, DateTime date, string memo)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            var item = original.Item ?? _itemRepository.GetAllIncluding(i => i.InventoryControlJournals)
                .FirstOrDefault(i => i.Id == original.ItemId);
            if (item == null)
                throw new InvalidOperationException("The item for this stock movement was not found.");

            var inQuantity = RoundQuantity(original.INQty.GetValueOrDefault());
            var outQuantity = RoundQuantity(original.OUTQty.GetValueOrDefault());
            if ((inQuantity <= 0) == (outQuantity <= 0))
                throw new InvalidOperationException("A stock movement must have either an incoming or outgoing quantity.");

            var totalCost = RoundMoney(original.TotalCost.GetValueOrDefault());
            var quantityBefore = item.QuantityOnHand;
            var quantity = inQuantity > 0 ? inQuantity : outQuantity;
            var quantityAfter = RoundQuantity(quantityBefore + (inQuantity > 0 ? -quantity : quantity));

            if (inQuantity > 0)
            {
                if (quantityAfter > 0)
                    item.AverageCost = RoundUnitCost(Math.Max(0m, (quantityBefore * item.AverageCost - totalCost) / quantityAfter));
            }
            else
            {
                var positiveQuantityBefore = Math.Max(quantityBefore, 0m);
                var denominator = positiveQuantityBefore + quantity;
                if (quantityAfter > 0 && denominator > 0)
                    item.AverageCost = RoundUnitCost((positiveQuantityBefore * item.AverageCost + totalCost) / denominator);
            }
            item.QuantityOnHand = quantityAfter;

            var movement = new InventoryControlJournal
            {
                Item = item,
                ItemId = item.Id,
                MeasurementId = original.MeasurementId,
                DocumentType = original.DocumentType,
                DocumentId = original.DocumentId,
                INQty = outQuantity > 0 ? outQuantity : (decimal?)null,
                OUTQty = inQuantity > 0 ? inQuantity : (decimal?)null,
                Date = date,
                UnitCost = original.UnitCost,
                TotalCost = totalCost,
                TotalAmount = original.TotalAmount,
                Memo = memo,
                IsReverse = true
            };
            item.InventoryControlJournals.Add(movement);
            return movement;
        }

        /// <inheritdoc />
        public InventoryAdjustment Adjust(Item item, decimal quantityChange, decimal? unitCost, string reason, DateTime date, string memo)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            quantityChange = RoundQuantity(quantityChange);
            if (quantityChange == 0)
                throw new ArgumentOutOfRangeException(nameof(quantityChange), "An adjustment quantity must be more than zero or less than zero.");
            if (quantityChange > 0 && (!unitCost.HasValue || unitCost.Value < 0))
                throw new ArgumentOutOfRangeException(nameof(unitCost), "A non-negative unit cost is required when adding stock.");
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Choose a reason for the stock adjustment.", nameof(reason));

            var adjustmentNo = GetNextNumber(SequenceNumberTypes.InventoryAdjustment).ToString();
            InventoryControlJournal movement;
            var adjustmentDescription = string.IsNullOrWhiteSpace(memo) ? reason : memo;
            if (quantityChange > 0)
            {
                movement = Receive(item, quantityChange, unitCost.Value, DocumentTypes.InventoryAdjustment, null, date, adjustmentDescription);
            }
            else
            {
                movement = Issue(item, Math.Abs(quantityChange), DocumentTypes.InventoryAdjustment, null, date, adjustmentDescription);
            }

            var adjustment = new InventoryAdjustment
            {
                Item = item,
                ItemId = item.Id,
                No = adjustmentNo,
                Date = date,
                QuantityChange = RoundQuantity(quantityChange),
                UnitCost = quantityChange > 0 ? RoundUnitCost(unitCost.Value) : null,
                Reason = reason,
                Memo = memo,
                InventoryControlJournal = movement
            };

            var amount = movement.TotalCost.GetValueOrDefault();
            if (amount > 0)
            {
                int inventoryAccountId = item.InventoryAccountId
                    ?? throw new InvalidOperationException("This stock item has no Inventory account.");
                int adjustmentAccountId;
                if (quantityChange > 0)
                {
                    adjustmentAccountId = reason == "OpeningStock"
                        ? GetAccountId("30700", "Opening Balance Equity")
                        : item.InventoryAdjustmentAccountId
                            ?? throw new InvalidOperationException("This stock item has no Inventory Adjustments account.");
                }
                else
                {
                    adjustmentAccountId = item.InventoryAdjustmentAccountId
                        ?? throw new InvalidOperationException("This stock item has no Inventory Adjustments account.");
                }

                var header = _financialService.CreateGeneralLedgerHeader(DocumentTypes.InventoryAdjustment, date, adjustmentDescription);
                if (quantityChange > 0)
                {
                    header.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, inventoryAccountId, amount));
                    header.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, adjustmentAccountId, amount));
                }
                else
                {
                    header.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Dr, adjustmentAccountId, amount));
                    header.GeneralLedgerLines.Add(_financialService.CreateGeneralLedgerLine(DrOrCrSide.Cr, inventoryAccountId, amount));
                }
                _financialService.ValidateGeneralLedgerEntry(header);
                adjustment.GeneralLedgerHeader = header;
            }

            item.InventoryAdjustments.Add(adjustment);
            return adjustment;
        }

        private int GetAccountId(string code, string name)
        {
            var account = _financialService.GetAccounts().FirstOrDefault(a => a.AccountCode == code);
            return account?.Id ?? throw new InvalidOperationException($"This workspace has no {name} account.");
        }

        private static decimal RoundQuantity(decimal value) => Math.Round(value, 4, MidpointRounding.ToEven);
        private static decimal RoundUnitCost(decimal value) => Math.Round(value, 4, MidpointRounding.ToEven);
        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);
    }
}
