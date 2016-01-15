//-----------------------------------------------------------------------
// <copyright file="Account.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
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
            ContraAccounts = new List<MainContraAccount>();
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
        public bool IsContraAccount { get; set; }
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
        public virtual ICollection<MainContraAccount> ContraAccounts { get; set; }
        public virtual ICollection<GeneralLedgerLine> GeneralLedgerLines { get; set; }


        [NotMapped]
        public decimal Balance { get { return GetBalance(); } }
        [NotMapped]
        public decimal DebitBalance { get { return GetDebitCreditBalance(TransactionTypes.Dr); } }
        [NotMapped]
        public decimal CreditBalance { get { return GetDebitCreditBalance(TransactionTypes.Cr); } }
        [NotMapped]
        public string BalanceSide { get; set; }

        private decimal GetDebitCreditBalance(TransactionTypes side)
        {
            decimal balance = 0;

            if (side == TransactionTypes.Dr)
            {
                var dr = from d in GeneralLedgerLines
                         where d.DrCr == TransactionTypes.Dr
                         select d;

                balance = dr.Sum(d => d.Amount);
            }
            else
            {
                var cr = from d in GeneralLedgerLines
                         where d.DrCr == TransactionTypes.Cr
                         select d;

                balance = cr.Sum(d => d.Amount);
            }

            return balance;
        }
        
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

            balance = drAmount - crAmount;

            if (!AccountClass.NormalBalance.Equals(Enum.GetName(typeof(TransactionTypes), DebitOrCredit(balance))))
                balance = balance * -1;

            return balance;
        }

        public bool CanPost()
        {
            if (ChildAccounts != null && ChildAccounts.Count > 0)
                return false;
            return true;
        }

        /// <summary>
        /// Used to indicate the increase or decrease on account. When there is a change in an account, that change is indicated by either debiting or crediting that account.
        /// </summary>
        /// <param name="amount">The amount to enter on account.</param>
        /// <returns></returns>
        public TransactionTypes DebitOrCredit(decimal amount)
        {
            var side = TransactionTypes.Dr;

            if (this.AccountClassId == (int)AccountClasses.Assets || this.AccountClassId == (int)AccountClasses.Expense)
            {
                if (amount > 0)
                    side = TransactionTypes.Dr;
                else
                    side = TransactionTypes.Cr;
            }

            if (this.AccountClassId == (int)AccountClasses.Liabilities || this.AccountClassId == (int)AccountClasses.Equity || this.AccountClassId == (int)AccountClasses.Revenue)
            {
                if (amount < 0)
                    side = TransactionTypes.Dr;
                else
                    side = TransactionTypes.Cr;
            }

            if (this.IsContraAccount)
            {
                if (side == TransactionTypes.Dr)
                    return TransactionTypes.Cr;
                if (side == TransactionTypes.Cr)
                    return TransactionTypes.Dr;
            }

            return side;
        }
    }
}
