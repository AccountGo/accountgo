//-----------------------------------------------------------------------
// <copyright file="Bank.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("Bank")]
    public partial class Bank : BaseEntity
    {
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
