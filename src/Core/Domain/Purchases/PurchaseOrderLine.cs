//-----------------------------------------------------------------------
// <copyright file="PurchaseOrderLine.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Purchases
{
    [Table("PurchaseOrderLine")]
    [PublicIdPrefix("pol")]
    public class PurchaseOrderLine : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public int PurchaseOrderHeaderId { get; set; }
        public int ItemId { get; set; }
        public int? TaxGroupId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Cost { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public virtual PurchaseOrderHeader PurhcaseOrderHeader { get; set; } = null!;
        public virtual Item Item { get; set; } = null!;
        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; } = null!;

        public PurchaseOrderLine()
        {
            PurchaseInvoiceLines = new HashSet<PurchaseInvoiceLine>();
        }

        public decimal GetRemainingQtyToInvoice()
        {
            decimal invoiced = PurchaseInvoiceLines.Sum(l => l.Quantity);
            return Quantity - invoiced;
        }
    }
}
