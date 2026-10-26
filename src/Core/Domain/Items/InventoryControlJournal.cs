//-----------------------------------------------------------------------
// <copyright file="InventoryControlJournal.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("InventoryControlJournal")]
    public partial class InventoryControlJournal : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public DocumentTypes DocumentType { get; set; }
        public int? DocumentId { get; set; }
        public decimal? INQty { get; set; }
        public decimal? OUTQty { get; set; }
        public DateTime Date { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? UnitCost { get; set; }
        public decimal? TotalAmount { get; set; }
        public string Memo { get; set; }
        public bool IsReverse { get; set; }

        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
    }
}
