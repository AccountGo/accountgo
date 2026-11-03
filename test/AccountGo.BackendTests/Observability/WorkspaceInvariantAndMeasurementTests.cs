// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Observability
{
    /// <summary>
    /// Slices 3 and 4. Verifies commit-accurate, idempotent invariant detection (no double-counting of a
    /// persisted violation), the recovery sweep for crash-left violations, and the durable, unique
    /// first-useful-action marker that underpins honest measurement.
    ///
    /// These tests observe the process-global AccountGo.WorkspacePolicy meter, so they share a
    /// non-parallel collection with the other meter-listening tests to keep counter measurements isolated.
    /// </summary>
    [Collection("WorkspacePolicyMeter")]
    public sealed class WorkspaceInvariantAndMeasurementTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ApiDbContext _db;
        private readonly TenantContext _tenant = new();
        private readonly MeterListener _listener;
        private readonly List<(string Name, long Value)> _counters = new();
        private readonly List<(string Name, double Value)> _histograms = new();

        public WorkspaceInvariantAndMeasurementTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
            _db.Database.EnsureCreated();

            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == WorkspaceOperatingPolicyMetrics.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                lock (_counters) _counters.Add((instrument.Name, measurement));
            });
            _listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
            {
                lock (_counters) _counters.Add((instrument.Name, measurement));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                lock (_histograms) _histograms.Add((instrument.Name, measurement));
            });
            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
            _db.Dispose();
            _connection.Dispose();
        }

        private long CounterTotal(string instrumentName)
        {
            lock (_counters)
                return _counters.Where(c => c.Name == instrumentName).Sum(c => c.Value);
        }

        private int HistogramCount(string instrumentName)
        {
            lock (_histograms)
                return _histograms.Count(h => h.Name == instrumentName);
        }

        private Company SeedCompany(string slug, string ownerUserId, out CompanyMember owner)
        {
            _db.SuppressTenantSecurityCheck = true;
            var company = new Company { Name = slug, Slug = slug, CreatedByUserId = ownerUserId };
            _db.Companies.Add(company);
            _db.SaveChanges();

            owner = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = ownerUserId,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            _db.CompanyMembers.Add(owner);
            _db.SaveChanges();
            return company;
        }

        private WorkspaceOperatingSettings SeedSettings(int companyId, WorkspaceCollaborationState state, int? confirmedOwnerMemberId, bool initialized)
        {
            var settings = new WorkspaceOperatingSettings
            {
                CompanyId = companyId,
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                CollaborationState = state,
                ConfirmedOwnerMemberId = confirmedOwnerMemberId,
                InitializedAtUtc = initialized ? DateTime.UtcNow : (DateTime?)null,
                PolicyRevision = 1,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
            };
            _db.WorkspaceOperatingSettings.Add(settings);
            _db.SaveChanges();
            return settings;
        }

        private void SeedSingleActorRule(int companyId)
        {
            _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
            {
                CompanyId = companyId,
                OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion,
                ConfiguredAtUtc = DateTime.UtcNow,
                ConfiguredAtPolicyRevision = 1
            });
            _db.SaveChanges();
        }

        // ---- ComputeViolationSignature -------------------------------------------------------------

        [Fact]
        public void Consistent_SoloWorkspace_HasNoViolationSignature()
        {
            var company = SeedCompany("consistent-solo", "owner-1", out var owner);
            var settings = SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            SeedSingleActorRule(company.Id);

            var signature = WorkspacePolicyTransaction.ComputeViolationSignature(_db, company.Id, settings);
            Assert.Null(signature);
        }

        [Fact]
        public void SingleActor_OnUninitializedWorkspace_IsUnauthorizedElevation()
        {
            var company = SeedCompany("elevation", "owner-2", out _);
            var settings = SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            var signature = WorkspacePolicyTransaction.ComputeViolationSignature(_db, company.Id, settings);
            Assert.NotNull(signature);
            Assert.StartsWith("unauthorized_elevation:", signature);
        }

        [Fact]
        public void SoloActive_MissingConfirmedOwner_IsInconsistentTransition()
        {
            var company = SeedCompany("inconsistent", "owner-3", out _);
            var settings = SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, confirmedOwnerMemberId: null, initialized: true);

            var signature = WorkspacePolicyTransaction.ComputeViolationSignature(_db, company.Id, settings);
            Assert.NotNull(signature);
            Assert.StartsWith("inconsistent_transition:", signature);
        }

        // ---- Idempotent reporting + recurrence (no double-count) ----------------------------------

        [Fact]
        public void VerifyUnderLock_CountsOncePerIncident_NotOnRepeatScans()
        {
            var company = SeedCompany("idem", "owner-4", out _);
            SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            // First observation reports and counts once; repeats of the SAME persisted violation do not.
            var o1 = WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);
            var o2 = WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);
            var o3 = WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);

            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NewIncident, o1);
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NoChange, o2);
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NoChange, o3);

            _listener.RecordObservableInstruments();
            Assert.Equal(1, CounterTotal("workspace.unauthorized_elevations.committed"));
        }

        [Fact]
        public void VerifyUnderLock_ConsistentWorkspace_CountsNothing()
        {
            var company = SeedCompany("clean", "owner-5", out var owner);
            SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            SeedSingleActorRule(company.Id);

            var outcome = WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NoChange, outcome);

            _listener.RecordObservableInstruments();
            Assert.Equal(0, CounterTotal("workspace.unauthorized_elevations.committed"));
            Assert.Equal(0, CounterTotal("workspace.inconsistent_transitions.committed"));
        }

        [Fact]
        public void Incident_Resolves_ThenRecurs_CountsAsNewIncident()
        {
            // A violating workspace: first incident counts once.
            var company = SeedCompany("recur", "owner-r", out var owner);
            var settings = SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, confirmedOwnerMemberId: null, initialized: true);
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NewIncident,
                WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id));
            Assert.True(WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id).HasActiveIncident);

            // Fix the violation (assign a valid confirmed owner). Next verify resolves it.
            settings.ConfirmedOwnerMemberId = owner.Id;
            _db.SaveChanges();
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.Resolved,
                WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id));
            Assert.False(WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id).HasActiveIncident);

            // Reintroduce the same violation: it must count again as a NEW incident (not suppressed forever).
            settings.ConfirmedOwnerMemberId = null;
            _db.SaveChanges();
            Assert.Equal(WorkspaceInvariantIncidentLog.ReconcileOutcome.NewIncident,
                WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id));

            _listener.RecordObservableInstruments();
            // Two distinct incidents observed across the lifecycle.
            Assert.Equal(2, CounterTotal("workspace.inconsistent_transitions.committed"));
        }

        [Fact]
        public void CurrentState_IsExposed_SeparatelyFromCounters()
        {
            var company = SeedCompany("state", "owner-s", out _);
            SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            Assert.False(WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id).HasActiveIncident);
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);

            var state = WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id);
            Assert.True(state.HasActiveIncident);
            Assert.StartsWith("unauthorized_elevation:", state.Signature);
        }

        // ---- External-commit verification (controller/signup own the transaction) -----------------

        [Fact]
        public void ExternalCommit_DrainsPending_AndRecordsIncidentOnce()
        {
            // Reproduce the controller/signup pattern: an externally-owned transaction, nested guarded work
            // that leaves a committed violation, an external commit, then the owner drains verification.
            var company = SeedCompany("external", "owner-ext", out _);
            SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            var policyTx = new WorkspacePolicyTransaction(_db, _tenant);

            // The "controller" owns the transaction and opens a completion scope.
            IWorkspacePolicyCompletionToken scope;
            using (var outer = _db.Database.BeginTransaction())
            {
                scope = policyTx.BeginExternalScope();
                // Nested guarded work observes CurrentTransaction != null and defers verification.
                policyTx.Execute(company.Id, _ => { /* mutation already seeded; no-op guarded body */ });
                outer.Commit();
            }

            // Before draining, nothing has been reported.
            Assert.False(WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id).HasActiveIncident);

            // The owner completes its scope as committed — this is the connection the review found missing.
            scope.Committed();

            Assert.True(WorkspaceInvariantIncidentLog.GetCurrentState(_db, company.Id).HasActiveIncident);
            _listener.RecordObservableInstruments();
            Assert.Equal(1, CounterTotal("workspace.unauthorized_elevations.committed"));

            // Completing the same token again is an idempotent no-op and does not double-count.
            scope.Committed();
            _listener.RecordObservableInstruments();
            Assert.Equal(1, CounterTotal("workspace.unauthorized_elevations.committed"));
        }

        [Fact]
        public void ExternalRollback_DiscardsPending_NoReport()
        {
            var company = SeedCompany("external-rb", "owner-rb", out _);
            SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            var policyTx = new WorkspacePolicyTransaction(_db, _tenant);

            IWorkspacePolicyCompletionToken scope;
            using (var outer = _db.Database.BeginTransaction())
            {
                scope = policyTx.BeginExternalScope();
                policyTx.Execute(company.Id, _ => { });
                outer.Rollback();
            }

            // Owner completes its scope as rolled back: nothing is reported.
            scope.RolledBack();

            _listener.RecordObservableInstruments();
            Assert.Equal(0, CounterTotal("workspace.unauthorized_elevations.committed"));
        }

        // ---- Emission timing on commit vs rollback (findings 1 & 2) -------------------------------

        [Fact]
        public void FirstActionTelemetry_EmitsOnlyAfterCommit_NotOnRollback()
        {
            var company = SeedCompany("tele-rb", "owner-t", out var owner);
            SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            SeedSingleActorRule(company.Id);

            var policyTx = new WorkspacePolicyTransaction(_db, _tenant);

            // Controller-owned transaction that enqueues a first-action emission, then ROLLS BACK. The
            // owner completes its scope as rolled back, so the emission never fires.
            IWorkspacePolicyCompletionToken rbScope;
            using (var outer = _db.Database.BeginTransaction())
            {
                rbScope = policyTx.BeginExternalScope();
                policyTx.Execute(company.Id, _ =>
                    policyTx.EnqueuePostCommit(() =>
                        WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                            1.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.JournalPost))));
                outer.Rollback();
            }
            rbScope.RolledBack();
            Assert.Equal(0, HistogramCount("workspace.first_useful_action.duration"));

            // The rolled-back transaction left stale tracked entities in this shared test context; a real
            // request uses a fresh context per call. Clear the tracker to mirror that.
            _db.ChangeTracker.Clear();

            // Committed external transaction: the owner completes its scope as committed and it fires once.
            IWorkspacePolicyCompletionToken cScope;
            using (var outer = _db.Database.BeginTransaction())
            {
                cScope = policyTx.BeginExternalScope();
                policyTx.Execute(company.Id, _ =>
                    policyTx.EnqueuePostCommit(() =>
                        WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                            1.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.JournalPost))));
                outer.Commit();
            }
            cScope.Committed();
            Assert.Equal(1, HistogramCount("workspace.first_useful_action.duration"));
        }

        [Fact]
        public void OwnedExecute_EmitsPostCommitAction_AfterCommit()
        {
            // When Execute OWNS the transaction (no ambient tx, e.g. the journal post path), it drains
            // inline after its own commit: the enqueued emission fires exactly once.
            var company = SeedCompany("tele-owned", "owner-o", out var owner);
            SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            SeedSingleActorRule(company.Id);

            var policyTx = new WorkspacePolicyTransaction(_db, _tenant);
            policyTx.Execute(company.Id, _ =>
                policyTx.EnqueuePostCommit(() =>
                    WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        2.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.JournalPost))));

            Assert.Equal(1, HistogramCount("workspace.first_useful_action.duration"));
        }

        [Fact]
        public void ActiveIncidentGauge_Counts_PersistentCurrentState()
        {
            // Two workspaces with active incidents, one resolved.
            var c1 = SeedCompany("gauge-1", "g1", out _);
            SeedSettings(c1.Id, WorkspaceCollaborationState.SoloActive, confirmedOwnerMemberId: null, initialized: true);
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, c1.Id);

            var c2 = SeedCompany("gauge-2", "g2", out _);
            SeedSettings(c2.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(c2.Id);
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, c2.Id);

            var c3 = SeedCompany("gauge-3", "g3", out var owner3);
            var s3 = SeedSettings(c3.Id, WorkspaceCollaborationState.SoloActive, confirmedOwnerMemberId: null, initialized: true);
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, c3.Id); // active
            s3.ConfirmedOwnerMemberId = owner3.Id;
            _db.SaveChanges();
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, c3.Id); // resolved

            Assert.Equal(2, WorkspaceInvariantIncidentLog.CountWorkspacesWithActiveIncident(_db));
        }

        // ---- Recovery sweep -----------------------------------------------------------------------

        [Fact]
        public void RecoverySweep_DetectsCrashLeftViolation_Once()
        {
            // Two violating workspaces plus one clean workspace.
            var c1 = SeedCompany("sweep-1", "o1", out _);
            SeedSettings(c1.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(c1.Id);

            var c2 = SeedCompany("sweep-2", "o2", out _);
            SeedSettings(c2.Id, WorkspaceCollaborationState.SoloActive, confirmedOwnerMemberId: null, initialized: true);

            var c3 = SeedCompany("sweep-clean", "o3", out var owner3);
            SeedSettings(c3.Id, WorkspaceCollaborationState.SoloActive, owner3.Id, initialized: true);
            SeedSingleActorRule(c3.Id);

            var services = new SingleDbServiceProvider(_db);

            var first = WorkspaceInvariantRecoveryScanner.ScanAsync(services, batchSize: 50, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(3, first.Scanned);
            Assert.Equal(2, first.NewViolations); // c1 + c2 violate; c3 is clean
            Assert.Equal(0, first.ScannerErrors);

            // A second sweep of the same persisted violations must not re-count them.
            var second = WorkspaceInvariantRecoveryScanner.ScanAsync(services, batchSize: 50, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(3, second.Scanned);
            Assert.Equal(0, second.NewViolations);

            _listener.RecordObservableInstruments();
            Assert.Equal(1, CounterTotal("workspace.unauthorized_elevations.committed")); // c1
            Assert.Equal(1, CounterTotal("workspace.inconsistent_transitions.committed")); // c2
        }

        // ---- Durable first-useful-action marker ---------------------------------------------------

        [Fact]
        public void FirstUsefulAction_RecordedOnce_PerWorkspace()
        {
            var company = SeedCompany("measure", "owner-6", out _);
            var repo = new Api.Data.EfRepository<Core.Domain.Auditing.AuditLog>(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Domain.Auditing.AuditLog>.Instance, _db);
            var init = DateTime.UtcNow.AddMinutes(-5);

            // The marker is written and checked inside the posting transaction, which is tenant-scoped to
            // the acting company; mirror that so the repository's tenant filter resolves the row.
            _db.SuppressTenantSecurityCheck = false;
            using var impersonation = _tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner);

            var first = WorkspaceFirstActionEvidence.TryRecordFirstUsefulAction(
                repo, company.Id, WorkspaceOperatingPolicyMetrics.ActionLabels.JournalPost, "owner-6", init, DateTime.UtcNow);
            _db.SaveChanges();
            Assert.True(first);

            // A later action (even a different kind) does not re-establish the first marker.
            var second = WorkspaceFirstActionEvidence.TryRecordFirstUsefulAction(
                repo, company.Id, WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice, "owner-6", init, DateTime.UtcNow);
            Assert.False(second);

            Assert.True(WorkspacePolicyDurableEvidence.HasFirstUsefulAction(_db, company.Id));
            var markerCount = _db.AuditLogs.IgnoreQueryFilters()
                .Count(a => a.CompanyId == company.Id && a.TableName == WorkspaceFirstActionEvidence.FirstUsefulActionTable);
            Assert.Equal(1, markerCount);
        }

        // ---- Immutability of durable evidence (#6) ------------------------------------------------

        [Fact]
        public void DurableMarkers_AreAppendOnly_CannotBeModifiedOrDeleted()
        {
            var company = SeedCompany("immutable", "owner-im", out _);
            SeedSettings(company.Id, WorkspaceCollaborationState.LegacyUnclassified, confirmedOwnerMemberId: null, initialized: false);
            SeedSingleActorRule(company.Id);

            // Produce a first-action marker and an incident row.
            var repo = new Api.Data.EfRepository<Core.Domain.Auditing.AuditLog>(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Domain.Auditing.AuditLog>.Instance, _db);
            using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
            {
                WorkspaceFirstActionEvidence.TryRecordFirstUsefulAction(
                    repo, company.Id, WorkspaceOperatingPolicyMetrics.ActionLabels.JournalPost, "owner-im", DateTime.UtcNow, DateTime.UtcNow);
            }
            WorkspacePolicyTransaction.VerifyCommittedCompanyUnderLock(_db, _tenant, company.Id);

            // Attempt to modify each protected marker row — must be rejected.
            foreach (var table in new[]
            {
                WorkspaceFirstActionEvidence.FirstUsefulActionTable,
                WorkspaceInvariantIncidentLog.IncidentTable
            })
            {
                var row = _db.AuditLogs.IgnoreQueryFilters().First(a => a.CompanyId == company.Id && a.TableName == table);
                row.NewValue = "tampered";
                var ex = Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
                Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
                _db.Entry(row).Reload();
            }
        }

        // ---- Finding 1: rolled-back post-commit work must never drain on a later commit ----------

        [Fact]
        public void RolledBackAction_MustNotEmitOnLaterUnrelatedCommit()
        {
            var company = SeedCompany("review-stale-queue", "review-owner", out var owner);
            SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            var policy = new WorkspacePolicyTransaction(_db, _tenant);
            var repo = new EfRepository<Core.Domain.Auditing.AuditLog>(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Domain.Auditing.AuditLog>.Instance, _db);

            // Enqueue first-action telemetry inside a caller-owned transaction, then roll that transaction
            // back. The emission is bound to the rolled-back transaction.
            using (var outer = _db.Database.BeginTransaction())
            {
                policy.Execute(company.Id, guard =>
                {
                    Assert.True(WorkspaceFirstActionEvidence.TryRecordFirstUsefulAction(repo, company.Id,
                        WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice, "review-owner", DateTime.UtcNow, DateTime.UtcNow));
                    policy.EnqueuePostCommit(() => WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        1.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice)));
                });
                outer.Rollback();
            }

            // Exactly the cleanup TryPostSalesInvoice performed BEFORE this fix: clear EF tracking but do
            // NOT discard the policy queue. The transaction-binding must still prevent the stale emission.
            _db.ChangeTracker.Clear();

            // A later, unrelated guarded commit owns a fresh transaction. It must drain only its OWN work.
            policy.Execute(company.Id, guard => { });

            Assert.Equal(0, _db.AuditLogs.IgnoreQueryFilters().Count(a => a.CompanyId == company.Id
                && a.TableName == WorkspaceFirstActionEvidence.FirstUsefulActionTable));
            Assert.Equal(0, HistogramCount("workspace.first_useful_action.duration"));
        }

        [Fact]
        public void RolledBackAction_MustNotEmitOnLaterExternalCommit()
        {
            // The reviewer's reproduction: a rolled-back scope A's work must NOT emit when an UNRELATED
            // external transaction B later commits and its owner completes B's scope. Completion is keyed to
            // the specific scope, so B's commit drains only B.
            var company = SeedCompany("review-stale-ext", "review-owner", out var owner);
            SeedSettings(company.Id, WorkspaceCollaborationState.SoloActive, owner.Id, initialized: true);
            var policy = new WorkspacePolicyTransaction(_db, _tenant);
            var repo = new EfRepository<Core.Domain.Auditing.AuditLog>(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Domain.Auditing.AuditLog>.Instance, _db);

            // 1-2. Enqueue under transaction A, roll A back and dispose it, clear tracking — WITHOUT
            //      completing A's scope (mirrors a rollback path that only cleared EF tracking).
            using (var outer = _db.Database.BeginTransaction())
            {
                var scopeA = policy.BeginExternalScope();
                policy.Execute(company.Id, guard =>
                {
                    Assert.True(WorkspaceFirstActionEvidence.TryRecordFirstUsefulAction(repo, company.Id,
                        WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice, "review-owner", DateTime.UtcNow, DateTime.UtcNow));
                    policy.EnqueuePostCommit(() => WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        1.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice)));
                });
                outer.Rollback();
                _ = scopeA; // deliberately not completed: A was abandoned
            }
            _db.ChangeTracker.Clear();

            // 3-4. An UNRELATED external transaction B commits; its owner completes B's scope. Because
            //      completion is per-scope, A's work is never drained by B's commit.
            using (var b = _db.Database.BeginTransaction())
            {
                var scopeB = policy.BeginExternalScope();
                b.Commit();
                b.Dispose();
                scopeB.Committed();
            }

            Assert.Equal(0, _db.AuditLogs.IgnoreQueryFilters().Count(a => a.CompanyId == company.Id
                && a.TableName == WorkspaceFirstActionEvidence.FirstUsefulActionTable));
            Assert.Equal(0, HistogramCount("workspace.first_useful_action.duration"));
        }

        [Fact]
        public void PostCommitWork_FromOneTransaction_IsNotDrainedByAnother()
        {
            var a = SeedCompany("tx-a", "owner-a", out var ownerA);
            SeedSettings(a.Id, WorkspaceCollaborationState.SoloActive, ownerA.Id, initialized: true);
            var b = SeedCompany("tx-b", "owner-b", out var ownerB);
            SeedSettings(b.Id, WorkspaceCollaborationState.SoloActive, ownerB.Id, initialized: true);
            var policy = new WorkspacePolicyTransaction(_db, _tenant);

            // Company A: enqueue under a caller-owned transaction that ROLLS BACK; scope A is not completed.
            using (var txA = _db.Database.BeginTransaction())
            {
                var scopeA = policy.BeginExternalScope();
                policy.Execute(a.Id, guard =>
                    policy.EnqueuePostCommit(() => WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        1.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice))));
                txA.Rollback();
                _ = scopeA;
            }
            _db.ChangeTracker.Clear();

            // Company B: a separate caller-owned transaction that COMMITS and completes ITS scope. It must
            // emit exactly once (for B), never picking up A's rolled-back emission.
            using (var txB = _db.Database.BeginTransaction())
            {
                var scopeB = policy.BeginExternalScope();
                policy.Execute(b.Id, guard =>
                    policy.EnqueuePostCommit(() => WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        2.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice))));
                txB.Commit();
                txB.Dispose();
                scopeB.Committed();
            }

            Assert.Equal(1, HistogramCount("workspace.first_useful_action.duration"));
        }

        [Fact]
        public void PruneScopes_ReleasesAbandonedScopePendingWork_WithoutEmission()
        {
            // Focused regression for the PruneScopes cleanup: removing an abandoned scope's registration
            // must ALSO release its queued pending work, without emitting it. The release here is driven by
            // the explicit completion path (Committed() -> DrainScopeCommitted -> PruneScopes) of a DIFFERENT
            // scope — NOT by EvictOrphanedPending — so it isolates the PruneScopes behavior. Before the fix
            // PruneScopes trimmed only _scopes, leaving the abandoned callback stranded in _pending forever
            // (never drained, never emitted, never collected). After the fix the abandoned item is released
            // on that PruneScopes pass and still never emits, because the abandoned transaction rolled back.
            var orphan = SeedCompany("prune-orphan", "orphan-owner", out var orphanOwner);
            SeedSettings(orphan.Id, WorkspaceCollaborationState.SoloActive, orphanOwner.Id, initialized: true);
            var policy = new WorkspacePolicyTransaction(_db, _tenant);

            // Abandon a scope: enqueue a post-commit callback under a caller-owned transaction, roll it back
            // and dispose it, and NEVER complete the scope (mirrors a rollback path that only cleared EF
            // tracking). Execute() enqueues its own deferred committed-state verification plus our explicit
            // post-commit action, so two items are bound to the abandoned scope.
            using (var abandoned = _db.Database.BeginTransaction())
            {
                policy.BeginExternalScope();
                policy.Execute(orphan.Id, guard =>
                    policy.EnqueuePostCommit(() => WorkspaceOperatingPolicyMetrics.FirstUsefulActionDuration.Record(
                        9.0, new KeyValuePair<string, object?>("action", WorkspaceOperatingPolicyMetrics.ActionLabels.SalesInvoice))));
                abandoned.Rollback();
                abandoned.Dispose();
                // deliberately abandoned: neither Committed() nor RolledBack() on its token
            }
            _db.ChangeTracker.Clear();

            // The abandoned scope's pending work is still queued: nothing has run that would release it.
            Assert.Equal(2, policy.PendingItemCount);
            Assert.Equal(1, policy.ScopeCount);

            // Open and COMMIT an unrelated transaction, then complete ONLY its scope via the token. This
            // drives DrainScopeCommitted -> PruneScopes directly, with no intervening Execute/EnqueuePostCommit
            // (so EvictOrphanedPending is not what cleans up). PruneScopes must now also drop the abandoned
            // scope's two pending items, while the abandoned callback never emits.
            using (var liveTx = _db.Database.BeginTransaction())
            {
                var liveScope = policy.BeginExternalScope();
                liveTx.Commit();
                liveTx.Dispose();
                liveScope.Committed();
            }

            // Decisive assertions: the abandoned scope registration and BOTH of its stranded pending items
            // are gone (before the fix PendingItemCount would still be 2), and nothing was emitted — the
            // abandoned transaction rolled back, so its post-commit side effect must never run.
            Assert.Equal(0, policy.PendingItemCount);
            Assert.Equal(0, policy.ScopeCount);
            Assert.Equal(0, HistogramCount("workspace.first_useful_action.duration"));
        }

        // ---- Finding 2: monitoring must not report a healthy zero on a failed measurement --------

        [Fact]
        public void ActiveIncidentGauge_ReportsUnavailable_NotZero_OnQueryFailure()
        {
            // An observer that fails (as a dropped DB connection would) must publish -1 (unavailable) and
            // bump the failure counter — never a zero that reads as "no active incidents".
            WorkspaceOperatingPolicyMetrics.RegisterActiveIncidentObserver(() => throw new InvalidOperationException("db down"));

            var failuresBefore = CounterTotal("workspace.active_invariant_incidents.query_failures");
            _listener.RecordObservableInstruments();

            var gauge = LastObservedGauge("workspace.active_invariant_incidents");
            Assert.True(gauge.HasValue, "gauge was observed");
            Assert.Equal(-1, gauge!.Value);
            Assert.True(CounterTotal("workspace.active_invariant_incidents.query_failures") > failuresBefore);
        }

        [Fact]
        public void ActiveIncidentGauge_HealthyZero_OnlyFromSuccessfulQuery()
        {
            // A successful observer returning zero publishes a genuine healthy zero and advances freshness.
            WorkspaceOperatingPolicyMetrics.RegisterActiveIncidentObserver(() => 0);

            _listener.RecordObservableInstruments();
            var gauge = LastObservedGauge("workspace.active_invariant_incidents");
            Assert.True(gauge.HasValue);
            Assert.Equal(0, gauge!.Value);

            var age = LastObservedGaugeDouble("workspace.active_invariant_incidents.last_success_age_seconds");
            Assert.True(age.HasValue);
            Assert.True(age!.Value >= 0, "freshness age is non-negative after a successful observation");
        }

        private long? LastObservedGauge(string instrumentName)
        {
            lock (_counters)
            {
                var hits = _counters.Where(c => c.Name == instrumentName).ToList();
                return hits.Count == 0 ? (long?)null : hits[^1].Value;
            }
        }

        private double? LastObservedGaugeDouble(string instrumentName)
        {
            lock (_histograms)
            {
                var hits = _histograms.Where(h => h.Name == instrumentName).ToList();
                return hits.Count == 0 ? (double?)null : hits[^1].Value;
            }
        }

        /// <summary>Minimal IServiceProvider that hands out the one test DbContext per scope.</summary>
        private sealed class SingleDbServiceProvider : IServiceProvider, IServiceScopeFactory, Microsoft.Extensions.DependencyInjection.IServiceScope
        {
            private readonly ApiDbContext _db;
            public SingleDbServiceProvider(ApiDbContext db) => _db = db;
            public IServiceProvider ServiceProvider => this;
            public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => this;
            public void Dispose() { }
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(ApiDbContext)) return _db;
                if (serviceType == typeof(IServiceScopeFactory)) return this;
                return null;
            }
        }
    }

    [CollectionDefinition("WorkspacePolicyMeter", DisableParallelization = true)]
    public sealed class WorkspacePolicyMeterCollection { }
}
