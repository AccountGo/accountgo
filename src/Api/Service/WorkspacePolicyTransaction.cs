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

        // Deferred work bound to the EF transaction that was active when it was enqueued. Binding to the
        // originating transaction instance is what makes rollback honest: work enqueued under transaction
        // T may only be drained when T itself commits, and is discarded the moment T goes away without
        // this service draining it (rollback, ChangeTracker.Clear + dispose, or any abandon). A LATER
        // transaction can never drain an EARLIER transaction's work. Scoped per DbContext/request.
        private sealed class PendingItem
        {
            // The transaction instance in scope at enqueue time. Null means "no ambient transaction existed"
            // — the enqueuing Execute owned a transaction it created; such items are keyed by that owned
            // transaction below before this call returns, so a null here is only transient within a call.
            public IDbContextTransaction? Transaction;
            public int? CompanyToVerify;
            public Action? PostCommit;
        }

        private readonly List<PendingItem> _pending = new();

        public WorkspacePolicyTransaction(ApiDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        /// <summary>
        /// Registers an action to run only after the owning transaction commits durably (and never on
        /// rollback). Use for post-commit side effects such as telemetry. The action is bound to the EF
        /// transaction active right now; it runs only if THAT transaction commits and is discarded if it
        /// rolls back or is abandoned. If no transaction is active, the caller is the owner and the action
        /// runs on this call's commit.
        /// </summary>
        public void EnqueuePostCommit(Action action)
        {
            if (action == null)
                return;
            EvictOrphanedPending();
            _pending.Add(new PendingItem
            {
                Transaction = _db.Database.CurrentTransaction,
                PostCommit = action
            });
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

            // Drop any work left bound to a transaction that has since gone away without this service
            // draining it (an earlier rollback path that only cleared EF tracking). This runs before we
            // observe the current transaction, so such stale work can never be attributed to — or drained
            // by — the transaction this call is about to own or join.
            EvictOrphanedPending();

            var ownsTransaction = _db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = null;
            try
            {
                if (ownsTransaction)
                    tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);

                // The transaction that items enqueued during this call must be bound to: the one we just
                // created, or the caller-owned ambient one we are joining.
                var activeTx = tx ?? _db.Database.CurrentTransaction;

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
                        // Nothing committed: discard every item bound to this rolled-back transaction,
                        // including any nested enqueues made during the action above.
                        DiscardPendingForTransaction(activeTx);
                    }
                    return result;
                }

                // Always flush guarded mutations; only commit when this call owns the transaction.
                _db.SaveChanges();

                // Bind this call's own verification obligations to the active transaction, so they share
                // the same commit/rollback fate as any post-commit side effects enqueued by the action.
                foreach (var guard in guards)
                    _pending.Add(new PendingItem { Transaction = activeTx, CompanyToVerify = guard.CompanyId });

                if (!ownsTransaction)
                {
                    // Nested in a caller-owned transaction: committed state is not established yet. The
                    // owner drains items bound to that transaction after it commits (see
                    // VerifyPendingAfterExternalCommit) or discards them on rollback. Verifying now would
                    // read uncommitted state.
                    return result;
                }

                tx!.Commit();

                // Collect this transaction's own work, then DISPOSE the transaction before draining so the
                // DbContext has no current transaction. Verification opens its OWN serializable transaction
                // and must see CurrentTransaction == null; a committed-but-undisposed transaction would
                // otherwise be mistaken for an ambient one. Side effects (telemetry) fire only on durable
                // success. Items bound to other (caller-owned) transactions are left for their owners.
                var mine = _pending.Where(p => ReferenceEquals(p.Transaction, activeTx)).ToList();
                _pending.RemoveAll(p => mine.Contains(p));
                tx.Dispose();
                tx = null;

                var toVerify = new HashSet<int>(mine.Where(p => p.CompanyToVerify.HasValue).Select(p => p.CompanyToVerify!.Value));
                RunCommittedVerification(toVerify);
                RunPostCommitActions(mine.Where(p => p.PostCommit != null).Select(p => p.PostCommit!).ToList());

                return result;
            }
            catch
            {
                if (ownsTransaction)
                {
                    tx?.Rollback();
                    DiscardPendingForTransaction(tx);
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

        /// <inheritdoc />
        public void VerifyPendingAfterExternalCommit()
        {
            // The external owner has committed its transaction and is telling us so. Drain the work bound to
            // that transaction. "That transaction" is whatever is NOT a different, still-live open
            // transaction: it may be the current transaction (committed but not yet disposed) or a disposed
            // one (committed then the `using` block disposed it). We must NOT evict-then-skip here — unlike
            // the safety-net eviction in Execute, this is the owner's explicit drain signal, so a disposed
            // transaction means "committed and gone", which drains, not "abandoned", which would discard.
            var ready = _pending.Where(p => !IsDifferentLiveTransaction(p.Transaction)).ToList();
            _pending.RemoveAll(p => ready.Contains(p));

            var toVerify = new HashSet<int>(ready.Where(p => p.CompanyToVerify.HasValue).Select(p => p.CompanyToVerify!.Value));
            RunCommittedVerification(toVerify);
            RunPostCommitActions(ready.Where(p => p.PostCommit != null).Select(p => p.PostCommit!).ToList());
        }

        /// <inheritdoc />
        public void DiscardPendingVerifications()
        {
            // The external owner is rolling back and is telling us so. Discard the work bound to that
            // transaction (current-or-already-disposed), leaving work bound to a different, still-live
            // transaction alone.
            _pending.RemoveAll(p => !IsDifferentLiveTransaction(p.Transaction));
        }

        /// <summary>
        /// True when the item's transaction is a live, open transaction OTHER than the DbContext's current
        /// one — i.e. work that belongs to some other in-flight transaction and must be left for its owner.
        /// A null, dead/disposed, or current transaction is NOT "a different live transaction".
        /// </summary>
        private bool IsDifferentLiveTransaction(IDbContextTransaction? transaction)
        {
            if (transaction == null)
                return false;
            if (ReferenceEquals(transaction, _db.Database.CurrentTransaction))
                return false;
            return !IsDeadTransaction(transaction);
        }

        /// <summary>
        /// Drops pending work whose originating transaction is no longer usable: it has been disposed or
        /// otherwise detached from the DbContext. Because the owning-commit path always drains its own work
        /// synchronously right after commit, any surviving item bound to a transaction that is neither the
        /// current open transaction nor a live transaction must belong to a transaction that rolled back or
        /// was abandoned (e.g. a controller that cleared EF tracking without calling discard). Such work
        /// never committed and must never run.
        /// </summary>
        private void EvictOrphanedPending()
        {
            if (_pending.Count == 0)
                return;
            var current = _db.Database.CurrentTransaction;
            _pending.RemoveAll(p =>
                p.Transaction != null
                && !ReferenceEquals(p.Transaction, current)
                && IsDeadTransaction(p.Transaction));
        }

        /// <summary>
        /// A transaction is dead once its underlying DB transaction/connection is gone. Disposed EF
        /// transactions throw or report a null/closed connection when inspected; treat any failure as dead.
        /// </summary>
        private static bool IsDeadTransaction(IDbContextTransaction? transaction)
        {
            if (transaction == null)
                return true;
            try
            {
                // GetDbTransaction throws ObjectDisposedException on a disposed EF transaction; a live one
                // exposes its ADO.NET transaction, whose Connection is non-null until it completes.
                var dbTx = transaction.GetDbTransaction();
                return dbTx == null || dbTx.Connection == null;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Discards, for the given rolled-back transaction only, all its pending work.</summary>
        private void DiscardPendingForTransaction(IDbContextTransaction? rolledBackTx)
        {
            _pending.RemoveAll(p => ReferenceEquals(p.Transaction, rolledBackTx));
        }

        /// <summary>Runs queued post-commit side effects; a failure in one never affects others or the
        /// already-committed request.</summary>
        private static void RunPostCommitActions(IReadOnlyList<Action> actions)
        {
            if (actions.Count == 0)
                return;
            foreach (var action in actions)
            {
                try { action(); }
                catch { WorkspaceOperatingPolicyMetrics.PostCommitDetectorExceptions.Add(1); }
            }
        }

        /// <summary>
        /// Runs committed-state verification for the given companies. Each company is verified under its
        /// own coordination lock in a fresh serializable transaction so settings, membership and rules are
        /// read as one consistent snapshot and reporting is serialized against concurrent writers and
        /// detectors. Any failure is swallowed and counted as a scanner exception — verification must never
        /// fail an already-committed request.
        /// </summary>
        private void RunCommittedVerification(HashSet<int> companyIds)
        {
            foreach (var companyId in companyIds)
            {
                try
                {
                    WorkspacePolicyTestGate.PostCommitVerificationFault?.Invoke(companyId);
                    VerifyCommittedCompanyUnderLock(_db, _tenant, companyId);
                }
                catch (Exception)
                {
                    // The caller's transaction is already durably committed; a verification failure is a
                    // scanner exception, kept distinct from current violations and detected events.
                    WorkspaceOperatingPolicyMetrics.PostCommitDetectorExceptions.Add(1);
                }
            }
        }

        /// <summary>
        /// Verifies one company's committed invariants under its coordination lock in a fresh transaction,
        /// reconciling the current signature against the durable incident log and emitting the counter only
        /// after the incident row commits. Returns the reconciliation outcome. Shared by the inline owner
        /// path and the recovery sweep so both get identical locking, snapshot and counter semantics.
        /// </summary>
        public static WorkspaceInvariantIncidentLog.ReconcileOutcome VerifyCommittedCompanyUnderLock(
            ApiDbContext db, ITenantContext? tenant, int companyId)
        {
            using var impersonation = tenant != null && tenant.CompanyId != companyId
                ? tenant.Impersonate(companyId)
                : null;

            var ownsTx = db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = ownsTx ? db.Database.BeginTransaction(IsolationLevel.Serializable) : null;
            try
            {
                // Lock the coordination row so this read-and-report is serialized against any concurrent
                // policy writer or detector for the same company (prevents duplicate reports and torn
                // settings/membership snapshots).
                LockCoordinationRowStatic(db, companyId);

                var settings = db.WorkspaceOperatingSettings
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefault(s => s.CompanyId == companyId);
                if (settings == null)
                {
                    if (ownsTx) tx!.Commit();
                    return WorkspaceInvariantIncidentLog.ReconcileOutcome.NoChange;
                }

                var signature = ComputeViolationSignature(db, companyId, settings);
                var outcome = WorkspaceInvariantIncidentLog.Reconcile(db, companyId, signature, DateTime.UtcNow);

                if (ownsTx) tx!.Commit();

                // Emit the counter only after the incident marker has committed.
                if (outcome == WorkspaceInvariantIncidentLog.ReconcileOutcome.NewIncident && signature != null)
                {
                    if (signature.StartsWith("inconsistent_transition:", StringComparison.Ordinal))
                        WorkspaceOperatingPolicyMetrics.InconsistentTransitionsCommitted.Add(1);
                    else if (signature.StartsWith("unauthorized_elevation:", StringComparison.Ordinal))
                        WorkspaceOperatingPolicyMetrics.UnauthorizedElevationsCommitted.Add(1);
                }

                return outcome;
            }
            catch
            {
                if (ownsTx) tx?.Rollback();
                throw;
            }
            finally
            {
                if (ownsTx) tx?.Dispose();
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

        private void LockCoordinationRow(int companyId) => LockCoordinationRowStatic(_db, companyId);

        internal static void LockCoordinationRowStatic(ApiDbContext db, int companyId)
        {
            // Ensure a coordination row exists so the lock has a target even for legacy/uninitialized rows.
            if (!db.WorkspacePolicyCoordinations.IgnoreQueryFilters().Any(c => c.CompanyId == companyId))
            {
                db.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
                {
                    CompanyId = companyId,
                    CreatedAtUtc = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var provider = db.Database.ProviderName ?? string.Empty;
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

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
