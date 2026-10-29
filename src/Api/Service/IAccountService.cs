using Core.Domain.Financials;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IAccountService
{
    // Add a new account
    Task<Account> AddAccountAsync(Account newAccount);

    // Update an existing account
    Task<Account?> UpdateAccountAsync(string accountCode, Account updatedAccount);

    // Delete an account by AccountCode
    // Returns (account, errorMessage) - account is null if deletion failed
    Task<(Account? account, string? errorMessage)> DeleteAccountAsync(string accountCode);

    // Archive an account by AccountCode
    Task<(Account? account, string? errorMessage)> ArchiveAccountAsync(string accountCode);

    // Unarchive an account by AccountCode
    Task<(Account? account, string? errorMessage)> UnarchiveAccountAsync(string accountCode);

    // Get protection and delete eligibility info
    Task<(bool isProtected, string? protectionReason, bool canDelete, string? deleteBlockedReason)> GetAccountProtectionInfoAsync(int accountId);

    // Get an account by AccountCode
    Task<Account?> GetAccountByCodeAsync(string accountCode);

    // Get all accounts
    Task<IEnumerable<Account>> GetAllAccountsAsync();

    // Get Chart of Accounts status (empty books detection, replace eligibility, control roles)
    Task<Dto.Financial.ChartOfAccountsStatusDto> GetChartStatusAsync();

    // Get required control roles (GeneralLedgerSetting, Taxes, Banks)
    Task<List<Dto.Financial.ControlRoleMappingDto>> GetRequiredControlRolesAsync();

    // Validate CSV for CoA Import
    Task<Dto.Financial.CoaImportPreviewDto> ValidateImportAsync(
        string csvContent,
        string mode,
        bool retireMissing = false,
        Dictionary<string, string>? controlMappings = null,
        bool isCallerOwner = false);

    // Commit CoA Import
    Task<(Dto.Financial.CoaImportCommitResultDto? result, Dto.Financial.CoaImportPreviewDto? failedPreview)> CommitImportAsync(
        string csvContent,
        string mode,
        bool skipErrors = false,
        bool retireMissing = false,
        Dictionary<string, string>? controlMappings = null,
        bool isCallerOwner = false);
}