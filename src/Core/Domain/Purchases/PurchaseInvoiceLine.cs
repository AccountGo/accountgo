//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoiceLine.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Core.Domain.Financials;
using Core.Domain.Items;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    /// <summary>
    /// A bill line: what was bought, at what cost, and the account it is booked to. Amount is the unit cost; tax is
    /// worked out from the line's tax when the bill is saved and stored, so the bill keeps the tax it was entered with.
    /// </summary>
    [Table("PurchaseInvoiceLine")]
    public partial class PurchaseInvoiceLine : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int PurchaseInvoiceHeaderId { get; set; }
        public int ItemId { get; set; }
        public int? TaxGroupId { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        /// <summary>The account the line is booked to: an expense, or an asset such as inventory or equipment.</summary>
        public int? AccountId { get; set; }
        public int? TaxId { get; set; }
        public decimal Quantity { get; set; }
        public decimal? ReceivedQuantity { get; set; }
        public decimal? Cost { get; set; }
        /// <summary>Percent off.</summary>
        public decimal? Discount { get; set; }
        public decimal Amount { get; set; }
        public decimal TaxAmount { get; set; }
        /// <summary>The line's own text; null on lines saved before it was stored, which show the item's.</summary>
        public string? Description { get; set; }

        public virtual PurchaseInvoiceHeader? PurchaseInvoiceHeader { get; set; }
        public virtual Item? Item { get; set; }
        public virtual Account? Account { get; set; }
        public virtual TaxSystem.Tax? Tax { get; set; }
        public virtual InventoryControlJournal? InventoryControlJournal { get; set; }
        public virtual PurchaseOrderLine? PurchaseOrderLine { get; set; }

        /// <summary>Quantity times cost, less the discount, to the cent.</summary>
        [NotMapped]
        public decimal Subtotal => SubtotalOf(Quantity, Amount, Discount);

        [NotMapped]
        public string DisplayDescription => Description ?? Item?.Description ?? "Item";

        public static decimal SubtotalOf(decimal quantity, decimal amount, decimal? discount)
        {
            var gross = quantity * amount;
            var off = discount is > 0 ? gross * (discount.Value / 100m) : 0;
            return Math.Round(gross - off, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>Tax on top of the subtotal at the tax's rate, to the cent.</summary>
        public static decimal TaxOf(decimal subtotal, decimal? rate) =>
            rate is > 0 ? Math.Round(subtotal * rate.Value / 100m, 2, MidpointRounding.AwayFromZero) : 0;

        /// <summary>
        /// Net amount for a tax-inclusive (gross) total so that net + <see cref="TaxOf"/>(net, rate)
        /// equals the gross to the cent. Used when a bank statement amount already includes tax.
        /// </summary>
        public static decimal NetOfInclusive(decimal inclusive, decimal? rate)
        {
            if (rate is not > 0)
                return inclusive;
            var net = Math.Round(inclusive / (1m + rate.Value / 100m), 2, MidpointRounding.AwayFromZero);
            var total = net + TaxOf(net, rate);
            if (total == inclusive)
                return net;
            var nudged = total < inclusive ? net + 0.01m : net - 0.01m;
            if (nudged + TaxOf(nudged, rate) == inclusive)
                return nudged;
            return net;
        }
    }
}
