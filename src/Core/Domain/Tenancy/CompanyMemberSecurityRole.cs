#nullable enable
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
    }
}
