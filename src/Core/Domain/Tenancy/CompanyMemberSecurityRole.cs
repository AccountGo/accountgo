#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("CompanyMemberSecurityRole")]   // per-workspace functional roles (Accountant, Sales Rep, …)
    public class CompanyMemberSecurityRole : BaseEntity
    {
        public int CompanyMemberId { get; set; }
        public int SecurityRoleId { get; set; }
        public virtual CompanyMember? CompanyMember { get; set; }
        public virtual Security.SecurityRole? SecurityRole { get; set; }

        /// <summary>How this grant was created. Existing rows backfill to LegacyExplicit.</summary>
        public WorkspaceMemberGrantOrigin GrantOrigin { get; set; } = WorkspaceMemberGrantOrigin.LegacyExplicit;

        public DateTime? GrantedAtUtc { get; set; }
        public string? GrantedByUserId { get; set; }

        /// <summary>Managed profile version when origin is a managed workspace profile; otherwise null.</summary>
        public int? AccessProfileVersion { get; set; }
    }
}
