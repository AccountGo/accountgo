using Core.Domain.Purchases;
using System.Linq;

namespace Core.Data
{
    public interface IPurchaseOrderRepository : IRepository<PurchaseOrderHeader>
    {
        IQueryable<PurchaseOrderHeader> GetAllPurchaseOrders();
    }
}
