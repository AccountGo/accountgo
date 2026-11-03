// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Api.Data;
using Api.Options;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Services.Security;
using Xunit;

namespace AccountGo.BackendTests.Observability
{
    public class WorkspacePolicyTelemetryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ApiDbContext _db;
        private readonly TenantContext _tenant = new();
        private readonly MeterListener _listener;
        private readonly List<(string InstrumentName, long Value, Dictionary<string, object?> Tags)> _counterMeasurements = new();
        private readonly List<(string InstrumentName, double Value, Dictionary<string, object?> Tags)> _histogramMeasurements = new();

        public WorkspacePolicyTelemetryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
            _db.Database.EnsureCreated();

            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == WorkspaceOperatingPolicyMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                var tagDict = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    tagDict[tag.Key] = tag.Value;
                }
                lock (_counterMeasurements)
                {
                    _counterMeasurements.Add((instrument.Name, measurement, tagDict));
                }
            });
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                var tagDict = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    tagDict[tag.Key] = tag.Value;
                }
                lock (_histogramMeasurements)
                {
                    _histogramMeasurements.Add((instrument.Name, measurement, tagDict));
                }
            });
            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
            _db.Dispose();
            _connection.Dispose();
        }

        private Company CreateTestCompany(string name, string slug, string ownerUserId)
        {
            _db.SuppressTenantSecurityCheck = true;
            var company = new Company
            {
                Name = name,
                Slug = slug,
                CreatedByUserId = ownerUserId
            };
            _db.Companies.Add(company);
            _db.SaveChanges();

            var owner = new CompanyMember
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

        [Fact]
        public void OnboardingStarts_IsEmitted_WhenEvaluatingEligibility()
        {
            var company = CreateTestCompany("Test Co", "test-co-1", "user-1");
            var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = true });
            var catalog = new WorkspaceAccessProfileCatalog(_db, Mock.Of<ISecurityService>(), Array.Empty<IWorkspaceAccessProfilePermissionContributor>());
            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var tx = new WorkspacePolicyTransaction(_db, _tenant);
            var service = new WorkspaceOperatingPolicyService(_db, resolver, catalog, tx, options);

            service.EvaluateLegacySoloEligibility(company.Id, "user-1");

            _listener.RecordObservableInstruments();

            lock (_counterMeasurements)
            {
                var startEvent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.onboarding.starts");
                Assert.NotNull(startEvent.InstrumentName);
                Assert.Equal(1, startEvent.Value);
                Assert.Equal(WorkspaceOperatingPolicyMetrics.FlowLabels.LegacySolo, startEvent.Tags["flow"]);
                // Cardinality verification: no PII or identifiers in tags
                Assert.False(startEvent.Tags.ContainsKey("companyId"));
                Assert.False(startEvent.Tags.ContainsKey("userId"));
            }
        }

        [Fact]
        public void StaleRevision_EmitsConflictMetric_AndDoesNotCorruptState()
        {
            var company = CreateTestCompany("Test Co 2", "test-co-2", "user-2");
            var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = true });
            var catalog = new WorkspaceAccessProfileCatalog(_db, Mock.Of<ISecurityService>(), Array.Empty<IWorkspaceAccessProfilePermissionContributor>());
            var resolver = new WorkspaceApprovalPolicyResolver(_db);
            var tx = new WorkspacePolicyTransaction(_db, _tenant);
            var service = new WorkspaceOperatingPolicyService(_db, resolver, catalog, tx, options);

            // Attempt confirm with stale revision 999 instead of 0
            var result = service.ConfirmLegacySoloSetup(company.Id, "user-2", expectedRevision: 999);
            Assert.False(result.Applied);
            Assert.Equal(WorkspacePolicyReasonCodes.StalePolicyRevision, result.ReasonCode);

            lock (_counterMeasurements)
            {
                var conflictEvent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.policy.conflicts");
                Assert.NotNull(conflictEvent.InstrumentName);
                Assert.Equal(1, conflictEvent.Value);
                Assert.Equal(WorkspaceOperatingPolicyMetrics.ConflictLabels.StaleRevision, conflictEvent.Tags["conflict_type"]);

                var completionEvent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.onboarding.completions" &&
                                                                               Equals(m.Tags.GetValueOrDefault("status"), WorkspaceOperatingPolicyMetrics.StatusLabels.Conflict));
                Assert.NotNull(completionEvent.InstrumentName);
            }
        }

        [Fact]
        public void Authorizations_Emitted_WithBoundedOperationAndModeTags()
        {
            var company = CreateTestCompany("Test Co 3", "test-co-3", "user-3");
            var resolver = new WorkspaceApprovalPolicyResolver(_db);

            var rule = resolver.Resolve(company.Id, AccessOperationCatalog.FinancialsJournalPost);
            Assert.Equal(WorkspaceApprovalMode.CatalogDefault, rule.Mode);

            lock (_counterMeasurements)
            {
                var authEvent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.policy.authorizations");
                Assert.NotNull(authEvent.InstrumentName);
                Assert.Equal(1, authEvent.Value);
                Assert.Equal(AccessOperationCatalog.FinancialsJournalPost, authEvent.Tags["operation"]);
                Assert.Equal("catalog_default", authEvent.Tags["mode"]);
                Assert.False(authEvent.Tags.ContainsKey("companyId"));
            }
        }

        [Fact]
        public void InvariantDetection_DetectsUnauthorizedElevation_WhenCommitted()
        {
            var company = CreateTestCompany("Test Co 4", "test-co-4", "user-4");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;

            // Introduce an illegal state inside policy transaction: SingleActorAllowed rule on unclassified legacy workspace
            tx.Execute(company.Id, guard =>
            {
                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            lock (_counterMeasurements)
            {
                var violation = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.NotNull(violation.InstrumentName);
                Assert.Equal(1, violation.Value);
            }
        }

        [Fact]
        public void InvariantDetection_DetectsInconsistentTransition_WhenCommitted()
        {
            var company = CreateTestCompany("Test Co 5", "test-co-5", "user-5");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;

            // Introduce an illegal state inside policy transaction: SoloActive without ConfirmedOwnerMemberId
            tx.Execute(company.Id, guard =>
            {
                guard.Settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                guard.Settings.ConfirmedOwnerMemberId = null;
                return true;
            });

            lock (_counterMeasurements)
            {
                var violation = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.NotNull(violation.InstrumentName);
                Assert.Equal(1, violation.Value);
            }
        }
    }
}
