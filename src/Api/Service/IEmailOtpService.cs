using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;

namespace Api.Service
{
    public sealed record OtpVerificationResult(
        bool Success,
        AuthResponse? Response,
        string? RawRefreshToken,
        string? Error,
        string? Status = null,
        string? MfaToken = null,
        IReadOnlyList<string>? Methods = null,
        string? Policy = null);

    public interface IEmailOtpService
    {
        Task<(bool Success, string Message, int ExpiresInSeconds, string? Error, string? DevCode)> SendOtpAsync(string email, string? ipAddress);
        Task<(bool Success, string Message, int ExpiresInSeconds, string? Error, string? DevCode)> SendWelcomeOtpAsync(ApplicationUser user, string firstName, string? organizationName, string? ipAddress);
        Task<OtpVerificationResult> VerifyOtpAsync(string email, string code, string? ipAddress, string? userAgent, string? agTdRawToken = null);
        Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionForUserAsync(ApplicationUser user, string? ipAddress, string? userAgent);
        Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RefreshSessionAsync(string rawRefreshToken, string? ipAddress, string? userAgent);
        Task<bool> RevokeSessionAsync(string rawRefreshToken);
    }
}
