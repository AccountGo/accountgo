#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Api.Data;
using Api.Filters;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Inventory;

namespace Api.Controllers
{
    /// <summary>Workspace-scoped stock valuation, movement history, and adjustments.</summary>
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule(SystemModules.Inventory)]
    public class StockController : BaseController
    {
        private static readonly string[] AdjustmentReasons = { "OpeningStock", "Count", "Damaged", "Other" };

        private readonly ApiDbContext _db;
        private readonly IStockService _stockService;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;

        /// <summary>Creates stock endpoints backed by tenant-scoped data and the stock ledger service.</summary>
        /// <param name="db">The workspace database context.</param>
        /// <param name="stockService">The service that posts stock and valuation movements.</param>
        /// <param name="tenantGuard">The guard used to reject items outside the active workspace.</param>
        public StockController(ApiDbContext db, IStockService stockService, Core.Tenancy.ITenantGuard tenantGuard)
        {
            _db = db;
            _stockService = stockService;
            _tenantGuard = tenantGuard;
        }

        /// <summary>Returns tracked items and compares their moving-average value with account 10800.</summary>
        [HttpGet("Items")]
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Items()
        {
            var items = StockItems().OrderBy(i => i.Description).ToList();
            var stockItemIds = items.Select(item => item.Id).ToArray();
            var itemsWithMovements = _db.InventoryControlJournals
                .Where(movement => stockItemIds.Contains(movement.ItemId))
                .Select(movement => movement.ItemId)
                .Distinct()
                .ToHashSet();
            var values = items.Select(item => new
            {
                id = item.Id,
                code = item.Code,
                name = item.Description,
                quantityOnHand = item.QuantityOnHand,
                averageCost = item.AverageCost,
                value = item.QuantityOnHand * item.AverageCost,
                archived = item.ArchivedAt.HasValue,
                hasMovements = itemsWithMovements.Contains(item.Id)
            }).ToList();

            var inventoryAccount = _db.Accounts.SingleOrDefault(account => account.AccountCode == "10800")
                ?? throw new InvalidOperationException("This workspace has no Inventory account (10800).");
            var inventoryAccountBalance = _db.GeneralLedgerLines
                .Where(line => line.AccountId == inventoryAccount.Id)
                .Sum(line => line.DrCr == DrOrCrSide.Dr ? line.Amount : -line.Amount);

            return Ok(new
            {
                items = values,
                totalValue = values.Sum(item => item.value),
                inventoryAccountBalance
            });
        }

        /// <summary>Returns stock ledger movements with a running quantity, newest first.</summary>
        [HttpGet("Movements")]
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Movements(int? itemId)
        {
            if (itemId.HasValue && itemId.Value <= 0)
                return BadRequest(new { message = "Choose a stock item." });

            var items = StockItems().ToList();
            var itemIds = items.Select(item => item.Id).ToArray();
            if (itemId.HasValue)
            {
                _tenantGuard.EnsureOwned<Item>(itemId.Value, nameof(itemId));
                if (!items.Any(item => item.Id == itemId.Value))
                    return BadRequest(new { message = "Choose a stock item." });
                itemIds = new[] { itemId.Value };
            }

            var itemNames = items.ToDictionary(item => item.Id, item => item.Description);
            var movements = _db.InventoryControlJournals
                .Where(movement => itemIds.Contains(movement.ItemId))
                .OrderBy(movement => movement.Date)
                .ThenBy(movement => movement.Id)
                .ToList();

            var billIds = movements
                .Where(movement => movement.DocumentType == DocumentTypes.PurchaseInvoice && movement.DocumentId.HasValue)
                .Select(movement => movement.DocumentId!.Value)
                .Distinct()
                .ToArray();
            var invoiceIds = movements
                .Where(movement => movement.DocumentType == DocumentTypes.SalesInvoice && movement.DocumentId.HasValue)
                .Select(movement => movement.DocumentId!.Value)
                .Distinct()
                .ToArray();
            var bills = _db.PurchaseInvoiceHeaders
                .Where(bill => billIds.Contains(bill.Id))
                .Select(bill => new { bill.Id, bill.No })
                .ToDictionary(bill => bill.Id, bill => bill.No);
            var invoices = _db.SalesInvoiceHeaders
                .Where(invoice => invoiceIds.Contains(invoice.Id))
                .Select(invoice => new { invoice.Id, invoice.No })
                .ToDictionary(invoice => invoice.Id, invoice => invoice.No);
            var movementIds = movements.Select(movement => movement.Id).ToArray();
            var adjustments = _db.InventoryAdjustments
                .Where(adjustment => adjustment.InventoryControlJournalId.HasValue
                    && movementIds.Contains(adjustment.InventoryControlJournalId.Value))
                .ToDictionary(adjustment => adjustment.InventoryControlJournalId!.Value);

            decimal runningQuantity = 0;
            var result = new List<object>(movements.Count);
            foreach (var movement in movements)
            {
                var incoming = movement.INQty.GetValueOrDefault();
                var outgoing = movement.OUTQty.GetValueOrDefault();
                runningQuantity += incoming - outgoing;

                string type;
                string? linkType = null;
                string? documentNo = null;
                int? documentId = movement.DocumentId;
                switch (movement.DocumentType)
                {
                    case DocumentTypes.PurchaseInvoice:
                        type = movement.IsReverse ? "Void of Bill" : "Bill";
                        linkType = "bill";
                        if (documentId.HasValue)
                            bills.TryGetValue(documentId.Value, out documentNo);
                        break;
                    case DocumentTypes.SalesInvoice:
                        type = movement.IsReverse ? "Void of Invoice" : "Invoice";
                        linkType = "invoice";
                        if (documentId.HasValue)
                            invoices.TryGetValue(documentId.Value, out documentNo);
                        break;
                    case DocumentTypes.InventoryAdjustment:
                        type = movement.IsReverse ? "Void of Adjustment" : "Adjustment";
                        if (adjustments.TryGetValue(movement.Id, out var adjustment))
                        {
                            documentId = adjustment.Id;
                            documentNo = adjustment.No;
                        }
                        break;
                    default:
                        type = movement.DocumentType.ToString();
                        break;
                }

                result.Add(new
                {
                    id = movement.Id,
                    itemId = movement.ItemId,
                    itemName = itemNames.TryGetValue(movement.ItemId, out var itemName) ? itemName : string.Empty,
                    date = movement.Date,
                    type,
                    documentId,
                    documentNo,
                    linkType,
                    @in = incoming,
                    @out = outgoing,
                    unitCost = movement.UnitCost,
                    totalCost = movement.TotalCost,
                    runningQuantity,
                    memo = movement.Memo
                });
            }

            result.Reverse();
            return Ok(result);
        }

        /// <summary>Posts an immutable stock adjustment and its matching ledger entry.</summary>
        [HttpPost("Adjust")]
        [RequirePermission(SystemPermissions.InventoryStockAdjust)]
        public IActionResult Adjust([FromBody] AdjustStockRequest? request)
        {
            if (request == null)
                return BadRequest(new { message = "Enter the stock adjustment details." });
            if (request.ItemId <= 0)
                return BadRequest(new { message = "Choose a stock item." });
            if (request.QuantityChange == 0)
                return BadRequest(new { message = "Quantity must be more than zero or less than zero." });
            if (request.QuantityChange > 0 && (!request.UnitCost.HasValue || request.UnitCost.Value < 0))
                return BadRequest(new { message = "Enter a non-negative cost per unit when adding stock." });
            if (string.IsNullOrWhiteSpace(request.Reason) || !AdjustmentReasons.Contains(request.Reason, StringComparer.Ordinal))
                return BadRequest(new { message = "Choose a valid adjustment reason." });
            if (!request.Date.HasValue)
                return BadRequest(new { message = "Choose an adjustment date." });
            if (request.Memo?.Length > 500)
                return BadRequest(new { message = "Memo must be 500 characters or fewer." });

            try
            {
                using var transaction = _db.Database.BeginTransaction(IsolationLevel.Serializable);
                _tenantGuard.EnsureOwned<Item>(request.ItemId, nameof(request.ItemId));
                var item = StockItems().SingleOrDefault(stockItem => stockItem.Id == request.ItemId);
                if (item == null)
                    return BadRequest(new { message = "Choose a stock item." });
                if (request.Reason == "OpeningStock"
                    && (item.QuantityOnHand != 0 || _db.InventoryControlJournals.Any(movement => movement.ItemId == item.Id)))
                    return BadRequest(new { message = "Opening stock can only be recorded before the item has stock movements." });

                var adjustment = _stockService.Adjust(
                    item,
                    request.QuantityChange,
                    request.UnitCost,
                    request.Reason,
                    DateTime.SpecifyKind(request.Date.Value.Date, DateTimeKind.Utc),
                    string.IsNullOrWhiteSpace(request.Memo) ? null : request.Memo.Trim());
                _db.SaveChanges();
                transaction.Commit();

                return Ok(new
                {
                    id = adjustment.Id,
                    number = adjustment.No,
                    message = "Stock adjustment posted."
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private IQueryable<Item> StockItems() =>
            _db.Items
                .Include(item => item.ItemCategory)
                .Where(item => item.ItemCategory != null && item.ItemCategory.ItemType == ItemTypes.Purchased);

        /// <summary>The values required to post a stock adjustment.</summary>
        public sealed class AdjustStockRequest
        {
            /// <summary>The stock item being adjusted.</summary>
            public int ItemId { get; set; }

            /// <summary>The signed quantity change: positive adds stock and negative removes it.</summary>
            public decimal QuantityChange { get; set; }

            /// <summary>The unit cost for incoming stock; ignored for outgoing stock.</summary>
            public decimal? UnitCost { get; set; }

            /// <summary>The adjustment reason.</summary>
            public string? Reason { get; set; }

            /// <summary>The date the adjustment is posted.</summary>
            public DateTime? Date { get; set; }

            /// <summary>An optional note shown with the movement.</summary>
            public string? Memo { get; set; }
        }
    }
}
