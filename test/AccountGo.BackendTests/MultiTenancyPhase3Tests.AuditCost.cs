#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Api.Auditing;
using Api.Data;
using Api.Data.Postgres;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AccountGo.BackendTests;

public partial class MultiTenancyPhase3Tests
{
    [Fact]
    public async Task PostgresAuditCost_ReportsPaired50LineJournalSaveSamples()
    {
        if (_postgres == null) return;
        var cash = AccountId("10111");
        var revenue = AccountId("40100");
        using var withoutAudit = MeasurementHost(false);
        using var withAudit = MeasurementHost(true);
        async Task<double> Sample(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<PostgresApiDbContext>();
            var entry = new JournalEntryHeader
            {
                CompanyId = _companyId, Date = new DateTime(2026, 2, 1), Memo = "Audit cost sample",
                JournalEntryLines = Enumerable.Range(0, 50).Select(i => new JournalEntryLine
                {
                    CompanyId = _companyId, AccountId = i % 2 == 0 ? cash : revenue,
                    DrCr = i % 2 == 0 ? DrOrCrSide.Dr : DrOrCrSide.Cr, Amount = 1m, Memo = "Audit cost sample"
                }).ToList()
            };
            db.JournalEntryHeaders.Add(entry);
            var watch = Stopwatch.StartNew();
            await db.SaveChangesAsync();
            return watch.Elapsed.TotalMilliseconds;
        }
        await Sample(withoutAudit);
        await Sample(withAudit);
        var deltas = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            double baseline, audited;
            if (i % 2 == 0) { baseline = await Sample(withoutAudit); audited = await Sample(withAudit); }
            else { audited = await Sample(withAudit); baseline = await Sample(withoutAudit); }
            deltas.Add(audited - baseline);
        }
        deltas.Sort();
        _output.WriteLine($"AUDIT COST median_delta_ms={((deltas[14] + deltas[15]) / 2):F3} p95_delta_ms={deltas[28]:F3} samples=30");
    }

    private IHost MeasurementHost(bool audit) => Host.CreateDefaultBuilder().ConfigureServices(services =>
    {
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IRequestAuditContext, AuditCostContext>();
        services.AddDbContext<PostgresApiDbContext>((sp, options) =>
        {
            options.UseNpgsql(_postgres.ConnectionString, p => p.ExecutionStrategy(d => new BufferingExecutionStrategy(d)));
            if (audit) options.AddInterceptors(new RecordChangeInterceptor(sp.GetRequiredService<IRequestAuditContext>()));
        });
    }).Build();

    private sealed class AuditCostContext : IRequestAuditContext
    {
        public string ActorUserId => "cost-measurement";
        public string ActorKind => "user";
        public string CorrelationId => "cost-measurement";
        public string IpAddress => null;
    }
}
