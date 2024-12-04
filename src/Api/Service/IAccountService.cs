

public interface IAccountService
{
    Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount);
    Task<Core.Domain.Financials.Account?> UpdateAccountAsync(int id, Core.Domain.Financials.Account updatedAccount);
    Task<Core.Domain.Financials.Account?> DeleteAccountAsync(int id);
    Task<Core.Domain.Financials.Account?> GetAccountByIdAsync(int id);
}