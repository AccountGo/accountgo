using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Data;
using Core.Domain;
using Core.Domain.Sales;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Services.BackgroundJobs
{
    public class ExpiryCheckJobService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRecurringJobManager _recurringJobManager;
  
        public ExpiryCheckJobService(
            IServiceProvider serviceProvider,
            IRecurringJobManager recurringJobManager)
        {
            _serviceProvider = serviceProvider;
            _recurringJobManager = recurringJobManager;
        }

        public void AddExpiryCheckJob()
        {
            _recurringJobManager.AddOrUpdate("ExpiryCheckJob", () => UpdateSalesProposalStatusIfExpired(), Cron.Minutely);
        }

        public void UpdateSalesProposalStatusIfExpired()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var salesProposalRepo = scope.ServiceProvider.GetRequiredService<IRepository<SalesProposalHeader>>();

                var salesProposals = salesProposalRepo.FindAllIncluding(true, proposal => proposal.Customer,
                    proposal => proposal.Customer.Party,
                    proposal => proposal.SalesProposalLines)
                    .ToList();

                foreach (var salesProposal in salesProposals)
                {
                    bool canOverdue = salesProposal.Status == SalesProposalStatus.Draft || salesProposal.Status == SalesProposalStatus.Open;

                    if (canOverdue && salesProposal.ExpiryDate < DateTime.Now)
                    {
                        salesProposal.Status = SalesProposalStatus.Overdue;

                        salesProposalRepo.Update(salesProposal);
                    }
                }

                Console.WriteLine("Executing scheduled job for Expiry Check...");
            }
        }

    }
}
