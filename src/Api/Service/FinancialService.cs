using System.Collections.Concurrent;
using Api.Data;
using Dto.Financial;
using Microsoft.EntityFrameworkCore;
namespace Api.Service
//<summary>
// 
{
    public class FinancialService : IFinancialService
    {
        private readonly ApiDbContext _context;

        public FinancialService(ApiDbContext context)
        {
            _context = context;
        }

        public async Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount)
        {
            await _context.Accounts.AddAsync(newAccount);
            await _context.SaveChangesAsync();
            return newAccount;
        }

        public async Task<Core.Domain.Financials.Account?> UpdateAccountAsync(int id, Core.Domain.Financials.Account updatedAccount)
        {
            // Account Balance is read-only and cannot be assigned

            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return null;
            account.AccountCode = updatedAccount.AccountCode;
            account.AccountName = updatedAccount.AccountName;
            account.Description = updatedAccount.Description;
            account.IsCash = updatedAccount.IsCash;
            account.IsContraAccount = updatedAccount.IsContraAccount;
            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();

            return account;
        }

        public async Task<Core.Domain.Financials.Account?> DeleteAccountAsync(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return null;
            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();
            return account;
        }
    }
}