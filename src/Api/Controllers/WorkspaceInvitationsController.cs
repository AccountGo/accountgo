#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Authorization;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        private readonly Core.Entitlements.IEntitlementService _entitlements;
        private readonly IAccessAuthorizationService _authorizationService;
        private readonly IWorkspaceApprovalPolicyResolver _approvalResolver;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        private IAccessAuthorizationService AuthService =>
            _authorizationService
            ?? HttpContext?.RequestServices?.GetService<IAccessAuthorizationService>()
            ?? new AccessAuthorizationService(
                _tenant,
                _entitlements ?? HttpContext?.RequestServices?.GetService<Core.Entitlements.IEntitlementService>(),
                _db,
                new HttpContextAccessor { HttpContext = HttpContext },
                HttpContext?.RequestServices?.GetService<Api.Service.IMfaGate>());

        public WorkspaceInvitationsController(ApiDbContext db, ApplicationIdentityDbContext identity, ITenantContext tenant, WorkspaceInvitationService invitations,
            Core.Entitlements.IEntitlementService entitlements = null, IAccessAuthorizationService authorizationService = null,
            IWorkspaceApprovalPolicyResolver approvalResolver = null)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
            _invitations = invitations;
            _entitlements = entitlements;
            _authorizationService = authorizationService;
            _approvalResolver = approvalResolver;
        }

        /// <summary>How long an expired, unaccepted invitation stays visible so it can be resent or revoked.</summary>
        public const int ExpiredInvitationVisibilityDays = 30;

        private IActionResult ReasonProblem(int status, string title, string detail, string reasonCode, long? policyRevision = null)
        {
            var problem = new ProblemDetails { Title = title, Detail = detail, Status = status };
            problem.Extensions["reasonCode"] = reasonCode;
            if (policyRevision.HasValue)
                problem.Extensions["policyRevision"] = policyRevision.Value;
            return StatusCode(status, problem);
        }

        [HttpGet]
        public IActionResult List()
        {
            var companyId = _tenant.RequireCompanyId();
            var now = DateTime.UtcNow;
            var oldestVisibleExpiry = now.AddDays(-ExpiredInvitationVisibilityDays);
            var currentSettings = _db.WorkspaceOperatingSettings.AsNoTracking().SingleOrDefault(s => s.CompanyId == companyId);
            var journalRule = _db.WorkspaceApprovalRules.AsNoTracking()
                .SingleOrDefault(r => r.CompanyId == companyId && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
            var currentJournalMode = _approvalResolver?.Resolve(
                currentSettings ?? new WorkspaceOperatingSettings { CompanyId = companyId },
                journalRule,
                AccessOperationCatalog.FinancialsJournalPost).Mode.ToString();

            return Ok(_db.CompanyInvitations
                .Where(i => i.CompanyId == companyId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > oldestVisibleExpiry)
                .OrderBy(i => i.ExpiresAt)
                .ToList()
                .Select(i =>
                {
                    var issued = WorkspaceInvitationTransition.GetLatest(_db, companyId, i.Id);
                    // Stale: the journal approval policy the invitee was told about has since changed.
                    var policyChanged = currentJournalMode != null
                        && issued != null
                        && !string.IsNullOrWhiteSpace(issued.IntendedJournalMode)
                        && !string.Equals(issued.IntendedJournalMode, currentJournalMode, StringComparison.Ordinal);
                    return new
                    {
                        id = i.Id,
                        email = i.Email,
                        role = i.Role.ToString(),
                        expiresAt = i.ExpiresAt,
                        expired = i.ExpiresAt <= now,
                        policyChanged,
                        securityRoleIds = JsonSerializer.Deserialize<List<int>>(i.SecurityRoleIdsJson)
                    };
                }));
        }

        public class InviteRequest
        {
            [Required, EmailAddress]
            public string Email { get; set; }
            public string Role { get; set; } = "Member";
            public List<int> SecurityRoleIds { get; set; } = new List<int>();
#nullable enable
            public string? ApprovedByUserId { get; set; }
            public string? ApprovalRequestId { get; set; }
#nullable restore
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
                if (user != null && user.Id == UserId)
                    return Problem(statusCode: 403, title: "Self-grant denied", detail: "You cannot invite yourself to elevate roles.");
                if (user != null && user.DeletedAt != null)
                    return Problem(statusCode: 409, title: AccountDeletionService.ClosedEmailTitle);
                if (user != null && _db.CompanyMembers.Any(m => m.CompanyId == companyId && m.UserId == user.Id))
                    return Problem(statusCode: 409, title: "User is already a workspace member");
                if (request.SecurityRoleIds.Count > 0)
                {
                    var decision = AuthService.Authorize(
                        AccessOperationCatalog.WorkspaceInvitationsCreate,
                        new AccessResourceContext
                        {
                            WorkspaceCompanyId = companyId,
                            TargetUserId = user?.Id,
                            TargetEmail = email,
                            RequestedWorkspaceRole = role,
                            RequestedSecurityRoleIds = request.SecurityRoleIds.Distinct().ToList(),
                            IndependentApproverUserId = request.ApprovedByUserId,
                            ApprovalRequestId = request.ApprovalRequestId
                        });
                    if (!decision.Allowed)
                        return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);
                }
                var hourAgo = DateTime.UtcNow.AddHours(-1);
                if (_db.CompanyInvitations.Count(i => i.CompanyId == companyId && i.CreatedAt > hourAgo) >= 20)
                    return Problem(statusCode: 429, title: "Workspace invitation limit reached", detail: "Limit: 20 invitations per hour.");
                var pending = _db.CompanyInvitations.Where(i => i.CompanyId == companyId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null).ToList();
                // Admins cannot replace an Owner's Admin invitation with a different role.
                if (_tenant.Role != CompanyMemberRole.Owner && pending.Any(i => i.Role == CompanyMemberRole.Admin))
                    return Problem(statusCode: 403, title: "Only Owners can manage Admin invitations");
                // Re-inviting the same address replaces its pending invitation, so it takes no extra seat.
                _entitlements?.EnsureCanCreate(companyId, Core.Entitlements.MeteredResource.Seats,
                    count: pending.Any(i => i.ExpiresAt > DateTime.UtcNow) ? 0 : 1);
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

                var operating = _db.WorkspaceOperatingSettings.AsNoTracking()
                    .FirstOrDefault(s => s.CompanyId == companyId);
                var journalMode = _db.WorkspaceApprovalRules.AsNoTracking()
                    .Where(r => r.CompanyId == companyId
                                && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost)
                    .Select(r => (WorkspaceApprovalRuleKind?)r.RuleKind)
                    .FirstOrDefault();
                WorkspaceInvitationTransition.Record(
                    _db,
                    companyId,
                    invitation.Id,
                    UserId,
                    role,
                    request.SecurityRoleIds.Distinct().ToList(),
                    issuingPolicyRevision: operating?.PolicyRevision ?? 0,
                    intendedJournalMode: journalMode?.ToString());

                if (!string.IsNullOrWhiteSpace(request.ApprovalRequestId))
                {
                    _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                    {
                        CompanyId = companyId,
                        UserName = request.ApprovedByUserId ?? UserId,
                        AuditEventDateUTC = DateTime.UtcNow,
                        AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                        TableName = "__InvitationApproval",
                        RecordId = invitation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        FieldName = "ApprovalRequestId",
                        OriginalValue = request.ApprovedByUserId ?? string.Empty,
                        NewValue = request.ApprovalRequestId
                    });
                }

                await _db.SaveChangesAsync();

                tx.Commit();
                return StatusCode(201, new
                {
                    id = invitation.Id,
                    email,
                    role = role.ToString(),
                    expiresAt = invitation.ExpiresAt,
                    policyRevision = operating?.PolicyRevision ?? 0,
                    grantFingerprint = WorkspaceInvitationTransition.ComputeGrantFingerprint(role, request.SecurityRoleIds)
                });
            }
        }

        public sealed class ResendInvitationRequest
        {
            public bool ConfirmChangedPolicy { get; set; }
            public long? ExpectedPolicyRevision { get; set; }
        }

        [HttpPost("{id:int}/resend")]
        public async Task<IActionResult> Resend(int id, [FromBody] ResendInvitationRequest? request = null)
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

                var companyId = _tenant.RequireCompanyId();
                var securityRoleIds = string.IsNullOrWhiteSpace(invitation.SecurityRoleIdsJson)
                    ? new List<int>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<int>>(invitation.SecurityRoleIdsJson) ?? new List<int>();

                if (!WorkspaceMembersController.ValidSecurityRoles(_db, securityRoleIds))
                    return Problem(statusCode: 400, title: "Invalid workspace security roles", detail: "Assigned security roles are no longer valid.");

                var currentSettings = _db.WorkspaceOperatingSettings.SingleOrDefault(s => s.CompanyId == companyId);
                var journalRule = _db.WorkspaceApprovalRules.SingleOrDefault(r => r.CompanyId == companyId && r.OperationKey == AccessOperationCatalog.FinancialsJournalPost);
                var effectiveRule = _approvalResolver?.Resolve(currentSettings ?? new WorkspaceOperatingSettings { CompanyId = companyId }, journalRule, AccessOperationCatalog.FinancialsJournalPost);

                var currentJournalMode = effectiveRule?.Mode.ToString();
                var currentRevision = currentSettings?.PolicyRevision ?? 0;
                if (request?.ExpectedPolicyRevision.HasValue == true && request.ExpectedPolicyRevision.Value != currentRevision)
                {
                    return ReasonProblem(
                        StatusCodes.Status409Conflict,
                        "Workspace policy revision mismatch",
                        $"The provided expected policy revision {request.ExpectedPolicyRevision.Value} does not match the current revision {currentRevision}.",
                        WorkspacePolicyReasonCodes.StalePolicyRevision,
                        currentRevision);
                }

                var latestTransition = WorkspaceInvitationTransition.GetLatest(_db, companyId, invitation.Id);
                if (latestTransition != null && !string.IsNullOrWhiteSpace(latestTransition.IntendedJournalMode))
                {
                    var modeChanged = !string.Equals(latestTransition.IntendedJournalMode, currentJournalMode, StringComparison.Ordinal);
                    if (modeChanged)
                    {
                        if (request?.ConfirmChangedPolicy != true)
                        {
                            return ReasonProblem(
                                StatusCodes.Status409Conflict,
                                "Workspace policy assumptions changed",
                                $"Workspace journal approval policy changed from {latestTransition.IntendedJournalMode} to {currentJournalMode} since this invitation was issued. Explicit reconfirmation is required to resend.",
                                WorkspacePolicyReasonCodes.PolicyAssumptionsChanged,
                                currentRevision);
                        }

                        if (!request.ExpectedPolicyRevision.HasValue || request.ExpectedPolicyRevision.Value != currentRevision)
                        {
                            return ReasonProblem(
                                StatusCodes.Status409Conflict,
                                "Workspace policy revision mismatch",
                                $"The expected policy revision must be specified and match current revision {currentRevision} to confirm changed policy.",
                                WorkspacePolicyReasonCodes.StalePolicyRevision,
                                currentRevision);
                        }
                    }
                }

                WorkspaceInvitationTransition.Record(
                    _db,
                    companyId,
                    invitation.Id,
                    UserId,
                    invitation.Role,
                    securityRoleIds,
                    issuingPolicyRevision: currentSettings?.PolicyRevision ?? 0,
                    intendedJournalMode: currentJournalMode);

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
            return _invitations.SendAsync(invitation, company, name, email);
        }
    }
}
