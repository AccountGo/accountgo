#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("CompanyInvitation")]
    public class CompanyInvitation : BaseEntity
    {
        public int CompanyId { get; set; }
        public string Email { get; set; } = "";             // normalized lower-case
        public CompanyMemberRole Role { get; set; }          // Admin or Member only (never Owner)
        public string SecurityRoleIdsJson { get; set; } = "[]";
        public string TokenHash { get; set; } = "";          // SHA-256 base64 of raw token; raw token never stored
        public DateTime ExpiresAt { get; set; }              // now + 7 days
        public DateTime CreatedAt { get; set; }
        public string InvitedByUserId { get; set; } = "";
        public DateTime? AcceptedAt { get; set; }
        public string? AcceptedByUserId { get; set; }
        public DateTime? RevokedAt { get; set; }
        public virtual Company Company { get; set; } = null!;
    }
}
