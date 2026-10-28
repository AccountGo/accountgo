using System;

namespace Api.Data
{
    public class RefreshTokenSession
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        /// <summary>Groups rotations from one sign-in. Theft/reuse outside grace revokes only this family.</summary>
        public string FamilyId { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByIp { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByTokenHash { get; set; }
        public string? UserAgent { get; set; }

        /// <summary>How many times this revoked parent issued a grace sibling. Bound is 3.</summary>
        public int GraceReuseCount { get; set; }

        public string? MfaMethod { get; set; }
        public string? Amr { get; set; }
        public int? TrustedDeviceId { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsRevoked => RevokedAt != null;
        public bool IsActive => !IsRevoked && !IsExpired;
    }
}
