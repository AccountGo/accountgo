#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Service
{
    /// <summary>Closes a person's login without removing the row, their history, or a company they still have to hand off.</summary>
    public class AccountDeletionService
    {
        public const string WorkspaceBlockTitle = "Transfer ownership or delete these workspaces first";
        public const string PlatformAdminBlockTitle = "The last platform administrator cannot delete their account";
        public const string ConfirmationTitle = "Email confirmation does not match";
        public const string ClosedEmailTitle = "This email belongs to a closed account";
        public const string ReviewerConflictTitle = "Resolve the journal reviewer conflict first";

        private readonly ApiDbContext _apiDb;
        private readonly ApplicationIdentityDbContext _identity;
        private readonly Core.Authorization.IWorkspacePolicyTransaction? _policyTransaction;
        private readonly Core.Authorization.IWorkspaceReviewerEligibility _reviewerEligibility;

        public AccountDeletionService(
            ApiDbContext apiDb,
            ApplicationIdentityDbContext identity,
            Core.Authorization.IWorkspacePolicyTransaction? policyTransaction = null,
            Core.Authorization.IWorkspaceReviewerEligibility? reviewerEligibility = null)
        {
            _apiDb = apiDb ?? throw new ArgumentNullException(nameof(apiDb));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _policyTransaction = policyTransaction;
            _reviewerEligibility = reviewerEligibility ?? new WorkspaceReviewerEligibility(apiDb);
        }

        public async Task<AccountDeletionPreview> PreviewAsync(string userId)
        {
            var user = await _identity.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user == null || user.DeletedAt != null)
                return new AccountDeletionPreview();
            return await EvaluateAsync(user);
        }

        public async Task<AccountDeletionOutcome> DeleteAsync(string userId, string? confirmEmail)
        {
            await using var tx = await BeginAsync();
            var user = await _identity.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user == null || user.DeletedAt != null)
                return new AccountDeletionOutcome { Status = AccountDeletionStatus.Missing };

            var expected = (user.Email ?? "").Trim();
            var given = (confirmEmail ?? "").Trim();
            if (expected.Length == 0 || !string.Equals(expected, given, StringComparison.OrdinalIgnoreCase))
                return new AccountDeletionOutcome { Status = AccountDeletionStatus.ConfirmationMismatch };

            var preview = await EvaluateAsync(user);
            if (preview.Blocked)
                return new AccountDeletionOutcome { Status = AccountDeletionStatus.Blocked, Preview = preview };

            // All validation passed; open the policy completion scope now (after the early returns) so no
            // empty scope lingers on a refused request.
            var policyScope = _policyTransaction?.BeginExternalScope();

            var now = DateTime.UtcNow;
            user.DeletedAt = now;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = DateTime.MinValue;

            var sessions = await _identity.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync();
            foreach (var session in sessions)
                session.RevokedAt = now;

            var otpTokens = await _identity.Set<IdentityUserToken<string>>().Where(t => t.UserId == user.Id).ToListAsync();
            _identity.RemoveRange(otpTokens);

            var members = await _apiDb.CompanyMembers.Include(m => m.SecurityRoles).Where(m => m.UserId == user.Id).ToListAsync();
            var companyIds = members.Select(m => m.CompanyId).Distinct().OrderBy(id => id).ToList();

            Action performDeletion = () =>
            {
                foreach (var member in members)
                {
                    var settings = _apiDb.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleOrDefault(s => s.CompanyId == member.CompanyId);
                    if (settings != null)
                    {
                        settings.EverTeamActivatedAtUtc ??= now;
                        settings.PolicyRevision++;
                    }
                    WorkspaceCollaborationEvidence.Record(_apiDb, member.CompanyId, WorkspaceCollaborationEvidence.EventAccountDeleted, user.Id, user.Id);
                    _apiDb.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
                }
                _apiDb.CompanyMembers.RemoveRange(members);
            };

            if (_policyTransaction != null && companyIds.Count > 0)
            {
                _policyTransaction.ExecuteAcrossCompanies(companyIds, guards =>
                {
                    performDeletion();
                });
            }
            else
            {
                performDeletion();
            }

            var email = expected.ToLowerInvariant();
            var inbound = await _apiDb.CompanyInvitations
                .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null)
                .ToListAsync();
            foreach (var invitation in inbound)
                invitation.RevokedAt = now;

            await _apiDb.SaveChangesAsync();
            await _identity.SaveChangesAsync();
            await tx.CommitAsync();
            // Externally-owned transaction committed; drain ONLY this scope's deferred committed-state
            // verification for the affected workspaces (e.g. a workspace left inconsistent by member removal).
            policyScope?.Committed();
            return new AccountDeletionOutcome { Status = AccountDeletionStatus.Deleted, Preview = preview };
        }

        private async Task<AccountDeletionPreview> EvaluateAsync(ApplicationUser user)
        {
            var memberships = await _apiDb.CompanyMembers
                .Where(m => m.UserId == user.Id && m.Status == CompanyMemberStatus.Active)
                .Select(m => new { m.CompanyId, m.Role })
                .ToListAsync();
            var companyIds = memberships.Select(m => m.CompanyId).Distinct().ToList();
            var liveCompanies = await _apiDb.Companies
                .Where(c => companyIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Slug })
                .ToListAsync();
            var liveIds = liveCompanies.Select(c => c.Id).ToList();
            var liveMembers = await _apiDb.CompanyMembers
                .Where(m => liveIds.Contains(m.CompanyId) && m.Status == CompanyMemberStatus.Active)
                .Select(m => new { m.Id, m.CompanyId, m.UserId, m.Role })
                .ToListAsync();

            var workspaces = new List<AccountDeletionWorkspace>();
            var reviewerConflicts = new List<AccountDeletionReviewerConflict>();
            foreach (var company in liveCompanies)
            {
                var members = liveMembers.Where(m => m.CompanyId == company.Id).ToList();
                var mine = members.SingleOrDefault(m => m.UserId == user.Id);
                if (mine == null)
                    continue;
                var soleOwner = mine.Role == CompanyMemberRole.Owner && members.Count(m => m.Role == CompanyMemberRole.Owner) <= 1;
                var onlyMember = members.Count <= 1;
                if (soleOwner || onlyMember)
                {
                    workspaces.Add(new AccountDeletionWorkspace { Name = company.Name, Slug = company.Slug ?? "" });
                    continue;
                }

                // Leaving must not silently disable approval: if this person is the workspace's last usable
                // independent reviewer, name the conflict so a replacement reviewer or an explicit Owner policy
                // change can resolve it first.
                var verdict = _reviewerEligibility.EvaluateMemberChange(
                    company.Id,
                    new Core.Authorization.MemberReviewerChange(mine.Id, Removed: true));
                if (verdict.Blocked)
                {
                    reviewerConflicts.Add(new AccountDeletionReviewerConflict
                    {
                        Name = company.Name,
                        Slug = company.Slug ?? "",
                        ReasonCode = verdict.ReasonCode ?? Core.Authorization.WorkspacePolicyReasonCodes.LastReviewerUnavailable,
                        Message = $"Deleting your account would remove the last usable independent journal reviewer in {company.Name}. "
                            + "Ask a workspace Owner to add another reviewer or explicitly change the journal approval policy, then try again."
                    });
                }
            }

            return new AccountDeletionPreview
            {
                Workspaces = workspaces,
                ReviewerConflicts = reviewerConflicts,
                PlatformAdministrator = await IsLastPlatformAdministratorAsync(user)
            };
        }

        private async Task<bool> IsLastPlatformAdministratorAsync(ApplicationUser user)
        {
            var roleId = await _apiDb.SecurityRoles
                .Where(r => r.Name == "SystemAdministrators")
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();
            if (roleId == null || string.IsNullOrEmpty(user.NormalizedEmail))
                return false;

            var adminEmails = await _apiDb.SecurityUserRoles
                .Where(r => r.SecurityRoleId == roleId)
                .Select(r => r.User.EmailAddress)
                .ToListAsync();
            var normalized = adminEmails
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => email.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (!normalized.Contains(user.NormalizedEmail))
                return false;

            var live = await _identity.Users.CountAsync(u => u.DeletedAt == null && u.NormalizedEmail != null && normalized.Contains(u.NormalizedEmail));
            return live <= 1;
        }

        /// <summary>Identity and the ledger share one transaction so a closed login cannot remain the last owner.</summary>
        private async Task<IDbContextTransaction> BeginAsync()
        {
            var tx = await _apiDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var identityConnection = _identity.Database.GetDbConnection();
            if (!ReferenceEquals(identityConnection, _apiDb.Database.GetDbConnection()))
            {
                if (identityConnection.State != ConnectionState.Closed)
                    await identityConnection.CloseAsync();
                _identity.Database.SetDbConnection(_apiDb.Database.GetDbConnection());
            }
            await _identity.Database.UseTransactionAsync(tx.GetDbTransaction());
            return tx;
        }
    }

    public enum AccountDeletionStatus { Deleted, ConfirmationMismatch, Blocked, Missing }

    public sealed class AccountDeletionOutcome
    {
        public AccountDeletionStatus Status { get; init; }
        public AccountDeletionPreview Preview { get; init; } = new AccountDeletionPreview();
    }

    public sealed class AccountDeletionPreview
    {
        public List<AccountDeletionWorkspace> Workspaces { get; init; } = new List<AccountDeletionWorkspace>();
        public List<AccountDeletionReviewerConflict> ReviewerConflicts { get; init; } = new List<AccountDeletionReviewerConflict>();
        public bool PlatformAdministrator { get; init; }
        public bool Blocked => PlatformAdministrator || Workspaces.Count > 0 || ReviewerConflicts.Count > 0;
    }

    public sealed class AccountDeletionReviewerConflict
    {
        public string Name { get; init; } = "";
        public string Slug { get; init; } = "";
        public string ReasonCode { get; init; } = "";
        public string Message { get; init; } = "";
    }

    public sealed class AccountDeletionWorkspace
    {
        public string Name { get; init; } = "";
        public string Slug { get; init; } = "";
    }
}
