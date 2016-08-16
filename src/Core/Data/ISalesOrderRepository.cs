using Core.Domain.Sales;
using System.Linq;

namespace Core.Data
{
    public interface ISalesOrderRepository : IRepository<SalesOrderHeader>
    {
        IQueryable<SalesOrderHeader> GetAllSalesOrders();
    }
}
