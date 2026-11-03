// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    /// <summary>
    /// Startup readiness sweep that detects committed invariant violations left behind by a process crash
    /// between a durable commit and the inline post-commit verification. It scans every workspace's
    /// committed state in bounded, tenant-scoped batches and reports each currently-violating workspace
    /// exactly once per signature via the shared idempotent reporting path, so:
    ///  - a violation left by a crash is surfaced on the next start, and
    ///  - repeated sweeps of the same persisted violation are NOT re-counted as new committed violations.
    ///
    /// A detector failure for one workspace never aborts the sweep and is counted as a scanner exception,
    /// kept distinct from current violations and detected events. Disabled by default; enabled with
    /// <c>WORKSPACE_INVARIANT_SCAN_ON_START=true</c>. Read-mostly: only idempotent "reported" markers are
    /// written, never financial or policy state.
    /// </summary>
    public sealed class WorkspaceInvariantRecoveryScanner : BackgroundService
    {
        public const string EnabledKey = "WORKSPACE_INVARIANT_SCAN_ON_START";
        private const int BatchSize = 200;

        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WorkspaceInvariantRecoveryScanner> _logger;

        public WorkspaceInvariantRecoveryScanner(
            IServiceProvider services,
            IConfiguration configuration,
            ILogger<WorkspaceInvariantRecoveryScanner> logger)
        {
            _services = services;
            _configuration = configuration;
            _logger = logger;
        }

        public static bool IsEnabled(IConfiguration configuration) =>
            string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Always expose durable current-state to monitoring, independent of whether the one-shot sweep
            // is enabled: the active-incident gauge reflects workspaces currently in a violating state,
            // including incidents that persisted across a restart while new-incident counters read zero.
            RegisterActiveIncidentGauge();

            if (!IsEnabled(_configuration))
                return;

            try
            {
                var (scanned, violations, scannerErrors) = await ScanAsync(_services, BatchSize, stoppingToken);
                _logger.LogWarning(
                    "Workspace invariant recovery sweep complete: scanned={Scanned} newViolations={Violations} scannerErrors={Errors}",
                    scanned, violations, scannerErrors);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // The sweep is a safety net; a sweep-level failure must never stop the app from serving.
                _logger.LogWarning(ex, "Workspace invariant recovery sweep failed");
            }
        }

        private void RegisterActiveIncidentGauge()
        {
            Core.Authorization.WorkspaceOperatingPolicyMetrics.RegisterActiveIncidentObserver(() =>
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                    db.SuppressTenantSecurityCheck = true;
                    return WorkspaceInvariantIncidentLog.CountWorkspacesWithActiveIncident(db);
                }
                catch (Exception ex)
                {
                    // Signal UNAVAILABLE (-1), never a healthy-looking zero. The metrics layer counts the
                    // failure and ages the last-success timestamp so monitoring can alert on it.
                    _logger.LogWarning(ex, "Active invariant-incident count query failed; reporting measurement as unavailable.");
                    return -1;
                }
            });
        }

        /// <summary>
        /// Scans all workspaces in bounded batches ordered by company id. Returns (scanned, newViolations,
        /// scannerErrors). Each batch uses its own scope/DbContext; a per-workspace detector failure is
        /// isolated and counted, never aborting the sweep. Tenant security is suppressed because this is a
        /// cross-tenant operator sweep reading committed state only.
        /// </summary>
        public static async Task<(int Scanned, int NewViolations, int ScannerErrors)> ScanAsync(
            IServiceProvider services,
            int batchSize,
            CancellationToken ct)
        {
            int scanned = 0, newViolations = 0, scannerErrors = 0;
            int lastCompanyId = 0;

            while (!ct.IsCancellationRequested)
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.SuppressTenantSecurityCheck = true;

                var batch = await db.WorkspaceOperatingSettings
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => s.CompanyId > lastCompanyId)
                    .OrderBy(s => s.CompanyId)
                    .Take(batchSize)
                    .ToListAsync(ct);

                if (batch.Count == 0)
                    break;

                foreach (var settings in batch)
                {
                    lastCompanyId = settings.CompanyId;
                    scanned++;
                    try
                    {
                        // Verify under the company coordination lock with a consistent snapshot so an
                        // in-flight transition (e.g. invitation acceptance committing two members while the
                        // settings row still reads SoloActive) is not mistaken for a violation. Counter
                        // emission and incident dedup happen inside, after the per-company commit.
                        var outcome = WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(
                            db, tenant: null, settings.CompanyId);
                        if (outcome == WorkspaceInvariantIncidentLog.ReconcileOutcome.NewIncident)
                            newViolations++;
                    }
                    catch (Exception)
                    {
                        // Isolate and count a per-workspace detector failure; keep sweeping.
                        Core.Authorization.WorkspaceOperatingPolicyMetrics.PostCommitDetectorExceptions.Add(1);
                        scannerErrors++;
                    }
                }

                if (batch.Count < batchSize)
                    break;
            }

            return (scanned, newViolations, scannerErrors);
        }
    }
}
