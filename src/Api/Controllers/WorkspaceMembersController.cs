#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Api.Data;
using Api.Filters;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/organizations/current")]
    public class WorkspaceMembersController : BaseController
    {
        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;
        private readonly ITenantContext _tenant;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        private bool IsOwner => _tenant.Role == CompanyMemberRole.Owner;

        public WorkspaceMembersController(ApiDbContext db, ApplicationIdentityDbContext identity, ITenantContext tenant)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
        }

        [HttpGet("members")]
        public IActionResult Members()
        {
            var companyId = _tenant.RequireCompanyId();
            var members = _db.CompanyMembers.Include(m => m.SecurityRoles).ThenInclude(r => r.SecurityRole)
                .Where(m => m.CompanyId == companyId).OrderBy(m => m.JoinedAt).ToList();
            var ids = members.Select(m => m.UserId).ToList();
            var users = _identity.Users.Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id);
            var emails = users.Values.Select(u => u.Email).ToList();
            var profiles = _db.Users.Where(u => emails.Contains(u.EmailAddress)).ToList();
            return Ok(members.Select(m =>
            {
                users.TryGetValue(m.UserId, out var user);
                var profile = profiles.FirstOrDefault(u => string.Equals(u.EmailAddress, user?.Email, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    memberId = m.Id, userId = m.UserId, email = user?.Email,
                    firstName = profile?.Firstname ?? "", lastName = profile?.Lastname ?? "",
                    role = m.Role.ToString(), status = m.Status.ToString(), joinedAt = m.JoinedAt,
                    securityRoles = m.SecurityRoles.Select(r => new { id = r.SecurityRoleId, name = r.SecurityRole.Name, displayName = r.SecurityRole.DisplayName })
                };
            }));
        }

        [HttpGet("security-roles")]
        public IActionResult SecurityRoles()
        {
            var companyId = _tenant.RequireCompanyId();
            // Role templates are global; memberCount is this workspace's active members holding the role.
            return Ok(_db.SecurityRoles.Where(r => !r.SysAdmin && r.Name != "SystemAdministrators")
                .Select(r => new
                {
                    r.Id, r.Name, r.DisplayName,
                    permissions = r.Permissions.Select(p => p.SecurityPermission.Name).ToList(),
                    memberCount = _db.CompanyMemberSecurityRoles.Count(mr => mr.SecurityRoleId == r.Id
                        && mr.CompanyMember.CompanyId == companyId && mr.CompanyMember.Status == CompanyMemberStatus.Active)
                }).ToList());
        }

        public class UpdateMemberRequest
        {
            public string Role { get; set; }
            public List<int> SecurityRoleIds { get; set; }
        }

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("members/{memberId:int}")]
        public IActionResult Update(int memberId, [FromBody] UpdateMemberRequest request)
        {
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var member = FindMember(memberId);
                if (member == null)
                    return NotFound();
                if (!IsOwner && member.Role != CompanyMemberRole.Member)
                    return Problem(statusCode: 403, title: "Admins can manage Members only");
                var role = member.Role;
                if (request.Role != null && !TryAssignableRole(request.Role, out role))
                    return Problem(statusCode: 400, title: "Role must be Admin or Member");
                if (!IsOwner && role == CompanyMemberRole.Admin)
                    return Problem(statusCode: 403, title: "Only Owners can grant Admin");
                if (member.Role == CompanyMemberRole.Owner && role != CompanyMemberRole.Owner && IsLastOwner())
                    return Problem(statusCode: 409, title: "The last Owner cannot be demoted");
                if (request.SecurityRoleIds != null && !ValidSecurityRoles(_db, request.SecurityRoleIds))
                    return Problem(statusCode: 400, title: "Invalid workspace security roles");

                member.Role = role;
                if (request.SecurityRoleIds != null)
                {
                    _db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
                    member.SecurityRoles = request.SecurityRoleIds.Distinct()
                        .Select(id => new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = id }).ToList();
                }
                _db.SaveChanges();
                tx.Commit();
                return NoContent();
            }
        }

        [HttpDelete("members/{memberId:int}")]
        public IActionResult Remove(int memberId)
        {
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var member = FindMember(memberId);
                if (member == null)
                    return NotFound();
                var self = member.UserId == UserId;
                if (!self && (_tenant.Role == CompanyMemberRole.Member || (!IsOwner && member.Role != CompanyMemberRole.Member)))
                    return Problem(statusCode: 403, title: "You cannot remove this member");
                if (member.Role == CompanyMemberRole.Owner && IsLastOwner())
                    return Problem(statusCode: 409, title: "The last Owner cannot leave or be removed");
                _db.CompanyMembers.Remove(member);
                _db.SaveChanges();
                tx.Commit();
                return NoContent();
            }
        }

        public class TransferRequest
        {
            public int MemberId { get; set; }
        }

        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [Authorize(Policy = "MfaStepUp")]
        [HttpPost("transfer-ownership")]
        public IActionResult Transfer([FromBody] TransferRequest request)
        {
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var target = FindMember(request.MemberId);
                if (target == null || target.Status != CompanyMemberStatus.Active)
                    return Problem(statusCode: 404, title: "Active member not found");
                if (target.UserId == UserId)
                    return Problem(statusCode: 400, title: "Choose another member");
                var caller = _db.CompanyMembers.Single(m => m.CompanyId == _tenant.RequireCompanyId() && m.UserId == UserId);
                target.Role = CompanyMemberRole.Owner;
                caller.Role = CompanyMemberRole.Admin;
                _db.SaveChanges();
                tx.Commit();
                return NoContent();
            }
        }

        internal static bool TryAssignableRole(string value, out CompanyMemberRole role)
        {
            role = CompanyMemberRole.Member;
            if (string.Equals(value, "Admin", StringComparison.OrdinalIgnoreCase))
                role = CompanyMemberRole.Admin;
            else if (!string.Equals(value, "Member", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        internal static bool ValidSecurityRoles(ApiDbContext db, IEnumerable<int> ids)
        {
            var requested = ids.Distinct().ToList();
            return db.SecurityRoles.Count(r => requested.Contains(r.Id) && !r.SysAdmin && r.Name != "SystemAdministrators") == requested.Count;
        }

        private CompanyMember FindMember(int memberId)
        {
            var companyId = _tenant.RequireCompanyId();
            return _db.CompanyMembers.Include(m => m.SecurityRoles).SingleOrDefault(m => m.Id == memberId && m.CompanyId == companyId);
        }

        private bool IsLastOwner()
        {
            var companyId = _tenant.RequireCompanyId();
            return _db.CompanyMembers.Count(m => m.CompanyId == companyId && m.Role == CompanyMemberRole.Owner && m.Status == CompanyMemberStatus.Active) <= 1;
        }
    }
}
