using System.Collections.Generic;

namespace Dto.Financial
{
    public class Account : BaseDto
    {
        public int AccountClassId { get; set; }
        public int? ParentAccountId { get; set; }
        public int CompanyId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string Description { get; set; }
        public bool IsCash { get; set; }
        public bool IsContraAccount { get; set; }
        public decimal Balance { get; set; }
        public decimal DebitBalance { get; set; }
        public decimal CreditBalance { get; set; }
        public decimal TotalBalance { get { return GetTotalBalance(); } }
        public decimal TotalDebitBalance { get { return GetTotalDebit(); } }
        public decimal TotalCreditBalance { get { return GetTotalCredit(); } }
        public IList<Account> ChildAccounts { get; set; }

        public Account()
        {
            ChildAccounts = new List<Account>();
        }

        
        private decimal GetTotalBalance()
        {
            decimal sum = 0;
            ComputeBalance(ChildAccounts, ref sum);
            return sum;
        }

        private void ComputeBalance(IList<Account> accounts, ref decimal sum)
        {
            foreach (var account in accounts)
            {
                sum += account.Balance;
                if (account.ChildAccounts.Count > 0)
                {
                    ComputeBalance(account.ChildAccounts, ref sum);
                }
            }
        }

        private decimal GetTotalDebit()
        {
            decimal sum = 0;
            ComputeDebit(ChildAccounts, ref sum);
            return sum;
        }

        private void ComputeDebit(IList<Account> accounts, ref decimal sum)
        {
            foreach (var account in accounts)
            {
                sum += account.DebitBalance;

                if (account.ChildAccounts.Count > 0)
                {
                    ComputeDebit(account.ChildAccounts, ref sum);
                }
            }
        }

        private decimal GetTotalCredit()
        {
            decimal sum = 0;
            ComputeCredit(ChildAccounts, ref sum);
            return sum;
        }

        private void ComputeCredit(IList<Account> accounts, ref decimal sum)
        {
            foreach (var account in accounts)
            {
                sum += account.CreditBalance;

                if (account.ChildAccounts.Count > 0)
                {
                    ComputeCredit(account.ChildAccounts, ref sum);
                }
            }
        }
    }    
}
