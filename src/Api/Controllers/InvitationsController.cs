#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize]
    [TenantFree]
    [ApiController]
    [Route("api/invitations")]
    public class InvitationsController : BaseController
    {
        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public InvitationsController(ApiDbContext db, ApplicationIdentityDbContext identity)
        {
            _db = db;
            _identity = identity;
        }

        [HttpGet("pending")]
        public IActionResult Pending()
        {
            var user = _identity.Users.SingleOrDefault(u => u.Id == UserId && u.EmailConfirmed);
            if (user == null)
                return Problem(statusCode: 403, title: "Verify your email before viewing invitations");
            var email = user.Email.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;
            return Ok((from i in _db.CompanyInvitations
                       join c in _db.Companies on i.CompanyId equals c.Id
                       where i.Email == email && i.RevokedAt == null && i.AcceptedAt == null && i.ExpiresAt > now
                       select new { id = i.Id, workspaceName = c.Name, slug = c.Slug, role = i.Role.ToString(), expiresAt = i.ExpiresAt }).ToList());
        }

        [AllowAnonymous]
        [EnableRateLimiting("invitation-preview")]
        [HttpGet("{token}")]
        public IActionResult Preview(string token)
        {
            var invitation = Find(token);
            if (invitation == null)
                return NotFound();
            var company = _db.Companies.Single(c => c.Id == invitation.CompanyId);
            var inviterEmail = _identity.Users.Where(u => u.Id == invitation.InvitedByUserId).Select(u => u.Email).SingleOrDefault();
            var profile = _db.Users.FirstOrDefault(u => u.EmailAddress == inviterEmail);
            var at = invitation.Email.IndexOf('@');
            var masked = invitation.Email.Substring(0, 1) + "***" + invitation.Email.Substring(at);
            return Ok(new
            {
                workspaceName = company.Name, inviterName = profile == null ? "A workspace administrator" : (profile.Firstname + " " + profile.Lastname).Trim(),
                role = invitation.Role.ToString(), emailMasked = masked, expired = invitation.ExpiresAt <= DateTime.UtcNow,
                accepted = invitation.AcceptedAt.HasValue
            });
        }

        [HttpPost("{token}/accept")]
        public IActionResult Accept(string token)
        {
            return AcceptInvitation(() => Find(token));
        }

        // Recovery path for verified users without the original invitation link. No token is exposed by pending.
        [HttpPost("pending/{id:int}/accept")]
        public IActionResult AcceptPending(int id)
        {
            return AcceptInvitation(() => FindById(id));
        }

        private IActionResult AcceptInvitation(Func<CompanyInvitation> find)
        {
            var user = _identity.Users.SingleOrDefault(u => u.Id == UserId && u.EmailConfirmed);
            if (user == null)
                return Problem(statusCode: 403, title: "Verify your email before accepting invitations");
            if (user.DeletedAt != null)
                return Problem(statusCode: 403, title: "This account has been closed");
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var invitation = find();
                if (invitation == null)
                    return NotFound();
                if (!string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
                    return Problem(statusCode: 403, title: "This invitation was sent to a different email");
                if (invitation.AcceptedAt.HasValue || invitation.ExpiresAt <= DateTime.UtcNow)
                    return Problem(statusCode: 410, title: "Invitation has expired or already been accepted");
                var roles = JsonSerializer.Deserialize<List<int>>(invitation.SecurityRoleIdsJson);
                if (roles == null || !WorkspaceMembersController.ValidSecurityRoles(_db, roles))
                    return Problem(statusCode: 409, title: "Invitation security roles are no longer available", detail: "Ask the workspace administrator for a new invitation.");
                if (_db.CompanyMembers.Any(m => m.CompanyId == invitation.CompanyId && m.UserId == user.Id))
                    return Problem(statusCode: 409, title: "You are already a workspace member");
                var member = new CompanyMember
                {
                    CompanyId = invitation.CompanyId, UserId = user.Id, Role = invitation.Role,
                    Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow, InvitedByUserId = invitation.InvitedByUserId
                };
                foreach (var id in roles.Distinct())
                    member.SecurityRoles.Add(new CompanyMemberSecurityRole { SecurityRoleId = id });
                _db.CompanyMembers.Add(member);
                invitation.AcceptedAt = DateTime.UtcNow;
                invitation.AcceptedByUserId = user.Id;
                _db.SaveChanges();
                tx.Commit();
                return Ok(new { slug = _db.Companies.Where(c => c.Id == invitation.CompanyId).Select(c => c.Slug).Single(), role = member.Role.ToString() });
            }
        }

        private CompanyInvitation Find(string token)
        {
            if (token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                return null;
            var hash = WorkspaceInvitationService.HashToken(token);
            return (from i in _db.CompanyInvitations
                    join c in _db.Companies on i.CompanyId equals c.Id
                    where i.TokenHash == hash && i.RevokedAt == null
                    select i).SingleOrDefault();
        }

        private CompanyInvitation FindById(int id)
        {
            return (from i in _db.CompanyInvitations
                    join c in _db.Companies on i.CompanyId equals c.Id
                    where i.Id == id && i.RevokedAt == null
                    select i).SingleOrDefault();
        }
    }
}
