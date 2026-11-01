#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Api.Authorization;
using Api.Data;
using Api.Filters;
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
    [ApiController]
    [Route("api/organizations/current")]
    public class WorkspaceMembersController : BaseController
    {
        private readonly ApiDbContext _db;
        private readonly ApplicationIdentityDbContext _identity;
        private readonly ITenantContext _tenant;
        private readonly IAccessAuthorizationService _authorizationService;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        private bool IsOwner => _tenant.Role == CompanyMemberRole.Owner;

        private IAccessAuthorizationService AuthService =>
            _authorizationService
            ?? HttpContext?.RequestServices?.GetService<IAccessAuthorizationService>()
            ?? new AccessAuthorizationService(
                _tenant,
                HttpContext?.RequestServices?.GetService<Core.Entitlements.IEntitlementService>(),
                _db,
                new HttpContextAccessor { HttpContext = HttpContext },
                HttpContext?.RequestServices?.GetService<Api.Service.IMfaGate>());

        public WorkspaceMembersController(
            ApiDbContext db,
            ApplicationIdentityDbContext identity,
            ITenantContext tenant,
            IAccessAuthorizationService authorizationService = null)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
            _authorizationService = authorizationService;
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

#nullable enable
        public class UpdateMemberRequest
        {
            public string? Role { get; set; }
            public List<int>? SecurityRoleIds { get; set; }
            public string? ApprovedByUserId { get; set; }
            public string? ApprovalRequestId { get; set; }
        }

        public class CreateAccessApprovalRequest
        {
            public int? TargetMemberId { get; set; }
            public string? TargetUserId { get; set; }
            public string? TargetEmail { get; set; }
            public string? OperationKey { get; set; }
            public string? ResourceId { get; set; }
            public List<int>? SecurityRoleIds { get; set; }
        }
#nullable restore

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPost("members/access-approvals")]
        public async System.Threading.Tasks.Task<IActionResult> CreateAccessApproval([FromBody] CreateAccessApprovalRequest request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Invalid approval request");
            if (request.SecurityRoleIds != null && !ValidSecurityRoles(_db, request.SecurityRoleIds))
                return Problem(statusCode: 400, title: "Invalid workspace security roles");

            var companyId = _tenant.RequireCompanyId();
            var opKey = string.IsNullOrWhiteSpace(request.OperationKey)
                ? AccessOperationCatalog.WorkspaceMembersAssignFunctionalRoles
                : request.OperationKey;

            try
            {
                var (requestId, expiresAt) = await AccessAuthorizationService.RecordAuthenticatedApprovalAsync(
                    _db,
                    companyId,
                    UserId,
                    opKey,
                    request.TargetMemberId,
                    request.TargetUserId,
                    request.TargetEmail,
                    request.SecurityRoleIds,
                    request.ResourceId);

                return StatusCode(StatusCodes.Status201Created, new
                {
                    approvalRequestId = requestId,
                    approverUserId = UserId,
                    expiresAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return Problem(statusCode: 403, title: "Approval denied", detail: ex.Message);
            }
        }

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpDelete("members/access-approvals/{requestId}")]
        public async System.Threading.Tasks.Task<IActionResult> RevokeAccessApproval(string requestId)
        {
            var companyId = _tenant.RequireCompanyId();
            var revoked = await AccessAuthorizationService.RevokeAuthenticatedApprovalAsync(_db, companyId, requestId);
            if (!revoked)
                return NotFound();
            return NoContent();
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
                if (member.UserId == UserId && request.Role != null && role != member.Role)
                    return Problem(statusCode: 403, title: "You cannot change your own workspace role");
                if (request.SecurityRoleIds != null && !ValidSecurityRoles(_db, request.SecurityRoleIds))
                    return Problem(statusCode: 400, title: "Invalid workspace security roles");

                List<int> addedRoleIds = null;
                if (request.SecurityRoleIds != null)
                {
                    var existingRoleIds = member.SecurityRoles.Select(sr => sr.SecurityRoleId).ToHashSet();
                    addedRoleIds = request.SecurityRoleIds.Distinct().Where(id => !existingRoleIds.Contains(id)).ToList();
                    if (addedRoleIds.Count > 0)
                    {
                        var decision = AuthService.Authorize(
                            AccessOperationCatalog.WorkspaceMembersAssignFunctionalRoles,
                            new AccessResourceContext
                            {
                                WorkspaceCompanyId = _tenant.RequireCompanyId(),
                                TargetMemberId = member.Id,
                                TargetUserId = member.UserId,
                                TargetCurrentWorkspaceRole = member.Role,
                                RequestedWorkspaceRole = request.Role != null ? role : (CompanyMemberRole?)null,
                                RequestedSecurityRoleIds = addedRoleIds,
                                IndependentApproverUserId = request.ApprovedByUserId,
                                ApprovalRequestId = request.ApprovalRequestId
                            });
                        if (!decision.Allowed)
                            return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);
                    }
                }

                member.Role = role;
                if (request.SecurityRoleIds != null)
                {
                    _db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
                    member.SecurityRoles = request.SecurityRoleIds.Distinct()
                        .Select(id => new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = id }).ToList();
                }
                _db.SaveChanges();
                if (addedRoleIds != null && addedRoleIds.Count > 0 &&
                    (!string.IsNullOrWhiteSpace(request.ApprovedByUserId) || !string.IsNullOrWhiteSpace(request.ApprovalRequestId)))
                {
                    AccessAuthorizationService.ConsumeMatchingApprovalAsync(
                        _db,
                        _tenant.RequireCompanyId(),
                        UserId,
                        member.Id,
                        member.UserId,
                        null,
                        addedRoleIds,
                        request.ApprovalRequestId,
                        request.ApprovedByUserId).GetAwaiter().GetResult();
                }
                tx.Commit();
                AuthService.InvalidateContextCache();
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
                AuthService.InvalidateContextCache();
                return NoContent();
            }
        }

        public class TransferRequest
        {
            public int MemberId { get; set; }
        }

#nullable enable
        public class CreateWorkspaceAccessRequestModel
        {
            public int? TargetMemberId { get; set; }
            public List<int> SecurityRoleIds { get; set; } = new();
            public string? Reason { get; set; }
        }

        public class ReviewWorkspaceAccessRequestModel
        {
            public string? Reason { get; set; }
        }

        public class SetupWorkspaceAccessModel
        {
            public List<int> SecurityRoleIds { get; set; } = new();
        }
#nullable disable

        [HttpPost("access-requests")]
        public async System.Threading.Tasks.Task<IActionResult> CreateAccessRequest([FromBody] CreateWorkspaceAccessRequestModel model)
        {
            if (model == null || model.SecurityRoleIds == null || model.SecurityRoleIds.Count == 0)
                return Problem(statusCode: 400, title: "Requested functional roles are required.");

            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessRequestsCreate);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            var callerMember = await _db.CompanyMembers
                .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
            if (callerMember == null)
                return Problem(statusCode: 403, title: "You are not an active member of this workspace.");

            CompanyMember targetMember;
            if (model.TargetMemberId.HasValue && model.TargetMemberId.Value > 0)
            {
                targetMember = await _db.CompanyMembers
                    .Include(m => m.SecurityRoles)
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Id == model.TargetMemberId.Value);

                if (targetMember == null || targetMember.Status != CompanyMemberStatus.Active)
                    return Problem(statusCode: 400, title: "Target member not found or inactive.");

                if (targetMember.UserId != callerUserId && callerMember.Role != CompanyMemberRole.Owner && callerMember.Role != CompanyMemberRole.Admin)
                    return Problem(statusCode: 403, title: "Only workspace administrators can request access on behalf of other members.");
            }
            else
            {
                targetMember = callerMember;
            }

            var requestedIds = model.SecurityRoleIds.Distinct().OrderBy(id => id).ToList();
            if (!ValidSecurityRoles(_db, requestedIds))
                return Problem(statusCode: 400, title: "Invalid functional roles requested.");

            var existingRoleIds = targetMember.SecurityRoles?.Select(r => r.SecurityRoleId).ToHashSet() ?? new HashSet<int>();
            var newRoleIds = requestedIds.Where(id => !existingRoleIds.Contains(id)).ToList();
            if (newRoleIds.Count == 0)
                return Problem(statusCode: 400, title: "Target member already possesses all requested functional roles.");

            var now = DateTime.UtcNow;
            var expiresAt = now.AddDays(7);
            var reqId = "acreq_" + Guid.NewGuid().ToString("N");
            var requestedRolesJson = System.Text.Json.JsonSerializer.Serialize(requestedIds);

            // Check for existing pending request with exact same target and roles
            var existingPending = await _db.WorkspaceAccessRequests
                .FirstOrDefaultAsync(r => r.CompanyId == companyId &&
                                          r.TargetMemberId == targetMember.Id &&
                                          r.Status == AccessRequestStatus.Pending &&
                                          r.ExpiresAtUtc > now);
            if (existingPending != null && string.Equals(existingPending.RequestedRoleIdsJson, requestedRolesJson, StringComparison.Ordinal))
            {
                return Ok(new
                {
                    requestId = existingPending.RequestId,
                    requesterUserId = existingPending.RequesterUserId,
                    targetMemberId = existingPending.TargetMemberId,
                    targetUserId = existingPending.TargetUserId,
                    status = existingPending.Status.ToString(),
                    expiresAt = existingPending.ExpiresAtUtc,
                    requiresIndependentApproval = true
                });
            }

            var accessRequest = new WorkspaceAccessRequest
            {
                CompanyId = companyId,
                RequestId = reqId,
                RequesterUserId = callerUserId,
                TargetUserId = targetMember.UserId,
                TargetMemberId = targetMember.Id,
                RequestedRoleIdsJson = requestedRolesJson,
                Reason = model.Reason?.Trim(),
                Status = AccessRequestStatus.Pending,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                PolicyVersion = AccessOperationCatalog.CurrentVersion,
                TargetMembershipRevisionAtRequest = AccessAuthorizationService.GlobalSecurityRevision
            };

            _db.WorkspaceAccessRequests.Add(accessRequest);
            await _db.SaveChangesAsync();

            // Send notification to eligible approvers (active Owner/Admin excluding caller & target)
            var approverMembers = await _db.CompanyMembers
                .Where(m => m.CompanyId == companyId &&
                            (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin) &&
                            m.Status == CompanyMemberStatus.Active &&
                            m.UserId != callerUserId &&
                            m.UserId != targetMember.UserId)
                .ToListAsync();

            if (approverMembers.Count > 0)
            {
                var approverUserIds = approverMembers.Select(m => m.UserId).ToList();
                var approverEmails = await _identity.Users
                    .Where(u => approverUserIds.Contains(u.Id) && !string.IsNullOrEmpty(u.Email))
                    .Select(u => u.Email)
                    .ToListAsync();

                var emailSender = HttpContext?.RequestServices?.GetService<Api.Service.IEmailSender>();
                if (emailSender != null)
                {
                    var targetUser = await _identity.Users.FirstOrDefaultAsync(u => u.Id == targetMember.UserId);
                    var targetDisplay = targetUser?.Email ?? targetMember.UserId;
                    foreach (var email in approverEmails)
                    {
                        try
                        {
                            await emailSender.SendAsync(
                                email,
                                $"[AccountGo] Access request pending review for {targetDisplay}",
                                $"<p>An access request for functional roles has been submitted for <strong>{targetDisplay}</strong> and requires independent approval.</p><p>Reason: {System.Net.WebUtility.HtmlEncode(model.Reason ?? "None provided")}</p>");
                        }
                        catch { }
                    }
                }
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                requestId = accessRequest.RequestId,
                requesterUserId = accessRequest.RequesterUserId,
                targetMemberId = accessRequest.TargetMemberId,
                targetUserId = accessRequest.TargetUserId,
                status = accessRequest.Status.ToString(),
                expiresAt = accessRequest.ExpiresAtUtc,
                requiresIndependentApproval = true
            });
        }

        [HttpGet("access-requests")]
        public async System.Threading.Tasks.Task<IActionResult> ListAccessRequests([FromQuery] string status = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessRequestsView);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            var callerMember = await _db.CompanyMembers
                .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
            if (callerMember == null)
                return Problem(statusCode: 403, title: "You are not an active member of this workspace.");

            var isGovAdmin = callerMember.Role == CompanyMemberRole.Owner || callerMember.Role == CompanyMemberRole.Admin;

            var query = _db.WorkspaceAccessRequests
                .Where(r => r.CompanyId == companyId);

            if (!isGovAdmin)
            {
                query = query.Where(r => r.RequesterUserId == callerUserId || r.TargetUserId == callerUserId);
            }

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AccessRequestStatus>(status, true, out var filterStatus))
            {
                query = query.Where(r => r.Status == filterStatus);
            }

            var requests = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
            var now = DateTime.UtcNow;

            var allRoleIds = requests.SelectMany(r =>
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<List<int>>(r.RequestedRoleIdsJson) ?? new List<int>(); }
                catch { return new List<int>(); }
            }).Distinct().ToList();

            var roleLookup = await _db.SecurityRoles
                .Where(r => allRoleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.DisplayName ?? r.Name);

            var userIds = requests.Select(r => r.RequesterUserId)
                .Concat(requests.Select(r => r.TargetUserId))
                .Concat(requests.Where(r => !string.IsNullOrEmpty(r.ApproverUserId)).Select(r => r.ApproverUserId))
                .Distinct()
                .ToList();

            var userLookup = await _identity.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName);

            var dtos = requests.Select(r =>
            {
                var roleIds = new List<int>();
                try { roleIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(r.RequestedRoleIdsJson) ?? new List<int>(); }
                catch { }

                var roleNames = roleIds.Select(id => roleLookup.TryGetValue(id, out var n) ? n : id.ToString()).ToList();
                var isExpired = r.Status == AccessRequestStatus.Pending && r.ExpiresAtUtc <= now;
                var effectiveStatus = isExpired ? AccessRequestStatus.Expired : r.Status;

                var canApprove = isGovAdmin &&
                                 effectiveStatus == AccessRequestStatus.Pending &&
                                 callerUserId != r.RequesterUserId &&
                                 callerUserId != r.TargetUserId;

                var canCancel = (callerUserId == r.RequesterUserId || callerMember.Role == CompanyMemberRole.Owner) &&
                                effectiveStatus == AccessRequestStatus.Pending;

                return new
                {
                    requestId = r.RequestId,
                    requesterUserId = r.RequesterUserId,
                    requesterEmail = userLookup.TryGetValue(r.RequesterUserId, out var reqEm) ? reqEm : r.RequesterUserId,
                    targetMemberId = r.TargetMemberId,
                    targetUserId = r.TargetUserId,
                    targetEmail = userLookup.TryGetValue(r.TargetUserId, out var tgtEm) ? tgtEm : r.TargetUserId,
                    requestedRoleIds = roleIds,
                    requestedRoleNames = roleNames,
                    reason = r.Reason,
                    status = effectiveStatus.ToString(),
                    approverUserId = r.ApproverUserId,
                    approverEmail = !string.IsNullOrEmpty(r.ApproverUserId) && userLookup.TryGetValue(r.ApproverUserId, out var appEm) ? appEm : r.ApproverUserId,
                    decisionReason = r.DecisionReason,
                    createdAtUtc = r.CreatedAtUtc,
                    expiresAtUtc = r.ExpiresAtUtc,
                    decidedAtUtc = r.DecidedAtUtc,
                    appliedAtUtc = r.AppliedAtUtc,
                    canApprove,
                    canCancel
                };
            }).ToList();

            return Ok(dtos);
        }

        [HttpPost("access-requests/{requestId}/approve")]
        public async System.Threading.Tasks.Task<IActionResult> ApproveAccessRequest(string requestId, [FromBody] ReviewWorkspaceAccessRequestModel model = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessRequestsReview);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var request = await _db.WorkspaceAccessRequests
                    .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RequestId == requestId);

                if (request == null)
                    return NotFound(new { message = "Access request not found." });

                if (request.Status != AccessRequestStatus.Pending)
                    return Problem(statusCode: 400, title: "Invalid request status", detail: $"Request is already {request.Status}.");

                if (DateTime.UtcNow > request.ExpiresAtUtc)
                {
                    request.Status = AccessRequestStatus.Expired;
                    await _db.SaveChangesAsync();
                    tx.Commit();
                    return Problem(statusCode: 400, title: "Request expired", detail: "This access request has expired.");
                }

                // Two-person rule / anti-self-grant
                if (string.Equals(callerUserId, request.RequesterUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-approval denied", detail: "The requester cannot approve their own access request.");

                if (string.Equals(callerUserId, request.TargetUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-approval denied", detail: "An approver cannot approve their own role elevation.");

                var approverMember = await _db.CompanyMembers
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
                if (approverMember == null || (approverMember.Role != CompanyMemberRole.Owner && approverMember.Role != CompanyMemberRole.Admin))
                    return Problem(statusCode: 403, title: "Approver unauthorized", detail: "Approver must be an active Owner or Admin in this workspace.");

                var targetMember = await _db.CompanyMembers
                    .Include(m => m.SecurityRoles)
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Id == request.TargetMemberId);

                if (targetMember == null || targetMember.Status != CompanyMemberStatus.Active)
                    return Problem(statusCode: 400, title: "Target member not found or inactive.");

                List<int> requestedRoleIds;
                try
                {
                    requestedRoleIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(request.RequestedRoleIdsJson) ?? new List<int>();
                }
                catch
                {
                    return Problem(statusCode: 400, title: "Malformed requested roles payload.");
                }

                if (!ValidSecurityRoles(_db, requestedRoleIds))
                    return Problem(statusCode: 400, title: "Requested security roles are no longer valid.");

                // Atomically update target member roles
                var existingRoleIds = targetMember.SecurityRoles.Select(sr => sr.SecurityRoleId).ToHashSet();
                foreach (var roleId in requestedRoleIds)
                {
                    if (!existingRoleIds.Contains(roleId))
                    {
                        var link = new CompanyMemberSecurityRole
                        {
                            CompanyMemberId = targetMember.Id,
                            SecurityRoleId = roleId
                        };
                        targetMember.SecurityRoles.Add(link);
                        _db.CompanyMemberSecurityRoles.Add(link);
                    }
                }

                var now = DateTime.UtcNow;
                request.Status = AccessRequestStatus.Applied;
                request.ApproverUserId = callerUserId;
                request.DecisionReason = model?.Reason?.Trim();
                request.DecidedAtUtc = now;
                request.AppliedAtUtc = now;

                // Record audit log entry for the approved delegation
                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = callerUserId,
                    AuditEventDateUTC = now,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Modified,
                    TableName = "__AccessDelegationApproval",
                    RecordId = request.RequestId,
                    FieldName = "Status",
                    OriginalValue = "Pending",
                    NewValue = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        requestId = request.RequestId,
                        companyId = companyId,
                        approverUserId = callerUserId,
                        targetUserId = targetMember.UserId,
                        targetMemberId = targetMember.Id,
                        securityRoleIds = requestedRoleIds,
                        status = "Applied",
                        appliedAt = now
                    })
                });

                await _db.SaveChangesAsync();
                tx.Commit();

                AccessAuthorizationService.BumpGlobalSecurityRevision();
                AuthService.InvalidateContextCache();

                // Send notification email to requester/target
                var emailSender = HttpContext?.RequestServices?.GetService<Api.Service.IEmailSender>();
                if (emailSender != null)
                {
                    var targetUser = await _identity.Users.FirstOrDefaultAsync(u => u.Id == targetMember.UserId);
                    if (targetUser != null && !string.IsNullOrEmpty(targetUser.Email))
                    {
                        try
                        {
                            await emailSender.SendAsync(
                                targetUser.Email,
                                "[AccountGo] Access request approved and applied",
                                $"<p>Your access request for workspace functional roles has been approved and applied to your account.</p>");
                        }
                        catch { }
                    }
                }

                return Ok(new
                {
                    status = "Applied",
                    requestId = request.RequestId,
                    targetMemberId = targetMember.Id,
                    appliedAt = request.AppliedAtUtc
                });
            }
        }

        [HttpPost("access-requests/{requestId}/reject")]
        public async System.Threading.Tasks.Task<IActionResult> RejectAccessRequest(string requestId, [FromBody] ReviewWorkspaceAccessRequestModel model = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessRequestsReview);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var request = await _db.WorkspaceAccessRequests
                    .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RequestId == requestId);

                if (request == null)
                    return NotFound(new { message = "Access request not found." });

                if (request.Status != AccessRequestStatus.Pending)
                    return Problem(statusCode: 400, title: "Invalid request status", detail: $"Request is already {request.Status}.");

                if (string.Equals(callerUserId, request.RequesterUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-review denied", detail: "Use cancel to withdraw your own access request.");

                var approverMember = await _db.CompanyMembers
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
                if (approverMember == null || (approverMember.Role != CompanyMemberRole.Owner && approverMember.Role != CompanyMemberRole.Admin))
                    return Problem(statusCode: 403, title: "Approver unauthorized", detail: "Approver must be an active Owner or Admin in this workspace.");

                var now = DateTime.UtcNow;
                request.Status = AccessRequestStatus.Rejected;
                request.ApproverUserId = callerUserId;
                request.DecisionReason = model?.Reason?.Trim();
                request.DecidedAtUtc = now;

                await _db.SaveChangesAsync();
                tx.Commit();

                return Ok(new
                {
                    status = "Rejected",
                    requestId = request.RequestId,
                    decidedAt = request.DecidedAtUtc
                });
            }
        }

        [HttpPost("access-requests/{requestId}/cancel")]
        public async System.Threading.Tasks.Task<IActionResult> CancelAccessRequest(string requestId)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessRequestsCancel);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var request = await _db.WorkspaceAccessRequests
                    .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RequestId == requestId);

                if (request == null)
                    return NotFound(new { message = "Access request not found." });

                if (request.Status != AccessRequestStatus.Pending)
                    return Problem(statusCode: 400, title: "Invalid request status", detail: $"Request is already {request.Status}.");

                var callerMember = await _db.CompanyMembers
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
                if (callerMember == null)
                    return Problem(statusCode: 403, title: "You are not an active member of this workspace.");

                var isOwner = callerMember.Role == CompanyMemberRole.Owner;
                if (!isOwner && !string.Equals(callerUserId, request.RequesterUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Unauthorized", detail: "Only the requester or workspace Owner can cancel a pending access request.");

                var now = DateTime.UtcNow;
                request.Status = AccessRequestStatus.Cancelled;
                request.DecidedAtUtc = now;

                await _db.SaveChangesAsync();
                tx.Commit();

                return Ok(new
                {
                    status = "Cancelled",
                    requestId = request.RequestId,
                    cancelledAt = request.DecidedAtUtc
                });
            }
        }

        [HttpGet("access-setup/status")]
        public async System.Threading.Tasks.Task<IActionResult> GetAccessSetupStatus()
        {
            var companyId = _tenant.RequireCompanyId();
            var alreadyBootstrapped = await _db.AuditLogs
                .AnyAsync(a => a.CompanyId == companyId && a.TableName == "__AccessBootstrap" && a.RecordId == "bootstrap");

            var adminCount = await _db.CompanyMembers
                .CountAsync(m => m.CompanyId == companyId &&
                                 (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin) &&
                                 m.Status == CompanyMemberStatus.Active);

            var callerMember = await _db.CompanyMembers
                .Include(m => m.SecurityRoles)
                .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == UserId);

            var totalFunctionalRoles = await _db.CompanyMemberSecurityRoles
                .CountAsync(sr => sr.CompanyMember.CompanyId == companyId);

            var eligible = !alreadyBootstrapped && adminCount <= 1 && callerMember?.Role == CompanyMemberRole.Owner;

            return Ok(new
            {
                alreadyBootstrapped,
                adminCount,
                totalFunctionalRoles,
                isEligibleForBootstrap = eligible
            });
        }

        [HttpPost("access-setup")]
        public async System.Threading.Tasks.Task<IActionResult> AccessSetup([FromBody] SetupWorkspaceAccessModel model = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessSetup);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var callerMember = await _db.CompanyMembers
                    .Include(m => m.SecurityRoles)
                    .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);

                if (callerMember == null || callerMember.Role != CompanyMemberRole.Owner)
                    return Problem(statusCode: 403, title: "Forbidden", detail: "Only a workspace Owner can perform initial access setup.");

                // AC7: Permanent boundary check: Has bootstrap ever been completed for this company?
                var alreadyBootstrapped = await _db.AuditLogs
                    .AnyAsync(a => a.CompanyId == companyId && a.TableName == "__AccessBootstrap" && a.RecordId == "bootstrap");
                if (alreadyBootstrapped)
                {
                    return Problem(statusCode: 403, title: "Bootstrap unavailable",
                        detail: "Initial access setup has already been completed for this workspace and cannot be reopened. New functional roles require independent approval.");
                }

                // Check administrator count: If there are multiple active administrators, two-person rule must be used!
                var adminCount = await _db.CompanyMembers
                    .CountAsync(m => m.CompanyId == companyId &&
                                     (m.Role == CompanyMemberRole.Owner || m.Role == CompanyMemberRole.Admin) &&
                                     m.Status == CompanyMemberStatus.Active);

                if (adminCount > 1)
                {
                    return Problem(statusCode: 403, title: "Bootstrap unavailable",
                        detail: "Workspaces with multiple administrators must use the independent approval request workflow.");
                }

                var roleIdsToAssign = model?.SecurityRoleIds?.Distinct().ToList() ?? new List<int>();
                if (roleIdsToAssign.Count == 0)
                {
                    // Default to Accountant if none specified
                    var defaultRole = await _db.SecurityRoles.FirstOrDefaultAsync(r => r.Name == "Accountant");
                    if (defaultRole != null)
                        roleIdsToAssign.Add(defaultRole.Id);
                }

                if (!ValidSecurityRoles(_db, roleIdsToAssign))
                    return Problem(statusCode: 400, title: "Invalid functional roles.");

                foreach (var roleId in roleIdsToAssign)
                {
                    if (!callerMember.SecurityRoles.Any(sr => sr.SecurityRoleId == roleId))
                    {
                        var link = new CompanyMemberSecurityRole
                        {
                            CompanyMemberId = callerMember.Id,
                            SecurityRoleId = roleId
                        };
                        callerMember.SecurityRoles.Add(link);
                        _db.CompanyMemberSecurityRoles.Add(link);
                    }
                }

                // Audit log
                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = callerUserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                    TableName = "__AccessBootstrap",
                    RecordId = "bootstrap",
                    FieldName = "Status",
                    OriginalValue = "None",
                    NewValue = "Completed"
                });

                await _db.SaveChangesAsync();
                tx.Commit();

                AccessAuthorizationService.BumpGlobalSecurityRevision();
                AuthService.InvalidateContextCache();

                return Ok(new
                {
                    message = "Initial access setup completed successfully.",
                    rolesAssigned = roleIdsToAssign
                });
            }
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
                AuthService.InvalidateContextCache();
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
