#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [RequireWorkspaceRole(CompanyMemberRole.Admin)]
    [ApiController]
    [Route("api/organizations/current/invitations")]
    public class WorkspaceInvitationsController : BaseController
    {
        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;
        private readonly ITenantContext _tenant;
        private readonly WorkspaceInvitationService _invitations;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public WorkspaceInvitationsController(ApiDbContext db, ApplicationIdentityDbContext identity, ITenantContext tenant, WorkspaceInvitationService invitations)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
            _invitations = invitations;
        }

        [HttpGet]
        public IActionResult List()
        {
            var companyId = _tenant.RequireCompanyId();
            var now = DateTime.UtcNow;
            return Ok(_db.CompanyInvitations.Where(i => i.CompanyId == companyId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
                .ToList().Select(i => new { id = i.Id, email = i.Email, role = i.Role.ToString(), expiresAt = i.ExpiresAt,
                    securityRoleIds = JsonSerializer.Deserialize<List<int>>(i.SecurityRoleIdsJson) }));
        }

        public class InviteRequest
        {
            [Required, EmailAddress]
            public string Email { get; set; }
            public string Role { get; set; } = "Member";
            public List<int> SecurityRoleIds { get; set; } = new List<int>();
        }

        [HttpPost]
        public async Task<IActionResult> Invite([FromBody] InviteRequest request)
        {
            if (!WorkspaceMembersController.TryAssignableRole(request.Role, out var role))
                return Problem(statusCode: 400, title: "Role must be Admin or Member");
            if (role == CompanyMemberRole.Admin && _tenant.Role != CompanyMemberRole.Owner)
                return Problem(statusCode: 403, title: "Only Owners can invite Admins");
            if (request.SecurityRoleIds == null || !WorkspaceMembersController.ValidSecurityRoles(_db, request.SecurityRoleIds))
                return Problem(statusCode: 400, title: "Invalid workspace security roles");

            var email = request.Email.Trim().ToLowerInvariant();
            var companyId = _tenant.RequireCompanyId();
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var user = _identity.Users.SingleOrDefault(u => u.NormalizedEmail == email.ToUpperInvariant());
                if (user != null && user.DeletedAt != null)
                    return Problem(statusCode: 409, title: AccountDeletionService.ClosedEmailTitle);
                if (user != null && _db.CompanyMembers.Any(m => m.CompanyId == companyId && m.UserId == user.Id))
                    return Problem(statusCode: 409, title: "User is already a workspace member");
                var hourAgo = DateTime.UtcNow.AddHours(-1);
                if (_db.CompanyInvitations.Count(i => i.CompanyId == companyId && i.CreatedAt > hourAgo) >= 20)
                    return Problem(statusCode: 429, title: "Workspace invitation limit reached", detail: "Limit: 20 invitations per hour.");
                var pending = _db.CompanyInvitations.Where(i => i.CompanyId == companyId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null).ToList();
                // Admins cannot replace an Owner's Admin invitation with a different role.
                if (_tenant.Role != CompanyMemberRole.Owner && pending.Any(i => i.Role == CompanyMemberRole.Admin))
                    return Problem(statusCode: 403, title: "Only Owners can manage Admin invitations");
                foreach (var previous in pending)
                    previous.RevokedAt = DateTime.UtcNow;
                var invitation = new CompanyInvitation
                {
                    CompanyId = companyId, Email = email, Role = role, CreatedAt = DateTime.UtcNow,
                    InvitedByUserId = UserId, SecurityRoleIdsJson = JsonSerializer.Serialize(request.SecurityRoleIds.Distinct())
                };
                // Keep delivery inside the transaction so a failed send rolls back the invitation and revocation.
                await SendAsync(invitation);
                _db.CompanyInvitations.Add(invitation);
                await _db.SaveChangesAsync();
                tx.Commit();
                return StatusCode(201, new { id = invitation.Id, email, role = role.ToString(), expiresAt = invitation.ExpiresAt });
            }
        }

        [HttpPost("{id:int}/resend")]
        public async Task<IActionResult> Resend(int id)
        {
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var invitation = Find(id);
                if (invitation == null)
                    return NotFound();
                if (_tenant.Role != CompanyMemberRole.Owner && invitation.Role == CompanyMemberRole.Admin)
                    return Problem(statusCode: 403, title: "Only Owners can manage Admin invitations");
                var now = DateTime.UtcNow;
                if (!invitation.ResendWindowStart.HasValue || invitation.ResendWindowStart.Value <= now.AddDays(-1))
                {
                    invitation.ResendWindowStart = now;
                    invitation.ResendCount = 0;
                }
                if (invitation.ResendCount >= 5)
                    return Problem(statusCode: 429, title: "Invitation resend limit reached", detail: "Limit: 5 resends per invitation per day.");
                invitation.ResendCount++;
                // Keep delivery inside the transaction so a failed send rolls back token rotation and resend limits.
                await SendAsync(invitation);
                await _db.SaveChangesAsync();
                tx.Commit();
                return NoContent();
            }
        }

        [HttpDelete("{id:int}")]
        public IActionResult Revoke(int id)
        {
            var invitation = Find(id);
            if (invitation == null)
                return NotFound();
            if (_tenant.Role != CompanyMemberRole.Owner && invitation.Role == CompanyMemberRole.Admin)
                return Problem(statusCode: 403, title: "Only Owners can manage Admin invitations");
            invitation.RevokedAt = DateTime.UtcNow;
            _db.SaveChanges();
            return NoContent();
        }

        private CompanyInvitation Find(int id)
        {
            var companyId = _tenant.RequireCompanyId();
            return _db.CompanyInvitations.SingleOrDefault(i => i.Id == id && i.CompanyId == companyId && i.RevokedAt == null && i.AcceptedAt == null);
        }

        private Task SendAsync(CompanyInvitation invitation)
        {
            var company = _db.Companies.Single(c => c.Id == invitation.CompanyId);
            var email = _identity.Users.Where(u => u.Id == UserId).Select(u => u.Email).Single();
            var profile = _db.Users.FirstOrDefault(u => u.EmailAddress == email);
            var name = profile == null ? email : (profile.Firstname + " " + profile.Lastname).Trim();
            return _invitations.SendAsync(invitation, company, name);
        }
    }
}
