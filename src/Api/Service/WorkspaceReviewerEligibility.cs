// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed class WorkspaceReviewerEligibility : IWorkspaceReviewerEligibility
    {
        private const string SystemActorPrefix = "system:";

        private readonly ApiDbContext _db;
        private readonly IEntitlementService? _entitlements;

        public WorkspaceReviewerEligibility(
            ApiDbContext db,
            IEntitlementService? entitlements = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _entitlements = entitlements;
        }

        public IReadOnlyList<EligibleJournalReviewer> GetEligibleReviewers(int companyId, string? excludeUserId = null)
        {
            var reviewers = EvaluateCapacity(companyId).EligibleReviewers;
            if (string.IsNullOrWhiteSpace(excludeUserId))
                return reviewers;
            return reviewers
                .Where(r => !string.Equals(r.UserId, excludeUserId, StringComparison.Ordinal))
                .ToList();
        }

        public IndependentReviewCapacity EvaluateCapacity(int companyId, MemberReviewerChange? change = null) =>
            EvaluateCapacity(companyId, change, rolePermissionOverride: null);

        public ReviewerGuardResult EvaluateMemberChange(int companyId, MemberReviewerChange change)
        {
            if (!RequiresIndependentReview(companyId))
                return ReviewerGuardResult.Allowed;

            var before = EvaluateCapacity(companyId);
            if (!before.IsUsable)
                return ReviewerGuardResult.Allowed;

            var after = EvaluateCapacity(companyId, change);
            if (after.IsUsable)
                return ReviewerGuardResult.Allowed;

            return LastReviewerBlocked();
        }

        public ReviewerGuardResult EvaluateRolePermissionChange(
            int companyId,
            int securityRoleId,
            IReadOnlyCollection<string> newPermissionNamesForRole)
        {
            if (!RequiresIndependentReview(companyId))
                return ReviewerGuardResult.Allowed;

            var before = EvaluateCapacity(companyId);
            if (!before.IsUsable)
                return ReviewerGuardResult.Allowed;

            var after = EvaluateCapacity(
                companyId,
                change: null,
                rolePermissionOverride: (securityRoleId, new HashSet<string>(
                    newPermissionNamesForRole ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase)));
            if (after.IsUsable)
                return ReviewerGuardResult.Allowed;

            return LastReviewerBlocked();
        }

        private bool RequiresIndependentReview(int companyId)
        {
            // Read the persisted rule directly (no resolver telemetry). Only an explicit independent-review
            // rule is protected. A missing rule (catalog default) keeps legacy behavior so existing
            // workspaces are never trapped by this guard.
            var rule = _db.WorkspaceApprovalRules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefault(r => r.CompanyId == companyId
                                      && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
            return rule != null
                && (rule.RuleKind == WorkspaceApprovalRuleKind.IndependentReviewerRequired
                    || rule.PolicySchemaVersion != WorkspaceOperatingPolicySchema.CurrentVersion);
        }

        private static ReviewerGuardResult LastReviewerBlocked() =>
            new(
                true,
                WorkspacePolicyReasonCodes.LastReviewerUnavailable,
                "This change would leave the workspace without a usable independent reviewer for journal posting. "
                + "Keep or add another Owner/Admin who can post journals, or change the journal approval policy to single-actor in the same request.");

        private IndependentReviewCapacity EvaluateCapacity(
            int companyId,
            MemberReviewerChange? change,
            (int RoleId, IReadOnlySet<string> Permissions)? rolePermissionOverride)
        {
            var members = _db.CompanyMembers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(m => m.SecurityRoles)
                .Where(m => m.CompanyId == companyId && m.Status == CompanyMemberStatus.Active)
                .ToList()
                .Where(m => !m.UserId.StartsWith(SystemActorPrefix, StringComparison.Ordinal))
                .ToList();

            var remaining = members
                .Where(m => !(change != null && change.Removed && change.MemberId == m.Id))
                .ToList();

            var financialsWritable = IsFinancialsWritable(companyId);
            var permissionsByRoleId = LoadPermissionsByRole(remaining, change, rolePermissionOverride);

            var reviewers = new List<EligibleJournalReviewer>();
            var preparers = new List<EligibleJournalReviewer>();
            if (financialsWritable)
            {
                foreach (var member in remaining)
                {
                    var effectiveRole = change != null && change.MemberId == member.Id && change.NewRole.HasValue
                        ? change.NewRole.Value
                        : member.Role;

                    var roleIds = change != null && change.MemberId == member.Id && change.NewSecurityRoleIds != null
                        ? change.NewSecurityRoleIds
                        : member.SecurityRoles.Select(r => r.SecurityRoleId).ToList();

                    bool HasPermission(string permissionName) =>
                        roleIds.Any(id =>
                            permissionsByRoleId.TryGetValue(id, out var permissions)
                            && permissions.Contains(permissionName));

                    // Preparers draft journals (create). Reviewers approve/post and must hold Owner/Admin
                    // governance. Split create-only / post-only assignments are a valid independent-review shape.
                    var canCreate = HasPermission(SystemPermissions.FinancialsJournalCreate);
                    var canPost = HasPermission(SystemPermissions.FinancialsJournalPost);
                    if (!canCreate && !canPost)
                        continue;

                    var entry = new EligibleJournalReviewer(member.Id, member.UserId, effectiveRole);
                    if (canCreate)
                        preparers.Add(entry);
                    if (canPost && effectiveRole is CompanyMemberRole.Owner or CompanyMemberRole.Admin)
                        reviewers.Add(entry);
                }
            }

            return new IndependentReviewCapacity(remaining.Count, reviewers, preparers);
        }

        private bool IsFinancialsWritable(int companyId)
        {
            if (_entitlements == null)
                return true;

            try
            {
                var decision = _entitlements.Evaluate(
                    companyId,
                    new EntitlementAction.Module(ModuleAccess.Write, SystemModules.Financials));
                return decision.Allowed;
            }
            catch (NotSupportedException)
            {
                // Entitlement service cannot answer: fail closed rather than assume reviewers are usable.
                return false;
            }
        }

        private Dictionary<int, HashSet<string>> LoadPermissionsByRole(
            IReadOnlyCollection<CompanyMember> members,
            MemberReviewerChange? change,
            (int RoleId, IReadOnlySet<string> Permissions)? rolePermissionOverride)
        {
            var roleIds = members.SelectMany(m => m.SecurityRoles.Select(r => r.SecurityRoleId)).ToHashSet();
            if (change?.NewSecurityRoleIds != null)
                roleIds.UnionWith(change.NewSecurityRoleIds);
            if (rolePermissionOverride.HasValue)
                roleIds.Add(rolePermissionOverride.Value.RoleId);
            if (roleIds.Count == 0)
                return new Dictionary<int, HashSet<string>>();

            var loaded = _db.Set<SecurityRolePermission>()
                .AsNoTracking()
                .Where(srp => roleIds.Contains(srp.SecurityRoleId) && srp.SecurityPermission != null)
                .Select(srp => new { srp.SecurityRoleId, srp.SecurityPermission!.Name })
                .ToList()
                .GroupBy(x => x.SecurityRoleId)
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(x => x.Name), StringComparer.OrdinalIgnoreCase));

            if (rolePermissionOverride.HasValue)
            {
                // Hypothetical post-edit permission set for the template, not the currently persisted one.
                loaded[rolePermissionOverride.Value.RoleId] = new HashSet<string>(
                    rolePermissionOverride.Value.Permissions,
                    StringComparer.OrdinalIgnoreCase);
            }

            return loaded;
        }
    }
}
