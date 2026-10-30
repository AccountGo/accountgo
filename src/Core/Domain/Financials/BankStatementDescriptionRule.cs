// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.Purchases;

namespace Core.Domain.Financials
{
    /// <summary>
    /// Remembers vendor + expense category for a normalized bank description (#436 I4).
    /// </summary>
    [Table("BankStatementDescriptionRule")]
    public partial class BankStatementDescriptionRule : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }

        /// <summary>Normalized payee/memo key (lowercase, collapsed whitespace).</summary>
        [Required, StringLength(200)]
        public string DescriptionKey { get; set; } = string.Empty;

        public int? VendorId { get; set; }
        public int? ExpenseAccountId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }

        public virtual Vendor? Vendor { get; set; }
        public virtual Account? ExpenseAccount { get; set; }
    }
}
