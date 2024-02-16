using Dto.TaxSystem;

namespace WebBlazor.Services.Contracts
{
    public interface ITaxService
    {
        Task<IEnumerable<ItemTaxGroup>> GetItemTaxGroups();
    }
}
