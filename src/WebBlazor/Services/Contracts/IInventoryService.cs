using Dto.Inventory.Request;
using Dto.Inventory.Response;
using static Dto.Response.ServiceResponses;

namespace WebBlazor.Services.Contracts
{
    public interface IInventoryService
    {
        Task<IEnumerable<GetItem>> GetItems();
        Task<CreatedResponse> CreateItem(CreateItem request);
    }
}
