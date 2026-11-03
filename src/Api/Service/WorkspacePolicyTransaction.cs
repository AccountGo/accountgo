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

        // Deferred work bound to the SCOPE (a specific owning transaction) that was active when it was
        // enqueued. A scope id is minted for each owning transaction — one this service creates in Execute,
        // or one an external owner registers via BeginExternalScope — and every item carries that id.
        // Completion is explicit per scope: the owner says "scope N committed" (drain scope N) or "scope N
        // rolled back" (discard scope N). We NEVER infer commit from disposal, so a later committed scope
        // can never drain an earlier rolled-back scope's work. Scoped per DbContext/request.
        private sealed class PendingItem
        {
            public long ScopeId;
            public int? CompanyToVerify;
            public Action? PostCommit;
        }

        private readonly List<PendingItem> _pending = new();

        // Live scopes, keyed by their owning EF transaction instance, with the scope id assigned to each.
        // An entry exists only while its transaction is open; it is removed on completion (commit/rollback)
        // or evicted when the transaction is found dead without having been completed (safety net).
        private readonly List<(long ScopeId, IDbContextTransaction Transaction)> _scopes = new();
        private long _scopeCounter;

        /// <summary>
        /// Diagnostic counts of the deferred-work and scope tables. Exposed so tests can assert that
        /// abandoned scopes and their pending work are released (not merely left un-emitted) and that
        /// neither list grows without bound. Not part of the policy contract.
        /// </summary>
        public int PendingItemCount => _pending.Count;

        public int ScopeCount => _scopes.Count;

        public WorkspacePolicyTransaction(ApiDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        /// <summary>
        /// Returns the scope id for the DbContext's current ambient transaction, registering one on first
        /// use. Work enqueued while that transaction is ambient binds to this scope. When there is no
        /// ambient transaction, returns 0 (the "no owning transaction" sentinel), which an owning Execute
        /// re-keys to the transaction it is about to create.
        /// </summary>
        private long CurrentScopeId()
        {
            var current = _db.Database.CurrentTransaction;
            if (current == null)
                return 0;
            var existing = _scopes.FirstOrDefault(s => ReferenceEquals(s.Transaction, current));
            if (existing.Transaction != null)
                return existing.ScopeId;
            var id = ++_scopeCounter;
            _scopes.Add((id, current));
            return id;
        }

        /// <summary>
        /// Registers an action to run only after the owning transaction commits durably (and never on
        /// rollback). Use for post-commit side effects such as telemetry. The action is bound to the scope
        /// of the EF transaction active right now; it runs only when THAT scope is completed as committed
        /// and is discarded if it rolls back or is abandoned. If no transaction is active, the caller is
        /// the owner and the action runs on this call's commit.
        /// </summary>
        public void EnqueuePostCommit(Action action)
        {
            if (action == null)
                return;
            EvictOrphanedPending();
            _pending.Add(new PendingItem
            {
                ScopeId = CurrentScopeId(),
                PostCommit = action
            });
        }

        /// <inheritdoc />
        public IWorkspacePolicyCompletionToken BeginExternalScope()
        {
            // The owner has just begun its transaction; capture it and mint a dedicated scope id so all
            // work enqueued until the owner completes the token binds to exactly this transaction.
            var current = _db.Database.CurrentTransaction
                ?? throw new InvalidOperationException("BeginExternalScope requires an active transaction owned by the caller.");
            var existing = _scopes.FirstOrDefault(s => ReferenceEquals(s.Transaction, current));
            var scopeId = existing.Transaction != null ? existing.ScopeId : ++_scopeCounter;
            if (existing.Transaction == null)
                _scopes.Add((scopeId, current));
            return new CompletionToken(this, scopeId);
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

            // Drop any work left bound to a scope whose transaction has since gone away without being
            // completed by its owner (an earlier rollback path that only cleared EF tracking). This runs
            // before we observe or create the current transaction, so such stale work can never be
            // attributed to — or drained by — the scope this call is about to own or join.
            EvictOrphanedPending();

            var ownsTransaction = _db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = null;
            try
            {
                if (ownsTransaction)
                    tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);

                // The scope that items enqueued during this call bind to: a fresh scope for a transaction
                // we just created, or the scope of the caller-owned ambient transaction we are joining.
                long scopeId;
                if (ownsTransaction)
                {
                    scopeId = ++_scopeCounter;
                    _scopes.Add((scopeId, tx!));
                }
                else
                {
                    scopeId = CurrentScopeId();
                }

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
                        // Nothing committed: discard every item bound to this rolled-back scope,
                        // including any nested enqueues made during the action above.
                        DiscardScope(scopeId);
                    }
                    return result;
                }

                // Always flush guarded mutations; only commit when this call owns the transaction.
                _db.SaveChanges();

                // Bind this call's own verification obligations to the active scope, so they share the same
                // commit/rollback fate as any post-commit side effects enqueued by the action.
                foreach (var guard in guards)
                    _pending.Add(new PendingItem { ScopeId = scopeId, CompanyToVerify = guard.CompanyId });

                if (!ownsTransaction)
                {
                    // Nested in a caller-owned transaction: committed state is not established yet. The
                    // owner completes its scope after it commits (drain) or rolls back (discard). Verifying
                    // now would read uncommitted state.
                    return result;
                }

                tx!.Commit();

                // This call owns the transaction and it committed. DISPOSE it before draining so the
                // DbContext has no current transaction — verification opens its OWN serializable
                // transaction and must see CurrentTransaction == null. Drain ONLY this scope's work; side
                // effects (telemetry) fire only on durable success. Other scopes' work is left untouched.
                tx.Dispose();
                tx = null;
                DrainScopeCommitted(scopeId);

                return result;
            }
            catch
            {
                if (ownsTransaction)
                {
                    tx?.Rollback();
                    // An exception rolled this scope back; discard its work. The scope id is the latest we
                    // minted for the transaction we own, if we reached that point.
                    DiscardScopeForTransaction(tx);
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

        // ----- scope completion primitives -------------------------------------------------------

        /// <summary>Drains, for one committed scope, its verifications and post-commit side effects.</summary>
        private void DrainScopeCommitted(long scopeId)
        {
            var mine = _pending.Where(p => p.ScopeId == scopeId).ToList();
            _pending.RemoveAll(p => p.ScopeId == scopeId);
            _scopes.RemoveAll(s => s.ScopeId == scopeId);
            PruneScopes();

            var toVerify = new HashSet<int>(mine.Where(p => p.CompanyToVerify.HasValue).Select(p => p.CompanyToVerify!.Value));
            RunCommittedVerification(toVerify);
            RunPostCommitActions(mine.Where(p => p.PostCommit != null).Select(p => p.PostCommit!).ToList());
        }

        /// <summary>Discards, for one rolled-back/abandoned scope, all its pending work.</summary>
        private void DiscardScope(long scopeId)
        {
            _pending.RemoveAll(p => p.ScopeId == scopeId);
            _scopes.RemoveAll(s => s.ScopeId == scopeId);
            PruneScopes();
        }

        private void DiscardScopeForTransaction(IDbContextTransaction? transaction)
        {
            if (transaction == null)
                return;
            var scope = _scopes.FirstOrDefault(s => ReferenceEquals(s.Transaction, transaction));
            if (scope.Transaction != null)
                DiscardScope(scope.ScopeId);
        }

        /// <summary>
        /// Safety net for an owner that completed NEITHER commit nor rollback on its scope (e.g. a path that
        /// rolled back and only cleared EF tracking). Discards pending work whose scope's transaction is
        /// dead/disposed and is not the current one. This runs at the start of Execute and EnqueuePostCommit
        /// — a NEW operation — so stale work is dropped before any new scope can be attributed to it. It is
        /// deliberately NOT run inside the explicit commit/rollback completion paths, which must act on
        /// their own scope regardless of disposal. Delegates to <see cref="PruneScopes"/>, which releases
        /// both the dead scope registrations and their stranded pending items as a single invariant.
        /// </summary>
        private void EvictOrphanedPending() => PruneScopes();

        /// <summary>
        /// Removes scope registrations whose transaction is dead, and releases any pending work still
        /// bound to those scopes. An abandoned owner (one that neither committed nor rolled back its
        /// scope, e.g. a path that rolled back and only cleared EF tracking) would otherwise leave its
        /// queued post-commit callbacks and deferred verifications stranded in _pending forever: never
        /// drained, never emitted, and never garbage-collected. Dropping the pending items here keeps
        /// _pending and _scopes bounded together and guarantees an abandoned scope's callbacks are
        /// released WITHOUT emission (a dead/rolled-back transaction never committed, so its post-commit
        /// side effects must not run). The current transaction's scope is always retained.
        /// </summary>
        private void PruneScopes()
        {
            var current = _db.Database.CurrentTransaction;
            if (_scopes.Count == 0)
                return;
            var deadScopes = _scopes
                .Where(s => !ReferenceEquals(s.Transaction, current) && IsDeadTransaction(s.Transaction))
                .Select(s => s.ScopeId)
                .ToHashSet();
            if (deadScopes.Count == 0)
                return;
            _pending.RemoveAll(p => deadScopes.Contains(p.ScopeId));
            _scopes.RemoveAll(s => deadScopes.Contains(s.ScopeId));
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

        /// <summary>
        /// One-shot completion handle bound to a specific scope id. Explicit commit/rollback means the
        /// policy service acts on exactly this scope's work, independent of transaction disposal.
        /// </summary>
        private sealed class CompletionToken : IWorkspacePolicyCompletionToken
        {
            private readonly WorkspacePolicyTransaction _owner;
            private readonly long _scopeId;
            private bool _completed;

            public CompletionToken(WorkspacePolicyTransaction owner, long scopeId)
            {
                _owner = owner;
                _scopeId = scopeId;
            }

            public void Committed()
            {
                if (_completed) return;
                _completed = true;
                _owner.DrainScopeCommitted(_scopeId);
            }

            public void RolledBack()
            {
                if (_completed) return;
                _completed = true;
                _owner.DiscardScope(_scopeId);
            }
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
