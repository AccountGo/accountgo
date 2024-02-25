using Dto.Common.Response;
using Dto.Financial;
using Dto.Inventory.Response;
using Dto.Purchasing.Response;
using Dto.TaxSystem;

namespace WebBlazor.Services.Contracts
{
    public interface ILookupService 
    {
        Task<IEnumerable<BaseAccount>> GetAccounts();
        Task<IEnumerable<GetItem>> GetItems();
        Task<IEnumerable<GetMeasurement>> GetMeasurements();
        Task<IEnumerable<ItemTaxGroup>> GetItemTaxGroups();
        Task<IEnumerable<GetItemCategory>> GetItemCategories();
        Task<IEnumerable<GetVendor>> GetVendors();
        Task<IEnumerable<GetPaymentTerm>> GetPaymentTerms();
    }
}
