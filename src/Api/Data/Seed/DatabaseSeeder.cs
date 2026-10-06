using System;
using System.Linq;
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

            SeedDonations(scope.ServiceProvider.GetRequiredService<ApiDbContext>());
        }

        private static void SeedDonations(ApiDbContext context)
        {
            var samples = new[]
            {
                new Dto.Donations.DonationInvoice
                {
                    No = "DON-SEED-001", DonorId = 1, GlAccountCode = "41001",
                    DonationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                    PaymentType = "Cash", Amount = 150.00m, Purpose = "General-purpose support",
                    IsTaxReceiptIssued = true, TaxReceiptNo = "TR-SEED-001"
                },
                new Dto.Donations.DonationInvoice
                {
                    No = "DON-SEED-002", DonorId = 1, GlAccountCode = "41200",
                    DonationDate = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                    PaymentType = "Credit Card", Amount = 75.50m, Purpose = "Sunday School materials",
                    IsTaxReceiptIssued = false, TaxReceiptNo = string.Empty
                },
                new Dto.Donations.DonationInvoice
                {
                    No = "DON-SEED-003", DonorId = 2, GlAccountCode = "41250",
                    DonationDate = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc),
                    PaymentType = "E-Transfer", Amount = 5000.00m, Purpose = "Brothers In Christ outreach",
                    IsTaxReceiptIssued = true, TaxReceiptNo = "TR-SEED-003"
                },
                new Dto.Donations.DonationInvoice
                {
                    No = "DON-SEED-004", DonorId = 2, GlAccountCode = "41001",
                    DonationDate = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
                    PaymentType = "Cheque", Amount = 1200.00m, Purpose = "Community care fund",
                    IsTaxReceiptIssued = false, TaxReceiptNo = string.Empty
                }
            };

            foreach (var sample in samples)
            {
                if (context.DonationInvoiceHeaders.Any(donation => donation.No == sample.No))
                    continue;

                var donor = context.Donors.FirstOrDefault(entity => entity.Id == sample.DonorId);
                var account = context.Accounts.FirstOrDefault(entity => entity.CompanyId == 1 && entity.AccountCode == sample.GlAccountCode);
                if (donor == null || account == null)
                {
                    Console.WriteLine($"Skipped sample donation {sample.No}: its donor or GL account is missing.");
                    continue;
                }

                context.DonationInvoiceHeaders.Add(new Core.Domain.Donations.DonationInvoiceHeader
                {
                    No = sample.No,
                    DonorId = donor.Id,
                    GlAccountId = account.Id,
                    Date = sample.DonationDate,
                    PaymentType = sample.PaymentType,
                    Amount = sample.Amount,
                    Purpose = sample.Purpose,
                    ReferenceNo = string.Empty,
                    IsTaxReceiptIssued = sample.IsTaxReceiptIssued,
                    TaxReceiptNo = sample.TaxReceiptNo,
                    Posted = false,
                    ModifiedBy = "seed"
                });
            }

            context.SaveChanges();
        }
    }
}