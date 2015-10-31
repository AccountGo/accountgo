using Core.Domain.Sales;
using Services.Financial;
using Services.Inventory;
using Services.Sales;
using System.Collections.Generic;
using System.Linq;

namespace Web.Models.ViewModels.Sales
{
    public partial class SalesViewModelBuilder
    {
        private readonly IInventoryService _inventoryService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;

        public SalesViewModelBuilder(IInventoryService inventoryService,
            IFinancialService financialService,
            ISalesService salesService)
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
            _salesService = salesService;

            
        }

        public SalesDeliveryViewModel CreateSalesDeliveryViewModel()
        {
            return new SalesDeliveryViewModel();
        }

        public SalesDeliveries CreateSalesDeliveriesViewModel(ICollection<SalesDeliveryHeader> salesDeliveries)
        {
            var model = new SalesDeliveries();

            foreach (var sd in salesDeliveries)
            {
                model.SalesDeliveriesViewModel.Add(new SalesDeliveryViewModel()
                {
                    CustomerId = sd.CustomerId.Value,
                    PaymentTermId = sd.PaymentTermId.HasValue ? sd.PaymentTermId.Value : -1,
                    Date = sd.Date,
                    Amount = sd.SalesDeliveryLines.Sum(a => a.Price)
                });
            }

            return model;
        }

        public SalesOrders CreateSalesOrdersViewModel(ICollection<SalesOrderHeader> salesOrders)
        {
            var model = new SalesOrders();
            foreach (var order in salesOrders)
            {
                model.SalesOrdersViewModel.Add(new SalesOrderViewModel()
                {
                    Id = order.Id,
                    Customer = order.Customer.Name,
                    PaymentTermId = order.PaymentTermId,
                    Date = order.Date,
                    Amount = order.SalesOrderLines.Sum(a => a.Amount)
                });
            }
            return model;
        }
    }
}