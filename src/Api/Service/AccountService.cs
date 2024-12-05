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
        // newAccount.Balance = 0; // Ensure read-only fields are initialized
        // newAccount.DebitBalance = 0;
        // newAccount.CreditBalance = 0;

        _context.Accounts.Add(newAccount);
        await _context.SaveChangesAsync();
        return newAccount;
    }

    // Update an existing account
    public async Task<Core.Domain.Financials.Account?> UpdateAccountAsync(string accountCode, Core.Domain.Financials.Account updatedAccount)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        account.AccountName = updatedAccount.AccountName;

        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        return account;
    }


    // Delete an account
    public async Task<Core.Domain.Financials.Account?> DeleteAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();

        return account;
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