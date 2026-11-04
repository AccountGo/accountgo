#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Api.Authorization;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        private readonly Core.Entitlements.IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;
        private readonly IAccessAuthorizationService _authorizationService;
        private readonly IWorkspacePolicyTransaction _policyTransaction;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public InvitationsController(
            ApiDbContext db,
            ApplicationIdentityDbContext identity,
            Core.Entitlements.IEntitlementService entitlements = null,
            ITenantContext tenant = null,
            IAccessAuthorizationService authorizationService = null,
            IWorkspacePolicyTransaction policyTransaction = null)
        {
            _db = db;
            _identity = identity;
            _entitlements = entitlements;
            _tenant = tenant;
            _authorizationService = authorizationService;
            _policyTransaction = policyTransaction;
        }

        private IAccessAuthorizationService AuthService =>
            _authorizationService
            ?? HttpContext?.RequestServices?.GetService<IAccessAuthorizationService>()
            ?? new AccessAuthorizationService(
                _tenant ?? HttpContext?.RequestServices?.GetService<ITenantContext>() ?? new TenantContext(),
                _entitlements ?? HttpContext?.RequestServices?.GetService<Core.Entitlements.IEntitlementService>(),
                _db,
                new HttpContextAccessor { HttpContext = HttpContext },
                HttpContext?.RequestServices?.GetService<Api.Service.IMfaGate>());

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
            var securityRoleIds = string.IsNullOrWhiteSpace(invitation.SecurityRoleIdsJson)
                ? new List<int>()
                : JsonSerializer.Deserialize<List<int>>(invitation.SecurityRoleIdsJson) ?? new List<int>();
            var grantedRoles = _db.SecurityRoles
                .Where(r => securityRoleIds.Contains(r.Id) && !r.SysAdmin)
                .Select(r => new { r.DisplayName, r.Name, Permissions = r.Permissions.Select(p => p.SecurityPermission.Name).ToList() })
                .ToList();
            var access = new
            {
                roles = grantedRoles.Select(r => string.IsNullOrWhiteSpace(r.DisplayName) ? r.Name : r.DisplayName).OrderBy(n => n).ToList(),
                areas = AccessSummaryBuilder.Describe(grantedRoles.SelectMany(r => r.Permissions).Distinct())
                    .Select(a => new { area = a.Area, label = a.Label, level = a.Level }).ToList()
            };
            return Ok(new
            {
                access,
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

            var policyTx = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (policyTx == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ProblemDetailsFactory.CreateProblemDetails(
                    HttpContext, statusCode: 503, title: "Policy service unavailable", detail: "Workspace policy coordination is unavailable."));

            var preliminaryInvitation = find();
            if (preliminaryInvitation == null)
                return NotFound();

            return policyTx.Execute<IActionResult>(preliminaryInvitation.CompanyId, guard =>
            {
                var invitation = find();
                if (invitation == null)
                    return NotFound();
                using var impersonation = _tenant?.Impersonate(invitation.CompanyId);
                if (!string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
                    return Problem(statusCode: 403, title: "This invitation was sent to a different email");
                if (invitation.AcceptedAt.HasValue || invitation.ExpiresAt <= DateTime.UtcNow)
                    return Problem(statusCode: 410, title: "Invitation has expired or already been accepted");
                var roles = JsonSerializer.Deserialize<List<int>>(invitation.SecurityRoleIdsJson);
                if (roles == null || !WorkspaceMembersController.ValidSecurityRoles(_db, roles))
                    return Problem(statusCode: 409, title: "Invitation security roles are no longer available", detail: "Ask the workspace administrator for a new invitation.");
                if (string.Equals(invitation.InvitedByUserId, user.Id, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-grant denied", detail: "You cannot accept an invitation you created for yourself.");
                var inviter = _db.CompanyMembers.Include(m => m.SecurityRoles)
                    .SingleOrDefault(m => m.CompanyId == invitation.CompanyId && m.UserId == invitation.InvitedByUserId && m.Status == CompanyMemberStatus.Active);
                if (inviter == null || (inviter.Role != CompanyMemberRole.Owner && inviter.Role != CompanyMemberRole.Admin)
                    || (invitation.Role == CompanyMemberRole.Admin && inviter.Role != CompanyMemberRole.Owner))
                    return Problem(statusCode: 403, title: "Invitation authority no longer valid", detail: "The administrator who issued this invitation no longer has authority to grant access.");
                if (_db.CompanyMembers.Any(m => m.CompanyId == invitation.CompanyId && m.UserId == user.Id))
                    return Problem(statusCode: 409, title: "You are already a workspace member");

                var distinctRoles = roles.Distinct().ToList();
                string? approvalRequestId = null;
                string? approverUserId = null;
                if (distinctRoles.Count > 0)
                {
                    var inviterRoleIds = inviter.SecurityRoles.Select(sr => sr.SecurityRoleId).ToList();
                    var inviterPermissions = inviterRoleIds.Count > 0
                        ? _db.Set<Core.Domain.Security.SecurityRolePermission>()
                            .AsNoTracking()
                            .Where(srp => inviterRoleIds.Contains(srp.SecurityRoleId) && srp.SecurityPermission != null)
                            .Select(srp => srp.SecurityPermission.Name)
                            .Distinct()
                            .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var inviterActor = new AccessActorContext
                    {
                        TrustDomain = ActorTrustDomain.WorkspaceUser,
                        UserId = inviter.UserId,
                        CompanyId = invitation.CompanyId,
                        MemberId = inviter.Id,
                        WorkspaceRole = inviter.Role,
                        MembershipStatus = inviter.Status,
                        FunctionalPermissions = inviterPermissions
                    };

                    var approvalRow = _db.AuditLogs
                        .AsNoTracking()
                        .Where(a => a.CompanyId == invitation.CompanyId &&
                                    a.TableName == "__InvitationApproval" &&
                                    a.RecordId == invitation.Id.ToString())
                        .OrderByDescending(a => a.Id)
                        .FirstOrDefault();

                    approvalRequestId = approvalRow?.NewValue;
                    approverUserId = approvalRow?.OriginalValue;

                    if (string.IsNullOrWhiteSpace(approvalRequestId))
                    {
                        var matchingApproval = _db.AuditLogs
                            .AsNoTracking()
                            .Where(a => a.CompanyId == invitation.CompanyId &&
                                        a.TableName == AccessAuthorizationService.ApprovalAuditTableName &&
                                        a.OriginalValue == "Approved")
                            .OrderByDescending(a => a.Id)
                            .ToList()
                            .FirstOrDefault(a =>
                            {
                                if (string.IsNullOrWhiteSpace(a.NewValue)) return false;
                                try
                                {
                                    var p = JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(a.NewValue);
                                    return p != null &&
                                           string.Equals(p.TargetEmail, invitation.Email, StringComparison.OrdinalIgnoreCase) &&
                                           string.Equals(p.OperationKey, AccessOperationCatalog.WorkspaceInvitationsCreate, StringComparison.Ordinal) &&
                                           p.ExpiresAtUtc > DateTimeOffset.UtcNow;
                                }
                                catch { return false; }
                            });

                        if (matchingApproval != null)
                        {
                            approvalRequestId = matchingApproval.RecordId;
                            approverUserId = matchingApproval.UserName;
                        }
                    }

                    var delegationDecision = AuthService.Authorize(
                        AccessOperationCatalog.WorkspaceInvitationsCreate,
                        new AccessResourceContext
                        {
                            WorkspaceCompanyId = invitation.CompanyId,
                            ResourceCompanyId = invitation.CompanyId,
                            TargetUserId = user.Id,
                            TargetEmail = invitation.Email,
                            RequestedWorkspaceRole = invitation.Role,
                            RequestedSecurityRoleIds = distinctRoles,
                            ApprovalRequestId = approvalRequestId,
                            IndependentApproverUserId = approverUserId
                        },
                        actorOverride: inviterActor);

                    if (!delegationDecision.Allowed)
                        return Problem(statusCode: delegationDecision.HttpStatus, title: delegationDecision.SafeTitle, detail: delegationDecision.SafeMessage);
                }

                var transition = WorkspaceInvitationTransition.ValidateOnAccept(
                    _db, invitation, distinctRoles, guard.Settings.PolicyRevision);
                if (!transition.Ok)
                    return Problem(statusCode: 409, title: transition.Title, detail: transition.Detail);

                // The pending invitation already holds a seat, so this only fails when the workspace is over its limit
                // (e.g. invitations sent before limits applied).
                _entitlements?.EnsureCanCreate(invitation.CompanyId, Core.Entitlements.MeteredResource.Seats, count: 0);
                var member = new CompanyMember
                {
                    CompanyId = invitation.CompanyId, UserId = user.Id, Role = invitation.Role,
                    Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow, InvitedByUserId = invitation.InvitedByUserId
                };
                foreach (var id in distinctRoles)
                {
                    member.SecurityRoles.Add(new CompanyMemberSecurityRole
                    {
                        SecurityRoleId = id,
                        GrantOrigin = WorkspaceMemberGrantOrigin.Invitation,
                        GrantedAtUtc = DateTime.UtcNow,
                        GrantedByUserId = invitation.InvitedByUserId
                    });
                }
                _db.CompanyMembers.Add(member);
                invitation.AcceptedAt = DateTime.UtcNow;
                invitation.AcceptedByUserId = user.Id;
                _db.SaveChanges();

                var settings = guard.Settings;
                var activeHumans = _db.CompanyMembers
                    .IgnoreQueryFilters()
                    .Count(m => m.CompanyId == invitation.CompanyId && m.Status == CompanyMemberStatus.Active);
                if (settings.EverTeamActivatedAtUtc == null)
                    settings.EverTeamActivatedAtUtc = DateTime.UtcNow;
                if (activeHumans >= 2)
                {
                    if (settings.CollaborationState is WorkspaceCollaborationState.SoloActive
                        or WorkspaceCollaborationState.TeamSetupPending
                        or WorkspaceCollaborationState.LegacyUnclassified)
                    {
                        settings.CollaborationState = WorkspaceCollaborationState.TeamActive;
                        settings.CollaborationIntent = WorkspaceCollaborationIntent.WithOthers;
                        settings.LastConfirmedAtUtc = DateTime.UtcNow;
                        settings.LastConfirmedByUserId = user.Id;
                    }
                }
                guard.AdvanceRevision();

                WorkspaceCollaborationEvidence.Record(_db, invitation.CompanyId, WorkspaceCollaborationEvidence.EventInvitationAccepted, user.Id, user.Id, new
                {
                    invitationId = invitation.Id,
                    role = invitation.Role.ToString(),
                    securityRoleIds = distinctRoles
                });

                if (distinctRoles.Count > 0 && (!string.IsNullOrWhiteSpace(approvalRequestId) || !string.IsNullOrWhiteSpace(approverUserId)))
                {
                    var consumed = AccessAuthorizationService.ConsumeMatchingApprovalAsync(
                        _db,
                        invitation.CompanyId,
                        inviter.UserId,
                        null,
                        user.Id,
                        invitation.Email,
                        distinctRoles,
                        approvalRequestId: approvalRequestId,
                        approverUserId: approverUserId,
                        operationKey: AccessOperationCatalog.WorkspaceInvitationsCreate).GetAwaiter().GetResult();
                    if (!consumed)
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, ProblemDetailsFactory.CreateProblemDetails(
                            HttpContext,
                            statusCode: StatusCodes.Status403Forbidden,
                            title: "Approval consumption failed",
                            detail: "The independent approval could not be consumed or has expired."));
                    }
                }

                _db.SaveChanges();
                AuthService.InvalidateContextCache();
                return Ok(new { slug = _db.Companies.Where(c => c.Id == invitation.CompanyId).Select(c => c.Slug).Single(), role = member.Role.ToString() });
            });
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
