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

        // Companies whose committed state must be verified once the OWNING transaction commits. When a
        // nested policy transaction runs inside a caller-owned transaction, it cannot verify committed
        // state itself (the outer transaction has not committed). It enqueues here; the owning call drains
        // the queue after its commit. Scoped per DbContext/instance, matching the request scope.
        private readonly HashSet<int> _pendingCommittedVerification = new();

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
                    {
                        tx?.Rollback();
                        // Nothing committed: discard any deferred verifications from nested calls.
                        _pendingCommittedVerification.Clear();
                    }
                    return result;
                }

                // Always flush guarded mutations; only commit when this call owns the transaction.
                _db.SaveChanges();

                if (!ownsTransaction)
                {
                    // Nested in a caller-owned transaction: committed state is not established yet.
                    // Defer verification to the owner, which drains this set after it commits. Verifying
                    // now would read uncommitted state and could both miss the final state and mislabel
                    // rolled-back state as committed.
                    foreach (var guard in guards)
                        _pendingCommittedVerification.Add(guard.CompanyId);
                    return result;
                }

                tx!.Commit();

                // Verify only after a real commit. Include any companies deferred by nested calls in the
                // same owning transaction. Failures here must never fail the already-committed request.
                var toVerify = new HashSet<int>(_pendingCommittedVerification);
                foreach (var guard in guards)
                    toVerify.Add(guard.CompanyId);
                _pendingCommittedVerification.Clear();

                try
                {
                    if (WorkspacePolicyTestGate.PostCommitVerificationFault != null)
                    {
                        foreach (var companyId in toVerify)
                            WorkspacePolicyTestGate.PostCommitVerificationFault(companyId);
                    }

                    VerifyCommittedInvariants(toVerify);
                }
                catch (Exception)
                {
                    // CRITICAL: Database transaction has ALREADY committed durably.
                    // An exception during telemetry or invariant evaluation must NOT bubble up
                    // to fail the caller request, which would falsely report failure for a
                    // committed financial or policy mutation. This is a scanner failure, tracked
                    // separately from current violations and detected events.
                    WorkspaceOperatingPolicyMetrics.PostCommitDetectorExceptions.Add(1);
                }

                return result;
            }
            catch
            {
                if (ownsTransaction)
                {
                    tx?.Rollback();
                    _pendingCommittedVerification.Clear();
                }
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

        /// <summary>
        /// Verifies committed invariants for the given companies, re-reading each from the database so the
        /// check reflects the committed row (not an in-memory guard snapshot). A newly observed violation
        /// increments the committed-violation counter exactly once per signature; an already-reported
        /// signature is not re-counted.
        /// </summary>
        private void VerifyCommittedInvariants(IEnumerable<int> companyIds)
        {
            foreach (var companyId in companyIds)
            {
                var settings = _db.WorkspaceOperatingSettings
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefault(s => s.CompanyId == companyId);
                if (settings == null)
                    continue;

                ReportCommittedViolation(_db, companyId, settings);
            }
        }

        /// <summary>
        /// Computes the current committed invariant violation for a workspace, if any, and returns a stable
        /// signature string identifying it (or null when consistent). Pure read; increments no counters.
        /// </summary>
        public static string? ComputeViolationSignature(ApiDbContext db, int companyId, WorkspaceOperatingSettings settings)
        {
            // 1. Collaboration-state consistency.
            if (settings.CollaborationState == WorkspaceCollaborationState.SoloActive)
            {
                var activeMemberCount = db.CompanyMembers
                    .IgnoreQueryFilters()
                    .Count(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active);

                var ownerMember = settings.ConfirmedOwnerMemberId.HasValue
                    ? db.CompanyMembers.IgnoreQueryFilters().FirstOrDefault(m => m.Id == settings.ConfirmedOwnerMemberId.Value && m.CompanyId == companyId)
                    : null;

                bool ownerValid = ownerMember != null && ownerMember.Status == CompanyMemberStatus.Active && ownerMember.Role == CompanyMemberRole.Owner;

                if (!ownerValid)
                    return "inconsistent_transition:solo_active_missing_owner";
                if (activeMemberCount > 1)
                    return "inconsistent_transition:solo_active_multiple_members";
            }

            // 2. Unauthorized elevation detection.
            var hasSingleActor = db.WorkspaceApprovalRules
                .IgnoreQueryFilters()
                .Any(r => r.CompanyId == companyId && r.RuleKind == WorkspaceApprovalRuleKind.SingleActorAllowed);

            if (hasSingleActor)
            {
                bool isUninitialized = settings.CollaborationState == WorkspaceCollaborationState.LegacyUnclassified
                                      || !settings.InitializedAtUtc.HasValue;
                bool missingOwner = !settings.ConfirmedOwnerMemberId.HasValue;

                if (isUninitialized)
                    return "unauthorized_elevation:single_actor_uninitialized";
                if (missingOwner)
                    return "unauthorized_elevation:single_actor_missing_owner";
            }

            return null;
        }

        /// <summary>
        /// Reports a currently-committed violation exactly once per signature, incrementing the appropriate
        /// committed-violation counter only when the signature is newly observed. Distinguishes current
        /// violation (signature != null) from a newly detected event (counter increment) and from an
        /// already-reported signature (no increment). Persists the "reported" marker in the same context.
        /// </summary>
        public static void ReportCommittedViolation(ApiDbContext db, int companyId, WorkspaceOperatingSettings settings)
        {
            var signature = ComputeViolationSignature(db, companyId, settings);
            if (signature == null)
                return;

            var newlyReported = WorkspacePolicyDurableEvidence.TryMarkViolationNewlyReported(
                db, companyId, signature, DateTime.UtcNow);
            if (!newlyReported)
                return;

            if (signature.StartsWith("inconsistent_transition:", StringComparison.Ordinal))
                WorkspaceOperatingPolicyMetrics.InconsistentTransitionsCommitted.Add(1);
            else if (signature.StartsWith("unauthorized_elevation:", StringComparison.Ordinal))
                WorkspaceOperatingPolicyMetrics.UnauthorizedElevationsCommitted.Add(1);

            db.SaveChanges();
        }

        /// <summary>
        /// Legacy entry point retained for compatibility. Reports a currently-committed violation exactly
        /// once per signature (idempotent); callers should prefer <see cref="ReportCommittedViolation"/>.
        /// Note: this must be called against committed state, not an in-flight guard snapshot.
        /// </summary>
        public static void CheckCompanyInvariants(ApiDbContext db, int companyId, WorkspaceOperatingSettings settings)
        {
            ReportCommittedViolation(db, companyId, settings);
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
