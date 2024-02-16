using Dto.Financial;
using Dto.Inventory;
using Dto.Purchasing.Response;

namespace WebBlazor.Services.Contracts
{
    public interface ILookupService 
    {
        Task<IEnumerable<BaseAccount>> GetAccounts();
        Task<IEnumerable<Measurement>> GetMeasurements();
        Task<IEnumerable<ItemCategory>> GetItemCategories();
        Task<IEnumerable<GetVendorResponse>> GetVendors();
    }
}
