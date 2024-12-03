using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using Services.Security;

namespace Api.Data.Seed
{
    public class DatabaseSeeder
    {
        private readonly IServiceProvider _serviceProvider;

        public DatabaseSeeder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Seed()
        {
            using var scope = _serviceProvider.CreateScope();
            var adminService = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
            var financialService = scope.ServiceProvider.GetRequiredService<IFinancialService>();
            var salesService = scope.ServiceProvider.GetRequiredService<ISalesService>();
            var purchasingService = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var securityService = scope.ServiceProvider.GetRequiredService<ISecurityService>();

            var initializer = new Initializer(
                adminService, 
                financialService, 
                salesService, 
                purchasingService, 
                inventoryService, 
                securityService
            );

            var success = initializer.Setup();

            if (success)
            {
                Console.WriteLine("Database seeding completed successfully.");
            }
            else
            {
                Console.WriteLine("Database seeding failed. Check logs for details.");
            }
        }
    }
}