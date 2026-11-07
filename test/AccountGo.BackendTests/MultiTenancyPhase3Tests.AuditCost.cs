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

    [Fact]
    public async Task PostgresRecordChanges_KeepCaptureOrderParentsAndValues()
    {
        if (_postgres == null) return;
        using var host = MeasurementHost(true);
        using var scope = host.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var impersonation = tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<PostgresApiDbContext>();
        var before = db.RecordChanges.IgnoreQueryFilters().Select(x => (long?)x.Id).Max() ?? 0;
        var entry = new JournalEntryHeader
        {
            CompanyId = _companyId, Date = new DateTime(2026, 2, 1), Memo = "Order check",
            JournalEntryLines = new[] { 11m, 22m, 33m }.Select((amount, i) => new JournalEntryLine
            {
                CompanyId = _companyId, AccountId = AccountId(i == 2 ? "40100" : "10111"),
                DrCr = i == 2 ? DrOrCrSide.Cr : DrOrCrSide.Dr, Amount = amount, Memo = "Order check " + i
            }).ToList()
        };
        db.JournalEntryHeaders.Add(entry);
        await db.SaveChangesAsync();

        var rows = db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id > before && x.CorrelationId == "cost-measurement").OrderBy(x => x.Id).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal("JournalEntryHeader", rows[0].EntityType);
        Assert.Equal(entry.Id, rows[0].EntityId);
        Assert.Null(rows[0].ParentEntityType);
        Assert.Null(rows[0].IpAddress);
        var lines = entry.JournalEntryLines.ToList();
        for (var i = 0; i < 3; i++)
        {
            var row = rows[i + 1];
            Assert.Equal("JournalEntryLine", row.EntityType);
            Assert.Equal(lines[i].Id, row.EntityId);
            Assert.Equal("JournalEntryHeader", row.ParentEntityType);
            Assert.Equal(entry.Id, row.ParentEntityId);
            Assert.Equal("Added", row.Action);
            Assert.Equal(_companyId, row.CompanyId);
            using var values = System.Text.Json.JsonDocument.Parse(row.ChangesJson);
            Assert.Equal(lines[i].Amount, values.RootElement.GetProperty("Amount").GetDecimal());
            Assert.Equal(lines[i].Id, values.RootElement.GetProperty("Id").GetInt32());
            Assert.Equal(entry.Id, values.RootElement.GetProperty("JournalEntryHeaderId").GetInt32());
        }
        Assert.All(rows, row => Assert.True(Math.Abs((DateTime.UtcNow - row.OccurredAt).TotalMinutes) < 5));
    }

    [Fact]
    public async Task RecordChanges_NewJournalLinesPointAtTheSavedHeader()
    {
        var journal = new Dto.Financial.JournalEntry { JournalDate = new DateTime(2026, 2, 1), VoucherType = 6, Memo = "Parent link check" };
        journal.JournalEntryLines.Add(new Dto.Financial.JournalEntryLine { AccountId = AccountId("10111"), DrCr = 1, Amount = 7m });
        journal.JournalEntryLines.Add(new Dto.Financial.JournalEntryLine { AccountId = AccountId("40100"), DrCr = 2, Amount = 7m });
        using var response = await Send(HttpMethod.Post, "/api/Financials/SaveJournalEntry", journal);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<Dto.Financial.JournalEntry>(response.Content);
        WithDb(db =>
        {
            var lineIds = db.JournalEntryLines.IgnoreQueryFilters().Where(l => l.JournalEntryHeaderId == saved.Id).Select(l => l.Id).ToList();
            var rows = db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.EntityType == "JournalEntryLine" && r.Action == "Added" && lineIds.Contains(r.EntityId)).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal(saved.Id, row.ParentEntityId);
                using var values = System.Text.Json.JsonDocument.Parse(row.ChangesJson);
                Assert.Equal(saved.Id, values.RootElement.GetProperty("JournalEntryHeaderId").GetInt32());
                Assert.Equal(row.EntityId, values.RootElement.GetProperty("Id").GetInt32());
            });
        });
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
