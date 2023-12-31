using Core.Data;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using System.Linq;

namespace Api.Data
{
    public class SalesOrderRepository : EfRepository<SalesOrderHeader>, ISalesOrderRepository
    {
        public SalesOrderRepository(ILogger<SalesOrderHeader> logger, ApiDbContext context) : base(logger,context)
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
