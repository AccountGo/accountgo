//-----------------------------------------------------------------------
// <copyright file="Account.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Financials
{
    [Table("Account")]
    public partial class Account : BaseEntity
    {
        public Account()
        {
            ChildAccounts = new HashSet<Account>();
            GeneralLedgerLines = new HashSet<GeneralLedgerLine>();
        }
        public int AccountClassId { get; set; }
        public int? ParentAccountId { get; set; }
        public AccountTypes AccountType { get; set; }
        [Required]
        [StringLength(50)]
        public string AccountCode { get; set; }
        [Required]
        [StringLength(200)]
        public string AccountName { get; set; }
        [StringLength(200)]
        public string Description { get; set; }
        public bool IsCash { get; set; }
        [Column(TypeName = "timestamp")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(8)]
        public byte[] AuditTimestamp { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Account ParentAccount { get; set; }
        public virtual AccountClass AccountClass { get; set; }

        public virtual ICollection<Account> ChildAccounts { get; set; }
        public virtual ICollection<GeneralLedgerLine> GeneralLedgerLines { get; set; }


        [NotMapped]
        public decimal Balance { get { return GetBalance(); } }
        [NotMapped]
        public string BalanceSide { get; set; }

        public decimal GetBalance()
        {
            decimal balance = 0;

            var dr = from d in GeneralLedgerLines
                     where d.DrCr == TransactionTypes.Dr
                     select d;
            var cr = from c in GeneralLedgerLines
                     where c.DrCr == TransactionTypes.Cr
                     select c;

            decimal drAmount = dr.Sum(d => d.Amount);
            decimal crAmount = cr.Sum(c => c.Amount);

            if (drAmount > crAmount)
            {
                balance = drAmount - crAmount;
                BalanceSide = "Dr";
            }
            else if (crAmount > drAmount)
            {
                balance = crAmount - drAmount;
                BalanceSide = "Cr";
            }
            else
            {
                // Both sides are equal, returns balance = 0
                BalanceSide = string.Empty;
            }

            if (!AccountClass.NormalBalance.Equals(BalanceSide) && !string.IsNullOrEmpty(BalanceSide))
                balance = balance * -1;

            return balance;
        }

        public bool CanPost()
        {
            if (ChildAccounts != null && ChildAccounts.Count > 0)
                return false;
            return true;
        }
    }
}
