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
    [Collection("WorkspacePolicyMeter")]
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
            var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = true, LegacySoloConfirmationEnabled = true });
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
            var options = Options.Create(new WorkspaceExperienceOptions { SoloSetupEnabled = true, LegacySoloConfirmationEnabled = true });
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

        [Fact]
        public void InvariantDetection_DetectsInconsistentTransition_WhenSoloActiveWithMultipleActiveMembers()
        {
            var company = CreateTestCompany("Multi Solo Co", "multi-solo-co", "owner-user-ms");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            var member2 = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = "second-user-ms",
                Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            _db.CompanyMembers.Add(member2);
            _db.SaveChanges();

            // Introduce illegal state: SoloActive while having 2 active members
            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.JustMe;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;
                return true;
            });

            lock (_counterMeasurements)
            {
                var violation = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.NotNull(violation.InstrumentName);
                Assert.Equal(1, violation.Value);
            }
        }

        [Fact]
        public void ValidSingleActorPolicy_DoesNotEmitViolations()
        {
            var company = CreateTestCompany("Valid Solo Co", "valid-solo-co", "user-valid");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.JustMe;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;
                guard.Settings.EverTeamActivatedAtUtc = null;

                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void LegitimateState_InvitationAcceptance_PreservesSingleActorRule_WithoutViolationAlerts()
        {
            var company = CreateTestCompany("Team Accepted Co", "team-accept-co", "owner-user");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            // Step 1: Initialize solo with SingleActorAllowed rule
            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.JustMe;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;

                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            // Step 2: Second human accepts invitation -> transitions to TeamActive with historical marker
            tx.Execute(company.Id, guard =>
            {
                var member2 = new CompanyMember
                {
                    CompanyId = company.Id,
                    UserId = "second-user",
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                };
                _db.CompanyMembers.Add(member2);

                guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                guard.Settings.EverTeamActivatedAtUtc = DateTime.UtcNow;
                return true;
            });

            // Step 3: Verify SingleActorAllowed rule with TeamActive and historical collaboration is NOT a violation
            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void LegitimateState_ReadOnlySecondMember_PreservesSingleActorRule_WithoutViolationAlerts()
        {
            var company = CreateTestCompany("ReadOnly Collab Co", "readonly-collab-co", "owner-user-ro");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;
                guard.Settings.EverTeamActivatedAtUtc = DateTime.UtcNow;

                // Add read-only second member
                var readOnlyAccountant = new CompanyMember
                {
                    CompanyId = company.Id,
                    UserId = "readonly-accountant",
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                };
                _db.CompanyMembers.Add(readOnlyAccountant);

                // Preserve SingleActorAllowed rule for owner's journal posting
                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    Provenance = WorkspaceApprovalRuleProvenance.ExplicitPolicyChange,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void LegitimateState_MemberRemoval_PreservesSingleActorRule_WithoutViolationAlerts()
        {
            var company = CreateTestCompany("Member Removal Co", "member-remove-co", "owner-user-mr");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            var departedMember = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = "departed-user",
                Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow.AddDays(-30)
            };
            _db.CompanyMembers.Add(departedMember);
            _db.SaveChanges();

            // Setup team with SingleActorAllowed rule
            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow.AddDays(-30);
                guard.Settings.EverTeamActivatedAtUtc = DateTime.UtcNow.AddDays(-30);

                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    Provenance = WorkspaceApprovalRuleProvenance.ExplicitPolicyChange,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            // Member departs -> removed/suspended
            tx.Execute(company.Id, guard =>
            {
                departedMember.Status = CompanyMemberStatus.Suspended;
                return true;
            });

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void LegitimateState_OwnershipTransfer_PreservesSingleActorRule_WithoutViolationAlerts()
        {
            var company = CreateTestCompany("Transfer Co", "transfer-co", "original-owner");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var originalOwner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            var newOwner = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = "new-owner-user",
                Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            _db.CompanyMembers.Add(newOwner);
            _db.SaveChanges();

            // Initialize workspace
            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                guard.Settings.ConfirmedOwnerMemberId = originalOwner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;
                guard.Settings.EverTeamActivatedAtUtc = DateTime.UtcNow;

                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                    Provenance = WorkspaceApprovalRuleProvenance.ExplicitPolicyChange,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            // Transfer ownership to new owner
            tx.Execute(company.Id, guard =>
            {
                originalOwner.Role = CompanyMemberRole.Admin;
                newOwner.Role = CompanyMemberRole.Owner;
                guard.Settings.ConfirmedOwnerMemberId = newOwner.Id;
                return true;
            });

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void LegitimateState_ExplicitIndependentReviewPolicy_EmitsNoViolations()
        {
            var company = CreateTestCompany("Independent Review Co", "ind-review-co", "owner-user-ir");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;
            var owner = _db.CompanyMembers.First(m => m.CompanyId == company.Id);

            tx.Execute(company.Id, guard =>
            {
                guard.Settings.Purpose = WorkspacePurpose.Business;
                guard.Settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                guard.Settings.ConfirmedOwnerMemberId = owner.Id;
                guard.Settings.InitializedAtUtc = DateTime.UtcNow;

                _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                {
                    CompanyId = company.Id,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                    RuleKind = WorkspaceApprovalRuleKind.IndependentReviewerRequired,
                    Provenance = WorkspaceApprovalRuleProvenance.ExplicitPolicyChange,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
                return true;
            });

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);

                var inconsistent = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.inconsistent_transitions.committed");
                Assert.Null(inconsistent.InstrumentName);
            }
        }

        [Fact]
        public void ActualViolation_Rollback_DoesNotEmitCommittedViolation()
        {
            var company = CreateTestCompany("Rollback Co", "rollback-co", "user-rollback");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;

            // Attempt an unauthorized elevation but fail/throw before commit
            Assert.Throws<InvalidOperationException>(() =>
            {
                tx.Execute<bool>(company.Id, guard =>
                {
                    _db.WorkspaceApprovalRules.Add(new WorkspaceApprovalRule
                    {
                        CompanyId = company.Id,
                        OperationKey = AccessOperationCatalog.FinancialsJournalPost,
                        RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed,
                        PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                    });
                    throw new InvalidOperationException("Simulated business validation failure triggering rollback");
                });
            });

            // Ensure rule was rolled back and violation metric was NOT emitted
            var ruleExists = _db.WorkspaceApprovalRules.Any(r => r.CompanyId == company.Id);
            Assert.False(ruleExists);

            lock (_counterMeasurements)
            {
                var unauth = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.unauthorized_elevations.committed");
                Assert.Null(unauth.InstrumentName);
            }
        }

        [Fact]
        public void DetectorFailure_DoesNotFailCommittedTransaction()
        {
            var company = CreateTestCompany("Fault Co", "fault-co", "user-fault");
            var tx = new WorkspacePolicyTransaction(_db, _tenant);

            _db.SuppressTenantSecurityCheck = true;

            try
            {
                // Inject fault into post-commit verification
                WorkspacePolicyTestGate.PostCommitVerificationFault = (cid) =>
                {
                    throw new InvalidOperationException("Simulated detector fault or transient query timeout");
                };

                // The financial transaction must succeed and return the result even if post-commit detector throws
                var result = tx.Execute(company.Id, guard =>
                {
                    guard.Settings.Purpose = WorkspacePurpose.Business;
                    return "FinancialCommitSucceeded";
                });

                Assert.Equal("FinancialCommitSucceeded", result);

                // Detector exception metric was recorded
                lock (_counterMeasurements)
                {
                    var detectorException = _counterMeasurements.FirstOrDefault(m => m.InstrumentName == "workspace.policy.detector_exceptions");
                    Assert.NotNull(detectorException.InstrumentName);
                    Assert.Equal(1, detectorException.Value);
                }
            }
            finally
            {
                WorkspacePolicyTestGate.Reset();
            }
        }
    }
}
