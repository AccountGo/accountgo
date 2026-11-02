// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Options;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Service
{
    public sealed class WorkspaceInitializationRequest
    {
        public WorkspacePurpose Purpose { get; init; }
        public WorkspaceCollaborationIntent CollaborationIntent { get; init; }
        public OwnerAccessPreset OwnerAccessPreset { get; init; } = OwnerAccessPreset.FullWorkspaceAccess;
        public WorkspaceApprovalRuleKind? InitialJournalWorkflow { get; init; }
    }

    public sealed class WorkspaceInitializationResult
    {
        public bool Applied { get; init; }
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
        public long PolicyRevision { get; init; }
        public string? AccessProfileKey { get; init; }
        public WorkspaceApprovalMode JournalMode { get; init; }
    }

    /// <summary>
    /// Applies explicit About/setup choices inside an existing company-creation or confirmation transaction.
    /// Caller must already hold the company create/membership transaction; this uses the policy guard.
    /// </summary>
    public sealed class WorkspaceInitializationApplicator
    {
        private readonly ApiDbContext _db;
        private readonly IWorkspacePolicyTransaction _policyTransaction;
        private readonly IWorkspaceAccessProfileCatalog _accessCatalog;
        private readonly IWorkspaceApprovalPolicyResolver _approvalResolver;
        private readonly WorkspaceExperienceOptions _experience;

        public WorkspaceInitializationApplicator(
            ApiDbContext db,
            IWorkspacePolicyTransaction policyTransaction,
            IWorkspaceAccessProfileCatalog accessCatalog,
            IWorkspaceApprovalPolicyResolver approvalResolver,
            IOptions<WorkspaceExperienceOptions> experienceOptions)
        {
            _db = db;
            _policyTransaction = policyTransaction;
            _accessCatalog = accessCatalog;
            _approvalResolver = approvalResolver;
            _experience = WorkspaceExperienceOptions.FromEnvironment(experienceOptions.Value);
        }

        public WorkspaceExperienceOptions Experience => _experience;

        public WorkspaceInitializationResult ApplyNewWorkspace(
            int companyId,
            string ownerUserId,
            int ownerMemberId,
            WorkspaceInitializationRequest request)
        {
            if (!_experience.SoloSetupEnabled)
            {
                if (request.CollaborationIntent == WorkspaceCollaborationIntent.JustMe)
                {
                    return _policyTransaction.Execute(companyId, guard =>
                    {
                        var settings = guard.Settings;
                        if (settings.InitializedAtUtc.HasValue)
                        {
                            return new WorkspaceInitializationResult
                            {
                                Applied = false,
                                ReasonCode = WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                                Message = "Workspace is already initialized.",
                                PolicyRevision = settings.PolicyRevision,
                                JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                            };
                        }

                        return new WorkspaceInitializationResult
                        {
                            Applied = false,
                            ReasonCode = WorkspacePolicyReasonCodes.SoloSetupDisabled,
                            Message = "Solo/team workspace initialization is not enabled; workspace remains unclassified.",
                            JournalMode = WorkspaceApprovalMode.CatalogDefault
                        };
                    });
                }

                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = WorkspacePolicyReasonCodes.SoloSetupDisabled,
                    Message = "Solo/team workspace initialization is not enabled; workspace remains unclassified.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            if (request.Purpose == WorkspacePurpose.Unspecified
                || request.CollaborationIntent == WorkspaceCollaborationIntent.Unspecified)
            {
                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = "about_answers_required",
                    Message = "Purpose and collaboration answers are required for the new workspace experience.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            if (request.Purpose == WorkspacePurpose.Personal && !_experience.PersonalPurposeEnabled)
            {
                return new WorkspaceInitializationResult
                {
                    Applied = false,
                    ReasonCode = WorkspacePolicyReasonCodes.PersonalPurposeDisabled,
                    Message = "Personal workspace purpose is not available until the personal equity mapping is approved.",
                    JournalMode = WorkspaceApprovalMode.CatalogDefault
                };
            }

            return _policyTransaction.Execute(companyId, guard =>
            {
                var settings = guard.Settings;
                if (settings.InitializedAtUtc.HasValue)
                {
                    return new WorkspaceInitializationResult
                    {
                        Applied = false,
                        ReasonCode = WorkspacePolicyReasonCodes.SoloSetupNotEligible,
                        Message = "Workspace is already initialized.",
                        PolicyRevision = settings.PolicyRevision,
                        JournalMode = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost).Mode
                    };
                }

                settings.Purpose = request.Purpose;
                settings.CollaborationIntent = request.CollaborationIntent;
                settings.ConfirmedOwnerMemberId = ownerMemberId;
                settings.InitializedAtUtc = DateTime.UtcNow;
                settings.InitializedByUserId = ownerUserId;
                settings.InitializationSource = WorkspaceInitializationSource.NewWorkspace;
                settings.LastConfirmedAtUtc = settings.InitializedAtUtc;
                settings.LastConfirmedByUserId = ownerUserId;

                string? profileKey = null;
                if (request.CollaborationIntent == WorkspaceCollaborationIntent.JustMe)
                {
                    settings.CollaborationState = WorkspaceCollaborationState.SoloActive;
                    AssignFullAccess(ownerMemberId, ownerUserId);
                    profileKey = _accessCatalog.FullAccessProfileKey;
                    SetJournalRule(companyId, WorkspaceApprovalRuleKind.SingleActorAllowed, ownerUserId, settings.PolicyRevision);
                }
                else
                {
                    settings.CollaborationState = WorkspaceCollaborationState.TeamSetupPending;
                    if (request.OwnerAccessPreset == OwnerAccessPreset.FullWorkspaceAccess)
                    {
                        AssignFullAccess(ownerMemberId, ownerUserId);
                        profileKey = _accessCatalog.FullAccessProfileKey;
                    }

                    var journalKind = request.InitialJournalWorkflow ?? WorkspaceApprovalRuleKind.IndependentReviewerRequired;
                    // Independent review only becomes usable once an eligible reviewer accepts; still persist the chosen rule.
                    SetJournalRule(companyId, journalKind, ownerUserId, settings.PolicyRevision);
                }

                var company = _db.Companies.IgnoreQueryFilters().Single(c => c.Id == companyId);
                company.AccessBootstrapAvailable = false;

                var previousRevision = settings.PolicyRevision;
                guard.AdvanceRevision();

                var auditPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    eventKind = "WorkspaceInitialization",
                    companyId,
                    purpose = settings.Purpose.ToString(),
                    collaborationIntent = settings.CollaborationIntent.ToString(),
                    collaborationState = settings.CollaborationState.ToString(),
                    confirmedOwnerMemberId = ownerMemberId,
                    initializedByUserId = ownerUserId,
                    initializationSource = settings.InitializationSource.ToString(),
                    accessProfileKey = profileKey,
                    previousPolicyRevision = previousRevision,
                    policyRevision = settings.PolicyRevision
                });

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = ownerUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                    TableName = "__WorkspaceInitialization",
                    RecordId = companyId.ToString(),
                    NewValue = auditPayload
                });

                var journal = _approvalResolver.Resolve(settings, FindJournalRule(companyId), AccessOperationCatalog.FinancialsJournalPost);
                return new WorkspaceInitializationResult
                {
                    Applied = true,
                    PolicyRevision = settings.PolicyRevision,
                    AccessProfileKey = profileKey,
                    JournalMode = journal.Mode
                };
            });
        }

        private void AssignFullAccess(int ownerMemberId, string grantedByUserId)
        {
            _accessCatalog.EnsureSeeded();
            var role = _accessCatalog.FindFullAccessRole()
                ?? throw new InvalidOperationException("Managed Full workspace access profile is not seeded.");

            var existing = _db.CompanyMemberSecurityRoles
                .IgnoreQueryFilters()
                .FirstOrDefault(r => r.CompanyMemberId == ownerMemberId && r.SecurityRoleId == role.Id);
            if (existing != null)
                return;

            _db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
            {
                CompanyMemberId = ownerMemberId,
                SecurityRoleId = role.Id,
                GrantOrigin = WorkspaceMemberGrantOrigin.SoloInitialization,
                GrantedAtUtc = DateTime.UtcNow,
                GrantedByUserId = grantedByUserId,
                AccessProfileVersion = _accessCatalog.FullAccessProfileVersion
            });
        }

        private WorkspaceApprovalRule? FindJournalRule(int companyId) =>
            _db.WorkspaceApprovalRules.IgnoreQueryFilters()
                .SingleOrDefault(r => r.CompanyId == companyId && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);

        private void SetJournalRule(int companyId, WorkspaceApprovalRuleKind kind, string userId, long revision)
        {
            var rule = FindJournalRule(companyId);
            if (rule == null)
            {
                rule = new WorkspaceApprovalRule
                {
                    CompanyId = companyId,
                    OperationKey = AccessOperationCatalog.FinancialsJournalPost
                };
                _db.WorkspaceApprovalRules.Add(rule);
            }

            rule.RuleKind = kind;
            rule.Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed;
            rule.ConfiguredAtUtc = DateTime.UtcNow;
            rule.ConfiguredByUserId = userId;
            rule.ConfiguredAtPolicyRevision = revision;
            rule.PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion;
        }
    }
}
