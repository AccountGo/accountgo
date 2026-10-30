// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    /// <summary>Audit row for each change to <see cref="CompanySetting.BooksLockedThrough"/>.</summary>
    [Table("BooksLockChange")]
    public partial class BooksLockChange : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }

        [Column(TypeName = "date")]
        public DateTime? PreviousLockedThrough { get; set; }

        [Column(TypeName = "date")]
        public DateTime? NewLockedThrough { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }

        [Required, StringLength(256)]
        public string ChangedBy { get; set; } = string.Empty;

        public DateTime ChangedAt { get; set; }
    }
}
