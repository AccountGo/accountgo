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

        /// <summary>Session channel: <c>web</c> (default) or <c>cli</c>.</summary>
        public string Channel { get; set; } = RefreshTokenChannels.Web;
        /// <summary>OAuth client_id when <see cref="Channel"/> is cli; otherwise null.</summary>
        public string? ClientId { get; set; }
        /// <summary>Human-readable label for session lists (e.g. "CLI on macbook").</summary>
        public string? DeviceLabel { get; set; }
        /// <summary>Space-delimited OAuth scopes bound to this session (CLI). Null for web.</summary>
        public string? Scope { get; set; }
        /// <summary>Hard absolute expiry for the family (CLI). Null means ExpiresAt is the only bound.</summary>
        public DateTime? AbsoluteExpiresAt { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt
            || (AbsoluteExpiresAt.HasValue && DateTime.UtcNow >= AbsoluteExpiresAt.Value);
        public bool IsRevoked => RevokedAt != null;
        public bool IsActive => !IsRevoked && !IsExpired;
    }

    public static class RefreshTokenChannels
    {
        public const string Web = "web";
        public const string Cli = "cli";
    }
}
