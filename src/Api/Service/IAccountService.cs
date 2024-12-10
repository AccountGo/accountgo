using Core.Domain.Financials;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Api.Service;

public interface IAccountService
{
    // Add a new account
    Task<Account> AddAccountAsync(Account newAccount);

    // Update an existing account
    Task<Account?> UpdateAccountAsync(string accountCode, Account updatedAccount);

    // Delete an account by AccountCode
    Task<Account?> DeleteAccountAsync(string accountCode);

    // Get an account by AccountCode
    Task<Account?> GetAccountByCodeAsync(string accountCode);

    // Get all accounts
    Task<IEnumerable<Account>> GetAllAccountsAsync();
}