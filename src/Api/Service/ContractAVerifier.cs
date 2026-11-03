// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Controllers;
using Api.Data;
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed record ContractAVerificationResult(
        bool Allowed,
        string Title,
        string Detail,
        CompanyMember? OwnerMember = null);

    /// <summary>
    /// Implements Contract A fail-closed eligibility checks for legacy sole-owner self-grants.
    /// A workspace without continuous solo-owner origin evidence or with any collaboration/invitation
    /// history cannot self-grant functional roles.
    /// </summary>
    public static class ContractAVerifier
    {
        public static ContractAVerificationResult Verify(
            ApiDbContext db,
            int companyId,
            string callerUserId,
            IReadOnlyCollection<int> requestedRoleIds)
        {
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                return new ContractAVerificationResult(false, "Unauthenticated", "User must be authenticated.");
            }

            if (requestedRoleIds == null || requestedRoleIds.Count == 0 || !WorkspaceMembersController.ValidSecurityRoles(db, requestedRoleIds))
            {
                return new ContractAVerificationResult(false, "Invalid functional roles", "Select valid functional roles.");
            }

            var eligibility = VerifySolePristineOwner(db, companyId, callerUserId, "self-grant");
            if (!eligibility.Allowed)
                return eligibility;

            return new ContractAVerificationResult(true, "Eligible", "Eligible for self-grant.", eligibility.OwnerMember);
        }

        /// <summary>
        /// Fail-closed proof that <paramref name="companyId"/> is a pristine, never-collaborated workspace
        /// still owned solely by its original creating owner (<paramref name="callerUserId"/>), awaiting the
        /// approved post-verification signup continuation. It requires BOTH the signup-specific
        /// pending-continuation marker (written only by the approved signup flow, bound to this workspace
        /// and owner — generic origin evidence is NOT sufficient because every created workspace carries it)
        /// AND the structural eligibility shared with the legacy self-grant: an uninitialized
        /// LegacyUnclassified policy that has never activated a team, a single active original owner that was
        /// not invited and matches the creator, and the total absence of invitation, policy-transition,
        /// ownership-transfer, or collaboration history. A legacy, current-team, former-team,
        /// transferred-owner, already-initialized, or pristine-but-non-signup workspace fails here and is
        /// never granted anything. Callers MUST invoke this inside the workspace policy coordination lock.
        /// </summary>
        public static ContractAVerificationResult VerifyPendingSignupContinuation(
            ApiDbContext db,
            int companyId,
            string callerUserId)
        {
            if (string.IsNullOrWhiteSpace(callerUserId))
                return new ContractAVerificationResult(false, "Unauthenticated", "User must be authenticated.");

            // Decisive, signup-specific evidence. Generic origin evidence is insufficient because EVERY
            // application-created workspace carries it; only the approved signup flow writes this immutable
            // pending-continuation marker, bound to the workspace and its original owner. A pristine but
            // non-signup workspace (e.g. created directly through the authenticated API and left
            // uninitialized) lacks it and must be rejected here, before the shared structural checks.
            if (!WorkspaceSignupContinuationEvidence.IsPendingContinuation(db, companyId, callerUserId))
            {
                return new ContractAVerificationResult(
                    false,
                    "Not a pending signup workspace",
                    "This workspace is not awaiting the approved signup continuation.");
            }

            return VerifySolePristineOwner(db, companyId, callerUserId, "continuation");
        }

        /// <summary>
        /// Shared, fail-closed structural proof that a workspace is pristine and solely owned by its
        /// original creator: durable origin evidence bound to the caller, an uninitialized
        /// LegacyUnclassified policy that never activated a team, a single active uninvited owner matching
        /// the creator, and no invitation, policy-transition, ownership-transfer, or collaboration history.
        /// Does NOT require the signup-specific marker — use <see cref="VerifyPendingSignupContinuation"/>
        /// for the authenticated continuation endpoint, which adds that requirement.
        /// </summary>
        public static ContractAVerificationResult VerifySolePristineOwner(
            ApiDbContext db,
            int companyId,
            string callerUserId,
            string operation)
        {
            // 1. Immutable origin row written by WorkspaceService.Create in the same transaction as initial owner.
            //    This is the durable, server-issued evidence binding the workspace to its original creator;
            //    backfilled or migrated legacy workspaces never receive it.
            if (!WorkspaceOriginEvidence.HasValidOrigin(db, companyId, callerUserId))
            {
                return new ContractAVerificationResult(
                    false,
                    "Origin evidence required",
                    "This workspace does not have continuous solo-owner origin evidence.");
            }

            // 2. Operating settings exist, LegacyUnclassified, uninitialized, EverTeamActivatedAtUtc null, current schema version.
            var settings = db.WorkspaceOperatingSettings
                .IgnoreQueryFilters()
                .SingleOrDefault(s => s.CompanyId == companyId);

            if (settings == null
                || settings.CollaborationState != WorkspaceCollaborationState.LegacyUnclassified
                || settings.InitializedAtUtc.HasValue
                || settings.EverTeamActivatedAtUtc.HasValue
                || settings.PolicySchemaVersion != WorkspaceOperatingPolicySchema.CurrentVersion)
            {
                return new ContractAVerificationResult(
                    false,
                    "Operating policy ineligible",
                    $"Workspace operating settings are not eligible for {operation}.");
            }

            // 3. Exactly one CompanyMember row. Active, Owner, UserId == callerUserId == company.CreatedByUserId, InvitedByUserId null.
            var members = db.CompanyMembers
                .IgnoreQueryFilters()
                .Include(m => m.SecurityRoles)
                .Where(m => m.CompanyId == companyId)
                .ToList();

            var company = db.Companies.IgnoreQueryFilters().SingleOrDefault(c => c.Id == companyId);
            if (company == null)
            {
                return new ContractAVerificationResult(false, "Workspace not found", "Workspace could not be found.");
            }

            if (members.Count != 1)
            {
                return new ContractAVerificationResult(
                    false,
                    "Sole owner required",
                    $"Only a sole active workspace owner can perform {operation}.");
            }

            var owner = members[0];
            if (owner.Status != CompanyMemberStatus.Active
                || owner.Role != CompanyMemberRole.Owner
                || !string.Equals(owner.UserId, callerUserId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(company.CreatedByUserId)
                || !string.Equals(company.CreatedByUserId, callerUserId, StringComparison.Ordinal)
                || owner.InvitedByUserId != null)
            {
                return new ContractAVerificationResult(
                    false,
                    "Sole owner required",
                    "Caller must be the sole original creating owner without invitations.");
            }

            // 4. No CompanyInvitation row in any state: pending, accepted, or revoked.
            var hasAnyInvitations = db.CompanyInvitations
                .IgnoreQueryFilters()
                .Any(i => i.CompanyId == companyId);

            if (hasAnyInvitations)
            {
                return new ContractAVerificationResult(
                    false,
                    "Invitation history exists",
                    $"Workspaces with invitation history cannot perform {operation}.");
            }

            // 5. No __WorkspacePolicyTransition row, and no confirmed-owner change.
            var hasTransitions = db.AuditLogs
                .IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId && a.TableName == "__WorkspacePolicyTransition");

            if (hasTransitions || settings.ConfirmedOwnerMemberId.HasValue)
            {
                return new ContractAVerificationResult(
                    false,
                    "Policy transition history exists",
                    $"Workspaces with policy transitions cannot perform {operation}.");
            }

            // 6. Zero __WorkspaceCollaborationEvidence indicating member removals, second members, or invitations
            var hasCollaborationEvidence = db.AuditLogs
                .IgnoreQueryFilters()
                .Any(a => a.CompanyId == companyId &&
                          a.TableName == WorkspaceCollaborationEvidence.AuditTableName &&
                          (a.FieldName == WorkspaceCollaborationEvidence.EventMemberRemoved ||
                           a.FieldName == WorkspaceCollaborationEvidence.EventMemberRoleUpdated ||
                           a.FieldName == WorkspaceCollaborationEvidence.EventInvitationAccepted ||
                           a.FieldName == WorkspaceCollaborationEvidence.EventOwnershipTransferred ||
                           a.FieldName == WorkspaceCollaborationEvidence.EventAccountDeleted ||
                           (a.FieldName == WorkspaceCollaborationEvidence.EventMemberAdded && a.RecordId != callerUserId)));

            if (hasCollaborationEvidence)
            {
                return new ContractAVerificationResult(
                    false,
                    "Collaboration history exists",
                    $"Workspaces with collaboration evidence cannot perform {operation}.");
            }

            return new ContractAVerificationResult(true, "Eligible", $"Eligible for {operation}.", owner);
        }
    }
}
