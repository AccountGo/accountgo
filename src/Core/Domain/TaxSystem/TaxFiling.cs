// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.TaxSystem
{
    /// <summary>Append-only record of a filed tax period return (or amendment).</summary>
    [Table("TaxFiling")]
    public partial class TaxFiling : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }

        [Column(TypeName = "date")]
        public DateTime PeriodStart { get; set; }

        [Column(TypeName = "date")]
        public DateTime PeriodEnd { get; set; }

        public TaxPeriodFrequency Frequency { get; set; }

        public AccountingBasis Basis { get; set; }

        public int Sequence { get; set; }

        public int? AmendsFilingId { get; set; }

        [StringLength(500)]
        public string? AmendmentReason { get; set; }

        [Column(TypeName = "date")]
        public DateTime FiledOn { get; set; }

        [StringLength(100)]
        public string? Reference { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OutputTax { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal InputTax { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NetTax { get; set; }

        [Required]
        public string SnapshotJson { get; set; } = string.Empty;

        [Required, StringLength(256)]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public TaxFiling? AmendsFiling { get; set; }
    }
}
