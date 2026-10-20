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
    );

    public record UserDto(
        string Id,
        string Email,
        string FirstName,
        string LastName,
        IEnumerable<string> Roles,
        IEnumerable<string>? Permissions = null,
        string? PhoneNumber = null,
        bool HasPassword = false
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
}
