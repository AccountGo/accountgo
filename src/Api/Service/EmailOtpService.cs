using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services.Security;

namespace Api.Service
{
    public class EmailOtpService : IEmailOtpService
    {
        private const string TokenProvider = "EmailOtp";
        private const string TokenPurpose = "Default";
        private const int OtpExpirySeconds = 300; // 5 minutes
        private const int ResendCooldownSeconds = 30; // 30 seconds
        private const int MaxFailedAttempts = 5;

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationIdentityDbContext _identityContext;
        private readonly IEmailSender _emailSender;
        private readonly IAuthenticationService _authService;
        private readonly ISecurityService _securityService;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<EmailOtpService> _logger;
        private readonly byte[] _hmacKey;

        public EmailOtpService(
            UserManager<ApplicationUser> userManager,
            ApplicationIdentityDbContext identityContext,
            IEmailSender emailSender,
            IAuthenticationService authService,
            ISecurityService securityService,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger<EmailOtpService> logger)
        {
            _userManager = userManager;
            _identityContext = identityContext;
            _emailSender = emailSender;
            _authService = authService;
            _securityService = securityService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;

            var secret = _configuration["SECRET_KEY"] ?? _configuration["SECRET:key"] ?? _configuration["SECRET__key"] ?? Environment.GetEnvironmentVariable("SECRET_KEY");
            if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            {
                throw new InvalidOperationException("SECRET_KEY (or SECRET:key) configuration is missing or fewer than 32 bytes (256 bits).");
            }
            _hmacKey = Encoding.UTF8.GetBytes(secret);
        }

        public async Task<(bool Success, string Message, int ExpiresInSeconds, string? Error, string? DevCode)> SendOtpAsync(string email, string? ipAddress)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _userManager.FindByEmailAsync(normalizedEmail);

            // User Enumeration Defense: Perform equivalent work and return identical 200 payload
            if (user == null)
            {
                _logger.LogInformation("OTP request for non-existent email {Email} from {IP} - running dummy hash compute.", normalizedEmail, ipAddress);
                PerformDummyWork(normalizedEmail);
                return (true, "If an account exists, a code has been sent.", OtpExpirySeconds, null, null);
            }

            // Inspect existing token payload if any for cooldown
            var existingTokenJson = await _userManager.GetAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
            if (!string.IsNullOrEmpty(existingTokenJson))
            {
                try
                {
                    var existingPayload = JsonSerializer.Deserialize<OtpPayload>(existingTokenJson);
                    if (existingPayload != null)
                    {
                        var secondsSinceLastSend = (DateTime.UtcNow - existingPayload.LastSentAt).TotalSeconds;
                        if (secondsSinceLastSend < ResendCooldownSeconds)
                        {
                            var waitRemaining = (int)Math.Ceiling(ResendCooldownSeconds - secondsSinceLastSend);
                            return (false, string.Empty, 0, $"Please wait {waitRemaining} seconds before requesting a new code.", null);
                        }
                    }
                }
                catch
                {
                    // Ignore deserialization error and regenerate
                }
            }

            // Generate secure 6-digit numeric OTP code
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            var codeHash = ComputeHmac(code);

            var payload = new OtpPayload
            {
                CodeHash = codeHash,
                ExpiresAt = DateTime.UtcNow.AddSeconds(OtpExpirySeconds),
                FailedAttempts = 0,
                LastSentAt = DateTime.UtcNow
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            await _userManager.SetAuthenticationTokenAsync(user, TokenProvider, TokenPurpose, payloadJson);

            // Send Email Dispatch
            var emailSubject = $"AccountGo Sign-In Code: {code}";
            var emailBody = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff;"">
  <div style=""text-align: center; margin-bottom: 24px;"">
    <h2 style=""color: #0f172a; margin: 0; font-size: 24px; font-weight: 700;"">AccountGo</h2>
    <p style=""color: #64748b; font-size: 14px; margin-top: 4px;"">Passwordless Sign-In Verification</p>
  </div>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Hello,</p>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Here is your single-use sign-in verification code. Enter this code to access your AccountGo workspace:</p>
  <div style=""text-align: center; margin: 32px 0;"">
    <div style=""display: inline-block; font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #1e3a8a; background-color: #f1f5f9; padding: 16px 28px; border-radius: 8px; border: 1px dashed #cbd5e1;"">
      {code}
    </div>
  </div>
  <p style=""color: #64748b; font-size: 13px; line-height: 1.4;"">This code will expire in <strong>5 minutes</strong>. If you did not request this login code, you can safely ignore this email.</p>
</div>";

            try
            {
                await _emailSender.SendAsync(user.Email ?? normalizedEmail, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver OTP email to {Email}", normalizedEmail);
                return (false, string.Empty, 0, "Failed to deliver verification email. Please check your email address or try again.", null);
            }

            var isConsoleDev = _emailSender is DevConsoleEmailSender && _environment.IsDevelopment();
            var devCode = isConsoleDev ? code : null;

            return (true, "If an account exists, a code has been sent.", OtpExpirySeconds, null, devCode);
        }

        public async Task<(bool Success, string Message, int ExpiresInSeconds, string? Error, string? DevCode)> SendWelcomeOtpAsync(
            ApplicationUser user, string firstName, string? organizationName, string? ipAddress)
        {
            var normalizedEmail = user.Email ?? user.UserName ?? string.Empty;

            // Generate secure 6-digit numeric OTP code
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            var codeHash = ComputeHmac(code);

            var payload = new OtpPayload
            {
                CodeHash = codeHash,
                ExpiresAt = DateTime.UtcNow.AddSeconds(OtpExpirySeconds),
                FailedAttempts = 0,
                LastSentAt = DateTime.UtcNow
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            await _userManager.SetAuthenticationTokenAsync(user, TokenProvider, TokenPurpose, payloadJson);

            // Send Welcome Email Dispatch
            var emailSubject = $"Welcome to AccountGo! Your verification code is {code}";
            var orgText = !string.IsNullOrWhiteSpace(organizationName)
                ? $"<p style=\"color: #334155; font-size: 15px; line-height: 1.5;\">Your workspace <strong>{System.Net.WebUtility.HtmlEncode(organizationName)}</strong> has been created and is ready for you.</p>"
                : string.Empty;

            var emailBody = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 520px; margin: 0 auto; padding: 28px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;"">
  <div style=""text-align: center; margin-bottom: 24px;"">
    <h2 style=""color: #0f172a; margin: 0; font-size: 26px; font-weight: 700; letter-spacing: -0.5px;"">AccountGo</h2>
    <p style=""color: #64748b; font-size: 14px; margin-top: 4px;"">Welcome to your financial accounting workspace</p>
  </div>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Hello {System.Net.WebUtility.HtmlEncode(firstName)},</p>
  <p style=""color: #334155; font-size: 15px; line-height: 1.5;"">Thank you for joining AccountGo. Please enter this 6-digit verification code to confirm your email and activate your account:</p>
  {orgText}
  <div style=""text-align: center; margin: 32px 0;"">
    <div style=""display: inline-block; font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #1e3a8a; background-color: #f1f5f9; padding: 16px 28px; border-radius: 8px; border: 1px dashed #cbd5e1;"">
      {code}
    </div>
  </div>
  <p style=""color: #64748b; font-size: 13px; line-height: 1.4;"">This single-use code will expire in <strong>5 minutes</strong>. If you did not create an account on AccountGo, please disregard this email.</p>
</div>";

            try
            {
                await _emailSender.SendAsync(normalizedEmail, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver welcome OTP email to {Email}", normalizedEmail);
                return (false, string.Empty, 0, "Failed to deliver verification email. Please check your email address or try again.", null);
            }

            var isConsoleDev = _emailSender is DevConsoleEmailSender && _environment.IsDevelopment();
            var devCode = isConsoleDev ? code : null;

            return (true, "Welcome! A 6-digit verification code has been sent to your email.", OtpExpirySeconds, null, devCode);
        }

        public async Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> VerifyOtpAsync(
            string email, string code, string? ipAddress, string? userAgent)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _userManager.FindByEmailAsync(normalizedEmail);

            if (user == null)
            {
                PerformDummyWork(normalizedEmail);
                return (false, null, null, "Invalid or expired code.");
            }

            var tokenJson = await _userManager.GetAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
            if (string.IsNullOrEmpty(tokenJson))
            {
                return (false, null, null, "Invalid or expired code.");
            }

            OtpPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<OtpPayload>(tokenJson);
            }
            catch
            {
                await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
                return (false, null, null, "Invalid or expired code.");
            }

            if (payload == null)
            {
                await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
                return (false, null, null, "Invalid or expired code.");
            }

            // Expiry Check
            if (DateTime.UtcNow >= payload.ExpiresAt)
            {
                await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
                return (false, null, null, "Invalid or expired code.");
            }

            // Failed Attempts Limit Check (Max 5 attempts)
            if (payload.FailedAttempts >= MaxFailedAttempts)
            {
                await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
                return (false, null, null, "Code locked due to too many failed attempts. Please request a new code.");
            }

            // Constant-Time HMAC Comparison
            var computedHash = ComputeHmac(code.Trim());
            var isMatch = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(payload.CodeHash));

            if (!isMatch)
            {
                payload.FailedAttempts++;
                if (payload.FailedAttempts >= MaxFailedAttempts)
                {
                    await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);
                }
                else
                {
                    await _userManager.SetAuthenticationTokenAsync(user, TokenProvider, TokenPurpose, JsonSerializer.Serialize(payload));
                }

                return (false, null, null, "Invalid or expired code.");
            }

            // Successful match - Invalidate single-use code immediately
            await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenPurpose);

            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            // Mint JWT & Session
            return await CreateSessionAndAuthResponseAsync(user, ipAddress, userAgent);
        }

        public async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RefreshSessionAsync(
            string rawRefreshToken, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return (false, null, null, "Missing refresh token.");

            var tokenHash = ComputeSha256(rawRefreshToken);
            var session = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (session == null)
                return (false, null, null, "Invalid session token.");

            var now = DateTime.UtcNow;
            if (session.IsRevoked)
            {
                // A token rotated moments ago comes back when the response carrying its replacement never reached the
                // browser (a timeout while the API was starting) or when two tabs refreshed at once. That is not theft:
                // within the grace period the session carries on from it, and the replacement nobody received is revoked.
                if (session.ReplacedByTokenHash != null && session.RevokedAt > now - RotationGracePeriod)
                {
                    var replacement = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == session.ReplacedByTokenHash);
                    if (replacement is { RevokedAt: null })
                        replacement.RevokedAt = now;
                    return await RotateAsync(session, ipAddress, userAgent, now);
                }

                // REUSE DETECTION (Compromise protection): an older revoked token revokes the ENTIRE chain for this user
                _logger.LogWarning("SECURITY ALERT: Revoked refresh token reuse detected for User {UserId}! Revoking all sessions.", session.UserId);
                var activeSessions = await _identityContext.RefreshTokens
                    .Where(t => t.UserId == session.UserId && t.RevokedAt == null)
                    .ToListAsync();

                foreach (var s in activeSessions)
                {
                    s.RevokedAt = DateTime.UtcNow;
                }

                await _identityContext.SaveChangesAsync();
                return (false, null, null, "Session security violation detected. Please sign in again.");
            }

            if (session.IsExpired)
            {
                return (false, null, null, "Session has expired. Please sign in again.");
            }

            return await RotateAsync(session, ipAddress, userAgent, now);
        }

        /// <summary>
        /// How long after a refresh token is rotated it is still accepted once more: long enough for a browser to retry
        /// a refresh whose response was lost while the API was starting.
        /// </summary>
        private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromMinutes(5);

        /// <summary>Replaces a session's refresh token with a new one and mints a fresh access token.</summary>
        private async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RotateAsync(
            RefreshTokenSession session, string? ipAddress, string? userAgent, DateTime now)
        {
            if (session.IsExpired)
                return (false, null, null, "Session has expired. Please sign in again.");

            var user = await _userManager.FindByIdAsync(session.UserId);
            if (user == null)
                return (false, null, null, "User account no longer exists.");

            var newRawToken = GenerateSecureToken();
            var newTokenHash = ComputeSha256(newRawToken);

            // A token already rotated keeps its first rotation time, so the grace period runs from then.
            session.RevokedAt ??= now;
            session.ReplacedByTokenHash = newTokenHash;

            var newSession = new RefreshTokenSession
            {
                UserId = user.Id,
                TokenHash = newTokenHash,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7),
                CreatedByIp = ipAddress,
                UserAgent = userAgent
            };

            _identityContext.RefreshTokens.Add(newSession);
            await _identityContext.SaveChangesAsync();

            // Mint fresh JWT Access Token
            var tokenResult = await _authService.CreateToken(user, populateExp: false);
            var userDto = await BuildUserDtoAsync(user);
            var expiresInSeconds = 15 * 60; // 15 mins

            var authResponse = new AuthResponse(
                tokenResult.AccessToken,
                expiresInSeconds,
                userDto);

            return (true, authResponse, newRawToken, null);
        }

        public async Task<bool> RevokeSessionAsync(string rawRefreshToken)
        {
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return false;

            var tokenHash = ComputeSha256(rawRefreshToken);
            var session = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            if (session == null || session.IsRevoked)
                return false;

            session.RevokedAt = DateTime.UtcNow;
            await _identityContext.SaveChangesAsync();
            return true;
        }

        public Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionForUserAsync(
            ApplicationUser user, string? ipAddress, string? userAgent)
        {
            return CreateSessionAndAuthResponseAsync(user, ipAddress, userAgent);
        }

        private async Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionAndAuthResponseAsync(
            ApplicationUser user, string? ipAddress, string? userAgent)
        {
            var tokenResult = await _authService.CreateToken(user, populateExp: false);

            var rawRefreshToken = GenerateSecureToken();
            var tokenHash = ComputeSha256(rawRefreshToken);

            var session = new RefreshTokenSession
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedByIp = ipAddress,
                UserAgent = userAgent
            };

            _identityContext.RefreshTokens.Add(session);
            await _identityContext.SaveChangesAsync();

            var userDto = await BuildUserDtoAsync(user);
            var expiresInSeconds = 15 * 60; // 15 mins

            var authResponse = new AuthResponse(
                tokenResult.AccessToken,
                expiresInSeconds,
                userDto);

            return (true, authResponse, rawRefreshToken, null);
        }

        private async Task<UserDto> BuildUserDtoAsync(ApplicationUser user)
        {
            var roles = new HashSet<string>(await _userManager.GetRolesAsync(user));
            var domainUser = _securityService.GetUser(user.UserName ?? user.Email ?? string.Empty);
            string firstName = string.Empty;
            string lastName = string.Empty;

            if (domainUser != null)
            {
                firstName = domainUser.Firstname ?? string.Empty;
                lastName = domainUser.Lastname ?? string.Empty;
                if (domainUser.Roles != null)
                {
                    foreach (var r in domainUser.Roles)
                    {
                        if (r?.SecurityRole?.Name != null)
                            roles.Add(r.SecurityRole.Name);
                    }
                }
            }

            var permissions = _securityService.GetPermissionsForUser(user.UserName ?? user.Email ?? string.Empty);

            return new UserDto(
                user.Id,
                user.Email ?? user.UserName ?? string.Empty,
                firstName,
                lastName,
                roles,
                permissions,
                user.PhoneNumber,
                !string.IsNullOrEmpty(user.PasswordHash));
        }

        private string ComputeHmac(string input)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash);
        }

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }

        private static string GenerateSecureToken()
        {
            var bytes = new byte[48];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private void PerformDummyWork(string input)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            hmac.ComputeHash(Encoding.UTF8.GetBytes(input + "dummy_entropy_constant"));
        }

        private class OtpPayload
        {
            public string CodeHash { get; set; } = string.Empty;
            public DateTime ExpiresAt { get; set; }
            public int FailedAttempts { get; set; }
            public DateTime LastSentAt { get; set; }
        }
    }
}
