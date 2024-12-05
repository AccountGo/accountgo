using Api.Data;
using Api.Service;
using Microsoft.EntityFrameworkCore;

public class AccountService : IAccountService
{
    private readonly ApiDbContext _context;

    public AccountService(ApiDbContext context)
    {
        _context = context;
    }

    public async Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount)
    {
        _context.Accounts.Add(newAccount);
        await _context.SaveChangesAsync();
        return newAccount;
    }

    public async Task<Core.Domain.Financials.Account?> UpdateAccountAsync(string accountCode, Core.Domain.Financials.Account updatedAccount)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        account.AccountName = updatedAccount.AccountName;
        account.Description = updatedAccount.Description;
        account.IsCash = updatedAccount.IsCash;
        account.IsContraAccount = updatedAccount.IsContraAccount;

        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        return account;
    }

    public async Task<Core.Domain.Financials.Account?> DeleteAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();

        return account;
    }

    public async Task<Core.Domain.Financials.Account?> GetAccountByCodeAsync(string accountCode)
    {
        return await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
    }
}