//-----------------------------------------------------------------------
// <copyright file="Bank.cs" company="AccountGo">
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
    [Table("Bank")]
    public partial class Bank : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public BankTypes Type { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; }

        /// <summary>
        /// Required by the domain and enforced by SaveChanges / SQLite triggers.
        /// Storage stays nullable (D11) so D1 does not rebuild the Bank table.
        /// </summary>
        [Required]
        public int? AccountId { get; set; }

        public string BankName { get; set; }
        public string Number { get; set; }
        public string Address { get; set; }
        public string RoutingNumber { get; set; }
        public string Iban { get; set; }
        public string Swift { get; set; }

        /// <summary>Legacy single-default flag; backfilled into <see cref="IsDefaultForReceipts"/>.</summary>
        public bool IsDefault { get; set; }

        public bool IsDefaultForReceipts { get; set; }
        public bool IsDefaultForPayments { get; set; }

        /// <summary>Kept in sync with <see cref="ArchivedAt"/> for older readers.</summary>
        public bool IsActive { get; set; }

        public decimal? OpeningBalance { get; set; }
        public DateTime? OpeningBalanceDate { get; set; }
        public int? OpeningBalanceGeneralLedgerHeaderId { get; set; }
        public DateTime? ArchivedAt { get; set; }

        /// <summary>Null means the workspace currency (P2 multi-currency room).</summary>
        [StringLength(3)]
        public string CurrencyCode { get; set; }

        public virtual Account Account { get; set; }
    }
}
