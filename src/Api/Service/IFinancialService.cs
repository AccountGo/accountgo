
using Dto.Financial;
namespace Api.Service
{
    /// <summary>
    /// Provides methods for managing financial accounts.
    /// </summary>
    public interface IFinancialService
    {
        Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount);
        Task<Core.Domain.Financials.Account?> UpdateAccountAsync(int id, Core.Domain.Financials.Account updatedAccount);
        Task<Core.Domain.Financials.Account?> DeleteAccountAsync(int id);
    }
}