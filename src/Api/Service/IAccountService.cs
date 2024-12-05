public interface IAccountService
{
    Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount);
    Task<Core.Domain.Financials.Account?> UpdateAccountAsync(string accountCode, Core.Domain.Financials.Account updatedAccount);
    Task<Core.Domain.Financials.Account?> DeleteAccountAsync(string accountCode);
    Task<Core.Domain.Financials.Account?> GetAccountByCodeAsync(string accountCode);
}