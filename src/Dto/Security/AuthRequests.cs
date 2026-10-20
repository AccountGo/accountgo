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
        IEnumerable<string> Roles
    );

    public record AddNewUserRequest(
        [Required, EmailAddress] string Email,
        [Required] string FirstName,
        [Required] string LastName,
        string? Password
    );
}
