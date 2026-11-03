// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Service
{
    /// <summary>
    /// Acquires the durable per-company coordination row before policy-sensitive work.
    /// A process-local revision or cache invalidation alone is insufficient.
    /// </summary>
    public sealed class WorkspacePolicyTransaction : IWorkspacePolicyTransaction
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;

        public WorkspacePolicyTransaction(ApiDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        public void Execute(int companyId, Action<WorkspacePolicyGuard> action) =>
            Execute(companyId, guard =>
            {
                action(guard);
                return true;
            });

        public T Execute<T>(int companyId, Func<WorkspacePolicyGuard, T> action)
        {
            if (companyId <= 0)
                throw new ArgumentOutOfRangeException(nameof(companyId));

            return ExecuteAcrossCompanies(new[] { companyId }, guards => action(guards[0]));
        }

        public void ExecuteAcrossCompanies(IEnumerable<int> companyIds, Action<IReadOnlyList<WorkspacePolicyGuard>> action) =>
            ExecuteAcrossCompanies(companyIds, guards =>
            {
                action(guards);
                return true;
            });

        public T ExecuteAcrossCompanies<T>(IEnumerable<int> companyIds, Func<IReadOnlyList<WorkspacePolicyGuard>, T> action)
        {
            var sortedCompanyIds = (companyIds ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            using var impersonation = sortedCompanyIds.Count == 1 && _tenant != null && _tenant.CompanyId != sortedCompanyIds[0]
                ? _tenant.Impersonate(sortedCompanyIds[0])
                : null;

            var priorSuppress = _db.SuppressTenantSecurityCheck;
            if (sortedCompanyIds.Count > 1)
                _db.SuppressTenantSecurityCheck = true;

            var ownsTransaction = _db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = null;
            try
            {
                if (ownsTransaction)
                    tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);

                var guards = new List<WorkspacePolicyGuard>();
                foreach (var cid in sortedCompanyIds)
                {
                    var settings = AcquireCoordination(cid);
                    guards.Add(new WorkspacePolicyGuard(cid, settings));
                    WorkspacePolicyTestGate.AfterLockAcquired?.Invoke(cid);
                }

                var result = action(guards);

                if (IsFailedResult(result))
                {
                    if (ownsTransaction)
                        tx?.Rollback();
                    return result;
                }

                // Always flush guarded mutations; only commit when this call owns the transaction.
                _db.SaveChanges();
                if (ownsTransaction)
                    tx!.Commit();

                try
                {
                    if (WorkspacePolicyTestGate.PostCommitVerificationFault != null)
                    {
                        foreach (var guard in guards)
                        {
                            WorkspacePolicyTestGate.PostCommitVerificationFault(guard.CompanyId);
                        }
                    }

                    VerifyPostCommitInvariants(guards);
                }
                catch (Exception)
                {
                    // CRITICAL: Database transaction has ALREADY committed durably.
                    // An exception during telemetry or invariant evaluation must NOT bubble up
                    // to fail the caller request, which would falsely report failure for a
                    // committed financial or policy mutation.
                    WorkspaceOperatingPolicyMetrics.PostCommitDetectorExceptions.Add(1);
                }

                return result;
            }
            catch
            {
                if (ownsTransaction)
                    tx?.Rollback();
                throw;
            }
            finally
            {
                if (sortedCompanyIds.Count > 1)
                    _db.SuppressTenantSecurityCheck = priorSuppress;
                if (ownsTransaction)
                    tx?.Dispose();
            }
        }

        private void VerifyPostCommitInvariants(IReadOnlyList<WorkspacePolicyGuard> guards)
        {
            foreach (var guard in guards)
            {
                CheckCompanyInvariants(_db, guard.CompanyId, guard.Settings);
            }
        }

        public static void CheckCompanyInvariants(ApiDbContext db, int companyId, WorkspaceOperatingSettings settings)
        {
            // 1. Inconsistent transition detection:
            // SoloActive must have ConfirmedOwnerMemberId, cannot have EverTeamActivatedAtUtc,
            // and cannot have more than 1 active member.
            if (settings.CollaborationState == WorkspaceCollaborationState.SoloActive)
            {
                var activeMemberCount = db.CompanyMembers
                    .IgnoreQueryFilters()
                    .Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);

                if (!settings.ConfirmedOwnerMemberId.HasValue || settings.EverTeamActivatedAtUtc.HasValue || activeMemberCount > 1)
                {
                    WorkspaceOperatingPolicyMetrics.InconsistentTransitionsCommitted.Add(1);
                }
            }

            // 2. Unauthorized elevation detection:
            // SingleActorAllowed rule must never exist if CollaborationState != SoloActive,
            // or if ConfirmedOwnerMemberId is missing, or if EverTeamActivatedAtUtc is present.
            var hasSingleActor = db.WorkspaceApprovalRules
                .IgnoreQueryFilters()
                .Any(r => r.CompanyId == companyId && r.RuleKind == WorkspaceApprovalRuleKind.SingleActorAllowed);

            if (hasSingleActor)
            {
                if (settings.CollaborationState != WorkspaceCollaborationState.SoloActive ||
                    !settings.ConfirmedOwnerMemberId.HasValue ||
                    settings.EverTeamActivatedAtUtc.HasValue)
                {
                    WorkspaceOperatingPolicyMetrics.UnauthorizedElevationsCommitted.Add(1);
                }
            }
        }

        private static bool IsFailedResult<T>(T result)
        {
            if (result is null) return false;

            if (result is Microsoft.AspNetCore.Mvc.IActionResult actionResult)
            {
                if (actionResult is Microsoft.AspNetCore.Mvc.ObjectResult obj && obj.StatusCode >= 400) return true;
                if (actionResult is Microsoft.AspNetCore.Mvc.StatusCodeResult sc && sc.StatusCode >= 400) return true;
                if (actionResult is Microsoft.AspNetCore.Mvc.ForbidResult or Microsoft.AspNetCore.Mvc.UnauthorizedResult
                    or Microsoft.AspNetCore.Mvc.BadRequestResult or Microsoft.AspNetCore.Mvc.NotFoundResult
                    or Microsoft.AspNetCore.Mvc.ConflictResult) return true;
            }

            if (result is WorkspacePolicyMutationResult mut && !mut.Applied) return true;

            var propApplied = typeof(T).GetProperty("Applied");
            if (propApplied != null && propApplied.PropertyType == typeof(bool) && !(bool)propApplied.GetValue(result)!) return true;

            var propSuccess = typeof(T).GetProperty("Success");
            if (propSuccess != null && propSuccess.PropertyType == typeof(bool) && !(bool)propSuccess.GetValue(result)!) return true;

            var propAllowed = typeof(T).GetProperty("Allowed");
            if (propAllowed != null && propAllowed.PropertyType == typeof(bool) && !(bool)propAllowed.GetValue(result)!) return true;

            return false;
        }

        private WorkspaceOperatingSettings AcquireCoordination(int companyId)
        {
            EnsureCoordinationRow(companyId);
            LockCoordinationRow(companyId);

            var settings = _db.WorkspaceOperatingSettings
                .IgnoreQueryFilters()
                .SingleOrDefault(s => s.CompanyId == companyId);

            if (settings == null)
            {
                settings = new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    Purpose = WorkspacePurpose.Unspecified,
                    CollaborationIntent = WorkspaceCollaborationIntent.Unspecified,
                    CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                    InitializationSource = WorkspaceInitializationSource.Unspecified,
                    PolicyRevision = 0,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                };
                _db.WorkspaceOperatingSettings.Add(settings);
                _db.SaveChanges();
            }

            if (settings.PolicySchemaVersion > WorkspaceOperatingPolicySchema.CurrentVersion)
            {
                throw new IncompatiblePolicySchemaException(
                    companyId,
                    settings.PolicySchemaVersion,
                    WorkspaceOperatingPolicySchema.CurrentVersion);
            }

            var coordination = _db.WorkspacePolicyCoordinations
                .IgnoreQueryFilters()
                .Single(c => c.CompanyId == companyId);
            coordination.LastAcquiredAtUtc = DateTime.UtcNow;

            return settings;
        }

        private void EnsureCoordinationRow(int companyId)
        {
            if (_db.WorkspacePolicyCoordinations.IgnoreQueryFilters().Any(c => c.CompanyId == companyId))
                return;

            _db.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
            {
                CompanyId = companyId,
                CreatedAtUtc = DateTime.UtcNow
            });
            _db.SaveChanges();
        }

        private void LockCoordinationRow(int companyId)
        {
            var provider = _db.Database.ProviderName ?? string.Empty;
            var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText =
                    @"SELECT ""Id"" FROM ""WorkspacePolicyCoordination"" WHERE ""CompanyId"" = @companyId FOR UPDATE";
            }
            else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText =
                    @"SELECT Id FROM WorkspacePolicyCoordination WITH (UPDLOCK, ROWLOCK) WHERE CompanyId = @companyId";
            }
            else
            {
                // SQLite: execute an update to acquire write lock immediately and serialize concurrent transactions.
                command.CommandText =
                    @"UPDATE ""WorkspacePolicyCoordination"" SET ""LastAcquiredAtUtc"" = CURRENT_TIMESTAMP WHERE ""CompanyId"" = @companyId";
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@companyId";
            parameter.Value = companyId;
            command.Parameters.Add(parameter);
            command.ExecuteNonQuery();
        }
    }
}
