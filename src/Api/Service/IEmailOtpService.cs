using System.Threading.Tasks;
using Api.Data;
using Dto.Security;

namespace Api.Service
{
    public interface IEmailOtpService
    {
        Task<(bool Success, string Message, int ExpiresInSeconds, string? Error)> SendOtpAsync(string email, string? ipAddress);
        Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> VerifyOtpAsync(string email, string code, string? ipAddress, string? userAgent);
        Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionForUserAsync(ApplicationUser user, string? ipAddress, string? userAgent);
        Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RefreshSessionAsync(string rawRefreshToken, string? ipAddress, string? userAgent);
        Task<bool> RevokeSessionAsync(string rawRefreshToken);
    }
}
