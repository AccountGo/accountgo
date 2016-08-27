using Core.Data;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Api.Data
{
    public class SalesOrderRepository : EfRepository<SalesOrderHeader>, ISalesOrderRepository
    {
        public SalesOrderRepository(ApiDbContext context) : base(context)
        {
        }

        public IQueryable<SalesOrderHeader> GetAllSalesOrders()
        {
            IQueryable<SalesOrderHeader> queryable = base.Entities;

            var salesOrders = queryable
                .Include(so => so.SalesOrderLines)
                .ThenInclude(line => line.SalesInvoiceLines)
                .Include(so => so.Customer)
                .Include(so => so.Customer.Party);

            return salesOrders;
        }
    }
}
