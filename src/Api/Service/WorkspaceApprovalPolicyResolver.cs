// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed class WorkspaceApprovalPolicyResolver : IWorkspaceApprovalPolicyResolver
    {
        private readonly ApiDbContext _db;

        public WorkspaceApprovalPolicyResolver(ApiDbContext db)
        {
            _db = db;
        }

        public EffectiveApprovalRule Resolve(int companyId, string operationKey)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
                throw new ArgumentException("Operation key is required.", nameof(operationKey));

            // Bypass tenant filter: resolver may run inside impersonation or during backfill.
            var settings = _db.WorkspaceOperatingSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefault(s => s.CompanyId == companyId);

            var rule = _db.WorkspaceApprovalRules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefault(r => r.CompanyId == companyId && r.OperationKey == operationKey);

            return Resolve(settings ?? new WorkspaceOperatingSettings { CompanyId = companyId, PolicyRevision = 0 }, rule, operationKey);
        }

        public EffectiveApprovalRule Resolve(WorkspaceOperatingSettings settings, WorkspaceApprovalRule? rule, string operationKey)
        {
            if (rule == null)
            {
                return new EffectiveApprovalRule(
                    operationKey,
                    WorkspaceApprovalMode.CatalogDefault,
                    null,
                    settings.PolicyRevision,
                    null);
            }

            if (settings.PolicySchemaVersion != WorkspaceOperatingPolicySchema.CurrentVersion
                || rule.PolicySchemaVersion != WorkspaceOperatingPolicySchema.CurrentVersion)
            {
                return new EffectiveApprovalRule(
                    operationKey,
                    WorkspaceApprovalMode.IndependentReviewerRequired,
                    rule.Provenance,
                    settings.PolicyRevision,
                    rule.ReviewerSecurityRoleId);
            }

            var mode = rule.RuleKind switch
            {
                WorkspaceApprovalRuleKind.SingleActorAllowed => WorkspaceApprovalMode.SingleActorAllowed,
                _ => WorkspaceApprovalMode.IndependentReviewerRequired
            };

            return new EffectiveApprovalRule(
                operationKey,
                mode,
                rule.Provenance,
                settings.PolicyRevision,
                rule.ReviewerSecurityRoleId);
        }
    }
}
