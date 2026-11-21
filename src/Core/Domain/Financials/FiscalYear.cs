//-----------------------------------------------------------------------
// <copyright file="FiscalYear.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("FinancialYear")]
    [PublicIdPrefix("fy")]
    public partial class FinancialYear : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int CompanyId { get; set; }

        [Required]
        [StringLength(10)]
        public string FiscalYearCode { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string FiscalYearName { get; set; } = null!;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; }

        public DateTime? ClosedAt { get; set; }

        [StringLength(256)]
        public string? ClosedBy { get; set; }

        public int? ClosingGeneralLedgerHeaderId { get; set; }
    }
}
