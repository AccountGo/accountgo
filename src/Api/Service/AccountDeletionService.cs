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

        private readonly ApiDbContext _apiDb;
        private readonly ApplicationIdentityDbContext _identity;

        public AccountDeletionService(ApiDbContext apiDb, ApplicationIdentityDbContext identity)
        {
            _apiDb = apiDb ?? throw new ArgumentNullException(nameof(apiDb));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
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
            foreach (var member in members)
                _apiDb.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
            _apiDb.CompanyMembers.RemoveRange(members);

            var email = expected.ToLowerInvariant();
            var inbound = await _apiDb.CompanyInvitations
                .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null)
                .ToListAsync();
            foreach (var invitation in inbound)
                invitation.RevokedAt = now;

            await _apiDb.SaveChangesAsync();
            await _identity.SaveChangesAsync();
            await tx.CommitAsync();
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
                .Select(m => new { m.CompanyId, m.UserId, m.Role })
                .ToListAsync();

            var workspaces = new List<AccountDeletionWorkspace>();
            foreach (var company in liveCompanies)
            {
                var members = liveMembers.Where(m => m.CompanyId == company.Id).ToList();
                var mine = members.SingleOrDefault(m => m.UserId == user.Id);
                if (mine == null)
                    continue;
                var soleOwner = mine.Role == CompanyMemberRole.Owner && members.Count(m => m.Role == CompanyMemberRole.Owner) <= 1;
                var onlyMember = members.Count <= 1;
                if (soleOwner || onlyMember)
                    workspaces.Add(new AccountDeletionWorkspace { Name = company.Name, Slug = company.Slug ?? "" });
            }

            return new AccountDeletionPreview
            {
                Workspaces = workspaces,
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
        public bool PlatformAdministrator { get; init; }
        public bool Blocked => PlatformAdministrator || Workspaces.Count > 0;
    }

    public sealed class AccountDeletionWorkspace
    {
        public string Name { get; init; } = "";
        public string Slug { get; init; } = "";
    }
}
