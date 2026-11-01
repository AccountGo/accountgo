#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    public enum AccessRequestStatus
    {
        Pending = 1,
        Applied = 2,
        Rejected = 3,
        Cancelled = 4,
        Expired = 5,
        Invalidated = 6
    }

    [Table("WorkspaceAccessRequest")]
    public class WorkspaceAccessRequest : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public string RequestId { get; set; } = "";
        public string RequesterUserId { get; set; } = "";
        public string TargetUserId { get; set; } = "";
        public int TargetMemberId { get; set; }
        public string RequestedRoleIdsJson { get; set; } = "[]";
        public string? Reason { get; set; }
        public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;
        public string? ApproverUserId { get; set; }
        public string? DecisionReason { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? DecidedAtUtc { get; set; }
        public DateTime? AppliedAtUtc { get; set; }
        public string PolicyVersion { get; set; } = "";
        public long TargetMembershipRevisionAtRequest { get; set; }
        public string? BoundApprovalId { get; set; }

        public virtual Company Company { get; set; } = null!;
        public virtual CompanyMember TargetMember { get; set; } = null!;
    }
}
