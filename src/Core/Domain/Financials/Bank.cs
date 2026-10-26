//-----------------------------------------------------------------------
// <copyright file="Bank.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("Bank")]
    public partial class Bank : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public BankTypes Type { get; set; }
        public string Name { get; set; }
        public int? AccountId { get; set; }
        public string BankName { get; set; }
        public string Number { get; set; }
        public string Address { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public virtual Account Account { get; set; }
    }
}
