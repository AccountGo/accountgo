#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    public enum CompanyMemberRole { Owner = 1, Admin = 2, Member = 3 }
    public enum CompanyMemberStatus { Active = 1, Suspended = 2 }

    [Table("CompanyMember")]
    public class CompanyMember : BaseEntity
    {
        public int CompanyId { get; set; }
        public string UserId { get; set; } = "";   // AspNetUsers.Id (== JWT NameIdentifier)
        public CompanyMemberRole Role { get; set; }  // governance: who can manage the workspace
        public CompanyMemberStatus Status { get; set; } = CompanyMemberStatus.Active;
        public DateTime JoinedAt { get; set; }
        public string? InvitedByUserId { get; set; }
        public virtual Company Company { get; set; } = null!;
        public virtual ICollection<CompanyMemberSecurityRole> SecurityRoles { get; set; } = new List<CompanyMemberSecurityRole>();
    }
}
