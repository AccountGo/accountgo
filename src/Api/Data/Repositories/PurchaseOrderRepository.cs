using Core.Data;
using Core.Domain.Purchases;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Api.Data
{
    public class PurchaseOrderRepository : EfRepository<PurchaseOrderHeader>, IPurchaseOrderRepository
    {
        public PurchaseOrderRepository(ApiDbContext context) : base(context)
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
