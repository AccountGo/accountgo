using System.Collections.Generic;

namespace Dto.Financial
{
    public class Account : BaseDto
    {
        public int AccountClassId { get; set; }
        public int? ParentAccountId { get; set; }
        public int CompanyId { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public string? Description { get; set; }
        public bool IsCash { get; set; }
        public bool IsContraAccount { get; set; }
        public bool Archived { get; set; }
        public System.DateTime? ArchivedAt { get; set; }
        public bool IsProtected { get; set; }
        public string? ProtectionReason { get; set; }
        public bool CanDelete { get; set; }
        public string? DeleteBlockedReason { get; set; }
        /// <summary>The account's own normal side: "Dr", "Cr" or "NA".</summary>
        public string? NormalBalance { get; set; }
        public string? AccountClassName { get; set; }
        /// <summary>False for header accounts (they have sub-accounts and only group totals).</summary>
        public bool CanPost { get; set; }
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

    public class CoaImportRowPreviewDto
    {
        public int RowNumber { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string? ParentAccountCode { get; set; }
        public string AccountClassName { get; set; } = string.Empty;
        public int AccountClassId { get; set; }
        public bool IsContraAccount { get; set; }
        public bool IsCash { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = "Active";
        public string Action { get; set; } = "create"; // create | update | skip | error
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class CoaImportPreviewDto
    {
        public string Mode { get; set; } = "append";
        public int TotalRows { get; set; }
        public int CreateCount { get; set; }
        public int UpdateCount { get; set; }
        public int SkipCount { get; set; }
        public int RetireCount { get; set; }
        public int DeleteCount { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public bool IsEmptyBooks { get; set; } = true;
        public bool CanReplace { get; set; } = true;
        public List<ControlRoleMappingDto> ControlMappings { get; set; } = new();
        public bool HasErrors => ErrorCount > 0 || FileErrors.Count > 0;
        public bool CanCommit => ErrorCount == 0 && FileErrors.Count == 0;
        public List<string> FileErrors { get; set; } = new();
        public List<string> FileWarnings { get; set; } = new();
        public List<CoaImportRowPreviewDto> Rows { get; set; } = new();
    }

    public class CoaImportCommitResultDto
    {
        public string Mode { get; set; } = "append";
        public int TotalRows { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int RetiredCount { get; set; }
        public int DeletedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ChartOfAccountsStatusDto
    {
        public bool IsEmptyBooks { get; set; }
        public int PostedTransactionCount { get; set; }
        public string? ReplaceBlockedReason { get; set; }
        public bool CanReplace { get; set; }
        public List<ControlRoleMappingDto> RequiredControlRoles { get; set; } = new();
    }

    public class ControlRoleMappingDto
    {
        public string RoleKey { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty; // GeneralLedgerSetting | Tax | Bank
        public int? SourceEntityId { get; set; }
        public int? CurrentAccountId { get; set; }
        public string? CurrentAccountCode { get; set; }
        public string? CurrentAccountName { get; set; }
        public string? TargetAccountCode { get; set; }
        public bool IsRequired { get; set; } = true;
    }

    public class CoaImportRequestDto
    {
        public string? CsvContent { get; set; }
        public string? Mode { get; set; } = "append";
        public bool RetireMissing { get; set; } = false;
        public Dictionary<string, string>? ControlMappings { get; set; }
    }
}
