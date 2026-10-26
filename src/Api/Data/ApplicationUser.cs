using Microsoft.AspNetCore.Identity;

namespace Api.Data
{
    public class ApplicationUser : IdentityUser
    {
        public string? RefreshToken { get; set; }
        public DateTime RefreshTokenExpiryTime { get; set; }
        /// <summary>Set when the person closes their account. The row stays so email, audit, and history still resolve.</summary>
        public DateTime? DeletedAt { get; set; }
        /// <summary>Set to true when user logs in via an emergency recovery code; cleared upon re-enrollment or recovery code regeneration.</summary>
        public bool MfaReEnrollRequired { get; set; } = false;
    }
}
