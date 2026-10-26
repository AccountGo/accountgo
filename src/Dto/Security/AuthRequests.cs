using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Security
{
    public record SendOtpRequest(
        [Required, EmailAddress] string Email
    );

    public record VerifyOtpRequest(
        [Required, EmailAddress] string Email,
        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be exactly 6 digits.")] string Otp
    );

    public record GoogleSignInRequest(
        [Required] string IdToken
    );

    public record AuthResponse(
        string AccessToken,
        int ExpiresIn,
        UserDto User
    )
    {
        public IEnumerable<OrganizationDto> Organizations { get; set; } = new List<OrganizationDto>();
    }

    #nullable disable
    public class OrganizationDto
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }
    #nullable restore

    public record UserDto(
        string Id,
        string Email,
        string FirstName,
        string LastName,
        IEnumerable<string> Roles,
        IEnumerable<string>? Permissions = null,
        string? PhoneNumber = null,
        bool HasPassword = false,
        bool MfaReEnrollRequired = false,
        DateTime? MfaGracePeriodEndsAt = null
    );

    public record UpdateProfileRequest(
        string? FirstName,
        string? LastName,
        string? PhoneNumber
    );

    public record ChangePasswordRequest(
        string? CurrentPassword,
        [Required, MinLength(6, ErrorMessage = "New password must be at least 6 characters.")] string NewPassword
    );

    public record UserSessionDto(
        int Id,
        System.DateTime CreatedAt,
        System.DateTime ExpiresAt,
        string? IpAddress,
        string? UserAgent,
        bool IsCurrent
    );

    public record AddNewUserRequest(
        [Required, EmailAddress] string Email,
        [Required] string FirstName,
        [Required] string LastName,
        string? Password
    );

    public record SignUpRequest(
        [Required, EmailAddress] string Email,
        [Required, MinLength(1, ErrorMessage = "First name is required.")] string FirstName,
        [Required, MinLength(1, ErrorMessage = "Last name is required.")] string LastName,
        string? OrganizationName = null,
        string? OrganizationSlug = null,
        string? Currency = null
    )
    {
        #nullable disable
        public string InvitationToken { get; set; }
        #nullable restore
    }

    public record SignUpResponse(
        bool Success,
        string Message,
        string Email,
        string? OrgSlug,
        int ExpiresInSeconds,
        string? DevCode
    );
}
