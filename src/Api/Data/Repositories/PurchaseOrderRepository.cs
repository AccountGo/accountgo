using Core.Data;
using Core.Domain.Purchases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using System.Linq;

namespace Api.Data
{
    public class PurchaseOrderRepository : EfRepository<PurchaseOrderHeader>, IPurchaseOrderRepository
    {
        public PurchaseOrderRepository(ILogger<PurchaseOrderHeader> logger, ApiDbContext context) : base(logger, context)
        {
        }        

        public IQueryable<PurchaseOrderHeader> GetAllPurchaseOrders()
        {
            IQueryable<PurchaseOrderHeader> queryable = base.Entities;

            var purchaseOrders = queryable
                .Include(po => po.PurchaseOrderLines)
                .ThenInclude(line => line.PurchaseInvoiceLines)
                .Include(po => po.Vendor)
                .Include(po => po.Vendor.Party);

            return purchaseOrders;
        }
    }
}
