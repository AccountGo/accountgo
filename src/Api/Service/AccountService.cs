using Api.Data;
using Dto.Financial;
using Microsoft.EntityFrameworkCore;

public class AccountService : IAccountService
{
    private readonly ApiDbContext _context;

    public AccountService(ApiDbContext context)
    {
        _context = context;
    }

    // Add a new account
    public async Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount)
    {
        _context.Accounts.Add(newAccount);
        await _context.SaveChangesAsync();
        return newAccount;
    }

    // Update an existing account
    public async Task<Core.Domain.Financials.Account> UpdateAccountAsync(string originalAccountCode, Core.Domain.Financials.Account account)
    {
        // Find account by the original code
        var existingAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountCode == originalAccountCode);

        if (existingAccount == null)
            return null;

        // Update properties
        existingAccount.AccountCode = account.AccountCode;
        existingAccount.AccountName = account.AccountName;
        existingAccount.Description = account.Description;
        existingAccount.IsCash = account.IsCash;
        existingAccount.IsContraAccount = account.IsContraAccount;

        // Optional properties that might not be provided
        if (account.AccountClassId > 0)
            existingAccount.AccountClassId = account.AccountClassId;
        
        if (account.CompanyId > 0)
            existingAccount.CompanyId = account.CompanyId;
            
        if (account.ParentAccountId.HasValue)
            existingAccount.ParentAccountId = account.ParentAccountId;

        await _context.SaveChangesAsync();
        return existingAccount;
    }

    // Delete an account with cascade option for child accounts
    public async Task<Core.Domain.Financials.Account?> DeleteAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        // Get all accounts to find children (for recursive deletion)
        var allAccounts = await _context.Accounts.ToListAsync();
        
        // Check if account is used in journal entries
        bool usedInJournals = await _context.JournalEntryLines.AnyAsync(j => j.AccountId == account.Id);
        if (usedInJournals)
            throw new InvalidOperationException("Cannot delete an account that is used in journal entries.");

        // Delete the account and all its children recursively
        await DeleteAccountWithChildrenAsync(account.Id, allAccounts);
        
        await _context.SaveChangesAsync();

        return account;
    }

    // Recursive method to delete an account and all its children
    private async Task DeleteAccountWithChildrenAsync(int accountId, List<Core.Domain.Financials.Account> allAccounts)
    {
        // Find all direct children of this account
        var childAccounts = allAccounts.Where(a => a.ParentAccountId == accountId).ToList();
        
        // Recursively delete each child and its children
        foreach (var childAccount in childAccounts)
        {
            // Check if child account is used in journal entries
            bool childUsedInJournals = await _context.JournalEntryLines.AnyAsync(j => j.AccountId == childAccount.Id);
            if (childUsedInJournals)
                throw new InvalidOperationException($"Cannot delete account '{childAccount.AccountCode}' because it is used in journal entries.");
                
            // Delete this child's children first
            await DeleteAccountWithChildrenAsync(childAccount.Id, allAccounts);
            
            // Then delete the child itself
            _context.Accounts.Remove(childAccount);
        }
        
        // Finally, delete the parent account
        var account = await _context.Accounts.FindAsync(accountId);
        if (account != null)
        {
            _context.Accounts.Remove(account);
        }
    }

    // Get an account by AccountCode
    public async Task<Core.Domain.Financials.Account?> GetAccountByCodeAsync(string accountCode)
    {
        return await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
    }

    // List all accounts
    public async Task<IEnumerable<Core.Domain.Financials.Account>> GetAllAccountsAsync()
    {
        return await _context.Accounts.ToListAsync();
    }
}