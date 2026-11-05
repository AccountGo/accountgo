#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Service;

public sealed class LinkedEntryIntegrityScanner : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LinkedEntryIntegrityScanner> _logger;
    public LinkedEntryIntegrityScanner(IServiceScopeFactory scopes, ILogger<LinkedEntryIntegrityScanner> logger)
    { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ScanOnceAsync(stoppingToken);
    }

    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var now = DateTime.UtcNow;
        var pairs = await db.LinkedEntryPairs.AsNoTracking().ToListAsync(cancellationToken);
        var warnings = 0;
        foreach (var pair in pairs)
        {
            var sourceExists = pair.SourceJournalId.HasValue && await db.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(
                j => j.CompanyId == pair.SourceCompanyId && j.Id == pair.SourceJournalId.Value, cancellationToken);
            var mirrorExists = pair.MirrorJournalId.HasValue && await db.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(
                j => j.CompanyId == pair.MirrorCompanyId && j.Id == pair.MirrorJournalId.Value, cancellationToken);
            if (!sourceExists || !mirrorExists)
            {
                warnings++;
                _logger.LogWarning("Linked entry pair {PairId} has a missing journal side (source workspace {SourceCompanyId}, mirror workspace {MirrorCompanyId}).",
                    pair.Id, pair.SourceCompanyId, pair.MirrorCompanyId);
            }
            else if (pair.State == "PendingSource" && pair.CreatedAtUtc < now.AddDays(-30))
            {
                warnings++;
                _logger.LogWarning("Linked entry pair {PairId} has a source journal pending review for more than 30 days.", pair.Id);
            }
        }
        return warnings;
    }
}
