#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Api.Authorization;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Entitlements;
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
        private readonly IWorkspaceOperatingPolicyService _operatingPolicy;
        private readonly IWorkspacePolicyTransaction _policyTransaction;
        private readonly IWorkspaceReviewerEligibility _reviewerEligibility;
        private readonly IEntitlementService _entitlements;
        private readonly LinkedEntryService _linkedEntries;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        private bool IsOwner => _tenant.Role == CompanyMemberRole.Owner;

        private IAccessAuthorizationService AuthService =>
            _authorizationService
            ?? HttpContext?.RequestServices?.GetService<IAccessAuthorizationService>()
            ?? new AccessAuthorizationService(
                _tenant,
                HttpContext?.RequestServices?.GetService<IEntitlementService>(),
                _db,
                new HttpContextAccessor { HttpContext = HttpContext },
                HttpContext?.RequestServices?.GetService<Api.Service.IMfaGate>());

        public WorkspaceMembersController(
            ApiDbContext db,
            ApplicationIdentityDbContext identity,
            ITenantContext tenant,
            IAccessAuthorizationService authorizationService = null,
            IWorkspaceOperatingPolicyService operatingPolicy = null,
            IWorkspacePolicyTransaction policyTransaction = null,
            IWorkspaceReviewerEligibility reviewerEligibility = null,
            IEntitlementService entitlements = null,
            LinkedEntryService linkedEntries = null)
        {
            _db = db;
            _identity = identity;
            _tenant = tenant;
            _authorizationService = authorizationService;
            _operatingPolicy = operatingPolicy;
            _policyTransaction = policyTransaction;
            _reviewerEligibility = reviewerEligibility;
            _entitlements = entitlements;
            _linkedEntries = linkedEntries;
        }

        private IWorkspaceReviewerEligibility ReviewerEligibility =>
            _reviewerEligibility
            ?? HttpContext?.RequestServices?.GetService<IWorkspaceReviewerEligibility>()
            ?? new WorkspaceReviewerEligibility(_db, HttpContext?.RequestServices?.GetService<Core.Entitlements.IEntitlementService>());

        private IWorkspaceOperatingPolicyService OperatingPolicy =>
            _operatingPolicy ?? HttpContext?.RequestServices?.GetService<IWorkspaceOperatingPolicyService>();

        private IActionResult ReasonProblem(int status, string title, string detail, string reasonCode, long? policyRevision = null)
        {
            var problem = new ProblemDetails { Title = title, Detail = detail, Status = status };
            problem.Extensions["reasonCode"] = reasonCode;
            if (policyRevision.HasValue)
                problem.Extensions["policyRevision"] = policyRevision.Value;
            return StatusCode(status, problem);
        }

        /// <summary>
        /// Rejects an ordinary member change that would leave independent journal review without a usable
        /// reviewer. The only way through in the same request is an Owner-authorized, revision-checked change
        /// to single-actor posting; approval is never relaxed implicitly. Returns null when the change may proceed.
        /// </summary>
        private IActionResult GuardIndependentReviewer(
            WorkspacePolicyGuard guard,
            MemberReviewerChange change,
            bool confirmPolicyChangeToSingleActor,
            long? expectedPolicyRevision)
        {
            var companyId = _tenant.RequireCompanyId();
            var verdict = ReviewerEligibility.EvaluateMemberChange(companyId, change);
            if (!verdict.Blocked)
                return null;

            if (!confirmPolicyChangeToSingleActor)
                return ReasonProblem(409, "Independent reviewer required", verdict.Message, verdict.ReasonCode, guard.PolicyRevision);

            if (!IsOwner)
                return ReasonProblem(403, "Only an Owner can change the journal approval policy",
                    "This change needs a policy change to single-actor posting, which only an Owner can authorize.",
                    WorkspacePolicyReasonCodes.OwnerAuthorizationRequired, guard.PolicyRevision);

            if (!expectedPolicyRevision.HasValue)
                return ReasonProblem(409, "Expected policy revision required",
                    "Confirming a policy change requires the current expected policy revision.",
                    WorkspacePolicyReasonCodes.StalePolicyRevision, guard.PolicyRevision);

            var operatingPolicy = OperatingPolicy;
            if (operatingPolicy == null)
                return Problem(statusCode: 503, title: "Operating policy unavailable");

            var result = operatingPolicy.ChangeJournalApprovalPolicy(
                guard, UserId, expectedPolicyRevision.Value, WorkspaceApprovalRuleKind.SingleActorAllowed);
            if (result.Applied)
                return null;

            var status = result.ReasonCode == WorkspacePolicyReasonCodes.StalePolicyRevision ? 409 : 403;
            return ReasonProblem(status, "Approval policy change not applied", result.Message, result.ReasonCode, result.PolicyRevision);
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

            /// <summary>Owner-only: authorize a same-request change to single-actor journal posting.</summary>
            public bool ConfirmPolicyChangeToSingleActor { get; set; }
            public long? ExpectedPolicyRevision { get; set; }
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

            if (string.Equals(opKey, AccessOperationCatalog.FinancialsJournalPost, StringComparison.Ordinal))
            {
                var journalProblem = ValidateJournalReviewApproval(companyId, request.ResourceId);
                if (journalProblem != null)
                    return journalProblem;
            }

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

        /// <summary>
        /// A journal-post review approval must name an existing unposted draft, come from a reviewer who can
        /// actually post journals (permission + entitlement aware), and never from the entry's own preparer.
        /// </summary>
        private IActionResult ValidateJournalReviewApproval(int companyId, string resourceId)
        {
            if (!int.TryParse(resourceId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var journalId))
                return Problem(statusCode: 400, title: "Journal entry required", detail: "A journal review approval must reference the journal entry id.");

            var header = _db.JournalEntryHeaders.AsNoTracking()
                .FirstOrDefault(h => h.CompanyId == companyId && h.Id == journalId);
            if (header == null || header.Posted == true || header.GeneralLedgerHeaderId.HasValue)
                return Problem(statusCode: 400, title: "Unposted journal draft required", detail: "Only an existing, unposted journal draft in this workspace can be approved for posting.");

            var isEligibleReviewer = ReviewerEligibility.GetEligibleReviewers(companyId)
                .Any(r => string.Equals(r.UserId, UserId, StringComparison.Ordinal));
            if (!isEligibleReviewer)
                return ReasonProblem(403, "Reviewer not eligible",
                    "Only an active Owner or Admin who can post journals in this workspace can review a journal entry.",
                    WorkspacePolicyReasonCodes.ReviewerRequired);

            var journalKey = journalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var preparer = _db.AuditLogs.AsNoTracking()
                .Where(a => a.CompanyId == companyId && a.TableName == "__JournalEntryPreparer" && a.RecordId == journalKey)
                .OrderByDescending(a => a.Id)
                .Select(a => a.NewValue)
                .FirstOrDefault();
            if (string.Equals(preparer, UserId, StringComparison.Ordinal))
                return Problem(statusCode: 403, title: "Approval denied", detail: "The person who prepared a journal entry cannot review it.");

            return null;
        }

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpDelete("members/access-approvals/{requestId}")]
        public async System.Threading.Tasks.Task<IActionResult> RevokeAccessApproval(string requestId)
        {
            var companyId = _tenant.RequireCompanyId();
            var approvalJson = _db.AuditLogs.AsNoTracking()
                .Where(a => a.CompanyId == companyId && a.TableName == "__AccessDelegationApproval" && a.RecordId == requestId)
                .Select(a => a.NewValue).FirstOrDefault();
            var revoked = await AccessAuthorizationService.RevokeAuthenticatedApprovalAsync(_db, companyId, requestId);
            if (!revoked)
                return NotFound();
            if (_linkedEntries != null && !string.IsNullOrWhiteSpace(approvalJson))
            {
                var approval = System.Text.Json.JsonSerializer.Deserialize<PersistedAccessApprovalPayload>(approvalJson);
                if (approval?.OperationKey == AccessOperationCatalog.FinancialsJournalPost &&
                    int.TryParse(approval.ResourceId, out var journalId))
                    _linkedEntries.CancelPendingSource(companyId, journalId);
            }
            return NoContent();
        }

        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("members/{memberId:int}")]
        public IActionResult Update(int memberId, [FromBody] UpdateMemberRequest request)
        {
            var companyId = _tenant.RequireCompanyId();
            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Policy service unavailable", detail: "Workspace policy coordination is unavailable.");

            return transaction.Execute<IActionResult>(companyId, guard =>
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
                                WorkspaceCompanyId = companyId,
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

                var changesReviewerCapacity = role != member.Role || request.SecurityRoleIds != null;
                if (changesReviewerCapacity)
                {
                    var blocked = GuardIndependentReviewer(
                        guard,
                        new MemberReviewerChange(
                            member.Id,
                            Removed: false,
                            NewRole: role,
                            NewSecurityRoleIds: request.SecurityRoleIds?.Distinct().ToList()),
                        request.ConfirmPolicyChangeToSingleActor,
                        request.ExpectedPolicyRevision);
                    if (blocked != null)
                        return blocked;
                }

                member.Role = role;
                if (request.SecurityRoleIds != null)
                {
                    _db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
                    member.SecurityRoles = request.SecurityRoleIds.Distinct()
                        .Select(id => new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = id }).ToList();
                }

                guard.AdvanceRevision();
                WorkspaceCollaborationEvidence.Record(_db, companyId, WorkspaceCollaborationEvidence.EventMemberRoleUpdated, UserId, member.UserId, new
                {
                    newRole = member.Role.ToString(),
                    securityRoleIds = request.SecurityRoleIds
                });

                if (addedRoleIds != null && addedRoleIds.Count > 0 &&
                    (!string.IsNullOrWhiteSpace(request.ApprovedByUserId) || !string.IsNullOrWhiteSpace(request.ApprovalRequestId)))
                {
                    AccessAuthorizationService.ConsumeMatchingApprovalAsync(
                        _db,
                        companyId,
                        UserId,
                        member.Id,
                        member.UserId,
                        null,
                        addedRoleIds,
                        request.ApprovalRequestId,
                        request.ApprovedByUserId).GetAwaiter().GetResult();
                }

                _db.SaveChanges();
                AuthService.InvalidateContextCache();
                return NoContent();
            });
        }

        [HttpDelete("members/{memberId:int}")]
        public IActionResult Remove(
            int memberId,
            [FromQuery] bool confirmPolicyChangeToSingleActor = false,
            [FromQuery] long? expectedPolicyRevision = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Policy service unavailable", detail: "Workspace policy coordination is unavailable.");

            return transaction.Execute<IActionResult>(companyId, guard =>
            {
                var member = FindMember(memberId);
                if (member == null)
                    return NotFound();
                var self = member.UserId == UserId;
                if (!self && (_tenant.Role == CompanyMemberRole.Member || (!IsOwner && member.Role != CompanyMemberRole.Member)))
                    return Problem(statusCode: 403, title: "You cannot remove this member");
                if (member.Role == CompanyMemberRole.Owner && IsLastOwner())
                    return Problem(statusCode: 409, title: "The last Owner cannot leave or be removed");

                var blocked = GuardIndependentReviewer(
                    guard,
                    new MemberReviewerChange(member.Id, Removed: true),
                    confirmPolicyChangeToSingleActor,
                    expectedPolicyRevision);
                if (blocked != null)
                    return blocked;

                guard.Settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                if (guard.Settings.CollaborationState == WorkspaceCollaborationState.LegacyUnclassified)
                    guard.Settings.CollaborationState = WorkspaceCollaborationState.TeamActive;

                WorkspaceCollaborationEvidence.Record(_db, companyId, WorkspaceCollaborationEvidence.EventMemberRemoved, UserId, member.UserId, new { role = member.Role.ToString() });
                guard.AdvanceRevision();

                _db.CompanyMemberSecurityRoles.RemoveRange(member.SecurityRoles);
                _db.CompanyMembers.Remove(member);
                _db.SaveChanges();
                AuthService.InvalidateContextCache();
                return NoContent();
            });
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

            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Workspace policy coordination is unavailable.");

            CompanyMember? targetMemberRef = null;
            var result = transaction.Execute<IActionResult>(companyId, guard =>
            {
                var request = _db.WorkspaceAccessRequests
                    .FirstOrDefault(r => r.CompanyId == companyId && r.RequestId == requestId);

                if (request == null)
                    return NotFound(new { message = "Access request not found." });

                if (request.Status != AccessRequestStatus.Pending)
                    return Problem(statusCode: 400, title: "Invalid request status", detail: $"Request is already {request.Status}.");

                if (DateTime.UtcNow > request.ExpiresAtUtc)
                {
                    request.Status = AccessRequestStatus.Expired;
                    _db.SaveChanges();
                    return Problem(statusCode: 400, title: "Request expired", detail: "This access request has expired.");
                }

                // Two-person rule / anti-self-grant
                if (string.Equals(callerUserId, request.RequesterUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-approval denied", detail: "The requester cannot approve their own access request.");

                if (string.Equals(callerUserId, request.TargetUserId, StringComparison.Ordinal))
                    return Problem(statusCode: 403, title: "Self-approval denied", detail: "An approver cannot approve their own role elevation.");

                var approverMember = _db.CompanyMembers
                    .FirstOrDefault(m => m.CompanyId == companyId && m.UserId == callerUserId && m.Status == CompanyMemberStatus.Active);
                if (approverMember == null || (approverMember.Role != CompanyMemberRole.Owner && approverMember.Role != CompanyMemberRole.Admin))
                    return Problem(statusCode: 403, title: "Approver unauthorized", detail: "Approver must be an active Owner or Admin in this workspace.");

                var targetMember = _db.CompanyMembers
                    .Include(m => m.SecurityRoles)
                    .FirstOrDefault(m => m.CompanyId == companyId && m.Id == request.TargetMemberId);

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

                guard.AdvanceRevision();

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

                _db.SaveChanges();
                targetMemberRef = targetMember;
                return Ok(new { message = "Access request approved and functional roles applied successfully.", status = "Applied" });
            });

            if (result is not OkObjectResult)
                return result;

            AccessAuthorizationService.BumpGlobalSecurityRevision();
            AuthService.InvalidateContextCache();

            // Send notification email to requester/target outside the lock
            var emailSender = HttpContext?.RequestServices?.GetService<Api.Service.IEmailSender>();
            if (emailSender != null && targetMemberRef != null)
            {
                var targetUser = await _identity.Users.FirstOrDefaultAsync(u => u.Id == targetMemberRef.UserId);
                if (targetUser != null && !string.IsNullOrEmpty(targetUser.Email))
                {
                    try
                    {
                        await emailSender.SendAsync(
                            targetUser.Email,
                            "Workspace access request approved",
                            $"Your workspace access request in has been approved by an administrator.");
                    }
                    catch { /* Best effort notification */ }
                }
            }

            return result;
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
            var company = await _db.Companies.AsNoTracking().SingleAsync(c => c.Id == companyId);
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

            var initialized = await _db.WorkspaceOperatingSettings
                .AsNoTracking()
                .AnyAsync(s => s.CompanyId == companyId && s.InitializedAtUtc != null);

            var eligible = !initialized
                && company.AccessBootstrapAvailable && !alreadyBootstrapped && adminCount == 1
                && totalFunctionalRoles == 0 && callerMember?.Role == CompanyMemberRole.Owner
                && callerMember.Status == CompanyMemberStatus.Active && company.CreatedByUserId == UserId;

            var ownerCanManageOwnAccess = callerMember?.Role == CompanyMemberRole.Owner
                && callerMember.Status == CompanyMemberStatus.Active
                && !await _db.CompanyMembers.AnyAsync(m => m.CompanyId == companyId
                    && m.Status == CompanyMemberStatus.Active && m.UserId != UserId);

            return Ok(new
            {
                alreadyBootstrapped,
                adminCount,
                totalFunctionalRoles,
                initialized,
                isEligibleForBootstrap = eligible,
                ownerCanManageOwnAccess
            });
        }

        [HttpPost("owner-access-setup")]
        public IActionResult OwnerAccessSetup([FromBody] SetupWorkspaceAccessModel model)
        {
            var companyId = _tenant.RequireCompanyId();
            var actor = UserId;
            if (string.IsNullOrWhiteSpace(actor))
                return Problem(statusCode: 401, title: "Unauthenticated");
            var ids = model?.SecurityRoleIds?.Distinct().OrderBy(id => id).ToList() ?? new List<int>();
            if (ids.Count == 0 || !ValidSecurityRoles(_db, ids))
                return Problem(statusCode: 400, title: "Select valid functional roles.");

            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Workspace access setup is unavailable.");

            var result = transaction.Execute<IActionResult>(companyId, guard =>
            {
                var verification = ContractAVerifier.Verify(_db, companyId, actor, ids);
                if (!verification.Allowed)
                    return Problem(statusCode: 403, title: verification.Title, detail: verification.Detail);

                var owner = verification.OwnerMember!;
                var current = owner.SecurityRoles.Select(r => r.SecurityRoleId).ToHashSet();
                var additions = ids.Where(id => !current.Contains(id)).ToList();
                foreach (var id in additions)
                    _db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = owner.Id, SecurityRoleId = id });
                if (additions.Count > 0)
                {
                    _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                    {
                        CompanyId = companyId, UserName = actor, AuditEventDateUTC = DateTime.UtcNow,
                        AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                        TableName = "__OwnerAccessSetup", RecordId = owner.Id.ToString(), FieldName = "SecurityRoleIds",
                        OriginalValue = System.Text.Json.JsonSerializer.Serialize(current.OrderBy(id => id)),
                        NewValue = System.Text.Json.JsonSerializer.Serialize(current.Union(additions).OrderBy(id => id))
                    });
                    guard.AdvanceRevision();
                }
                return Ok(new { rolesAssigned = additions });
            });
            if (result is OkObjectResult)
            {
                AccessAuthorizationService.BumpGlobalSecurityRevision();
                AuthService.InvalidateContextCache();
            }
            return result;
        }

        [HttpPost("access-setup")]
        public IActionResult AccessSetup([FromBody] SetupWorkspaceAccessModel model = null)
        {
            var companyId = _tenant.RequireCompanyId();
            var callerUserId = UserId;
            if (string.IsNullOrWhiteSpace(callerUserId))
                return Problem(statusCode: 401, title: "Unauthenticated");

            var decision = AuthService.Authorize(AccessOperationCatalog.WorkspaceAccessSetup);
            if (!decision.Allowed)
                return Problem(statusCode: decision.HttpStatus, title: decision.SafeTitle, detail: decision.SafeMessage);

            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Workspace access setup is unavailable.");

            var roleIdsToAssign = model?.SecurityRoleIds?.Distinct().ToList() ?? new List<int>();
            if (roleIdsToAssign.Count == 0)
            {
                var defaultRole = _db.SecurityRoles.FirstOrDefault(r => r.Name == "Accountant");
                if (defaultRole != null)
                    roleIdsToAssign.Add(defaultRole.Id);
            }

            var result = transaction.Execute<IActionResult>(companyId, guard =>
            {
                var company = _db.Companies.SingleOrDefault(c => c.Id == companyId);
                if (company == null || !company.AccessBootstrapAvailable)
                    return Problem(statusCode: 403, title: "Bootstrap unavailable",
                        detail: "Initial access setup is no longer available. Request functional roles through independent approval.");

                var verification = ContractAVerifier.Verify(_db, companyId, callerUserId, roleIdsToAssign);
                if (!verification.Allowed)
                    return Problem(statusCode: 403, title: verification.Title, detail: verification.Detail);

                var callerMember = verification.OwnerMember!;
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

                company.AccessBootstrapAvailable = false;
                guard.AdvanceRevision();

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

                return Ok(new
                {
                    message = "Initial access setup completed successfully.",
                    rolesAssigned = roleIdsToAssign
                });
            });

            if (result is OkObjectResult)
            {
                AccessAuthorizationService.BumpGlobalSecurityRevision();
                AuthService.InvalidateContextCache();
            }
            return result;
        }

        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [Authorize(Policy = "MfaStepUp")]
        [HttpPost("transfer-ownership")]
        public IActionResult Transfer([FromBody] TransferRequest request)
        {
            var companyId = _tenant.RequireCompanyId();
            var transaction = _policyTransaction ?? HttpContext?.RequestServices?.GetService<IWorkspacePolicyTransaction>();
            if (transaction == null)
                return Problem(statusCode: 503, title: "Workspace policy coordination is unavailable.");

            return transaction.Execute<IActionResult>(companyId, guard =>
            {
                if (_db.PersonalWorkspaces.Any(p => p.CompanyId == companyId))
                    return Conflict(new ProblemDetails
                    {
                        Status = 409, Title = "Personal books cannot be transferred",
                        Detail = "Personal books can't be transferred to another owner.",
                        Extensions = { ["code"] = "personal_workspace_not_transferable" }
                    });

                var target = FindMember(request.MemberId);
                if (target == null || target.Status != CompanyMemberStatus.Active)
                    return Problem(statusCode: 404, title: "Active member not found");
                if (target.UserId == UserId)
                    return Problem(statusCode: 400, title: "Choose another member");

                var entitlements = _entitlements
                    ?? HttpContext?.RequestServices?.GetService<IEntitlementService>();
                if (entitlements != null)
                {
                    var decision = entitlements.EvaluateForUser(target.UserId, new UserAction.ReceiveWorkspace(companyId));
                    if (!decision.Allowed)
                    {
                        var displayName = ResolveMemberDisplayName(target);
                        var detail =
                            $"{displayName} already has a free workspace. Upgrade this workspace before you transfer it, or pick another member.";
                        var problem = EntitlementProblemFactory.FromDecision(
                            decision with { Detail = detail },
                            _tenant,
                            entitlements.Options.SupportEmail);
                        return StatusCode(problem.Status ?? StatusCodes.Status403Forbidden, problem);
                    }
                }

                var caller = _db.CompanyMembers.Single(m => m.CompanyId == companyId && m.UserId == UserId);
                _db.WorkspaceLinks.RemoveRange(_db.WorkspaceLinks.Where(l => l.WorkCompanyId == companyId));
                target.Role = CompanyMemberRole.Owner;
                caller.Role = CompanyMemberRole.Admin;

                guard.Settings.EverTeamActivatedAtUtc ??= DateTime.UtcNow;
                guard.Settings.ConfirmedOwnerMemberId = target.Id;
                guard.Settings.LastConfirmedAtUtc = DateTime.UtcNow;
                guard.Settings.LastConfirmedByUserId = UserId;
                guard.AdvanceRevision();

                WorkspaceCollaborationEvidence.Record(_db, companyId, WorkspaceCollaborationEvidence.EventOwnershipTransferred, UserId, target.UserId, new
                {
                    previousOwnerUserId = UserId,
                    newOwnerMemberId = target.Id
                });

                _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
                {
                    CompanyId = companyId,
                    UserName = UserId,
                    AuditEventDateUTC = DateTime.UtcNow,
                    AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added,
                    TableName = "__WorkspacePolicyTransition",
                    RecordId = companyId.ToString(),
                    FieldName = "ConfirmedOwnerMemberId",
                    OriginalValue = caller.Id.ToString(),
                    NewValue = target.Id.ToString()
                });

                _db.SaveChanges();
                AuthService.InvalidateContextCache();
                return NoContent();
            });
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

        private string ResolveMemberDisplayName(CompanyMember member)
        {
            var user = _identity.Users.AsNoTracking().FirstOrDefault(u => u.Id == member.UserId);
            if (!string.IsNullOrWhiteSpace(user?.Email))
                return user.Email;
            if (!string.IsNullOrWhiteSpace(user?.UserName))
                return user.UserName;
            return "That member";
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
