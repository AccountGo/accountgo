using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Dto.Security;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Administration;
using Services.Security;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseController
    {
        private const string RefreshCookieName = "accountgo_refresh_token";

        private readonly IEmailOtpService _otpService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ISecurityService _securityService;
        private readonly IAdministrationService _adminService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly ApplicationIdentityDbContext _identityContext;

        public AuthController(
            IEmailOtpService otpService,
            UserManager<ApplicationUser> userManager,
            ISecurityService securityService,
            IAdministrationService adminService,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            ApplicationIdentityDbContext identityContext)
        {
            _otpService = otpService;
            _userManager = userManager;
            _securityService = securityService;
            _adminService = adminService;
            _configuration = configuration;
            _logger = logger;
            _identityContext = identityContext;
        }

        [AllowAnonymous]
        [HttpPost("google")]
        public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings();
                var googleClientId = _configuration["Google:ClientId"];
                if (string.IsNullOrWhiteSpace(googleClientId))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Google Sign-In Not Configured",
                        Detail = "Google authentication is not configured on this server.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
                settings.Audience = new[] { googleClientId };

                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
                if (payload == null || !payload.EmailVerified)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Google Authentication Failed",
                        Detail = "Unverified Google email or invalid token.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                // Provisioning policy: check domain restriction if configured
                var allowedHostedDomain = _configuration["Google:HostedDomain"];
                if (!string.IsNullOrEmpty(allowedHostedDomain) &&
                    !string.Equals(payload.HostedDomain, allowedHostedDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }

                var user = await _userManager.FindByEmailAsync(payload.Email);
                if (user == null)
                {
                    var autoProvision = _configuration.GetValue<bool>("Google:AutoProvision", false);
                    if (!autoProvision)
                    {
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Account Not Found",
                            Detail = "No AccountGo profile associated with this Google address. Please contact your administrator.",
                            Status = StatusCodes.Status400BadRequest
                        });
                    }

                    user = new ApplicationUser
                    {
                        UserName = payload.Email,
                        Email = payload.Email,
                        EmailConfirmed = true
                    };
                    var createResult = await _userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Registration Failed",
                            Detail = string.Join(", ", System.Linq.Enumerable.Select(createResult.Errors, e => e.Description)),
                            Status = StatusCodes.Status400BadRequest
                        });
                    }
                }

                // Link Google login if not already linked
                var logins = await _userManager.GetLoginsAsync(user);
                if (!logins.Any(l => l.LoginProvider == "Google" && l.ProviderKey == payload.Subject))
                {
                    await _userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                }

                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers.UserAgent.ToString();
                var result = await _otpService.CreateSessionForUserAsync(user, ip, userAgent);

                if (!string.IsNullOrEmpty(result.RawRefreshToken))
                {
                    SetRefreshTokenCookie(result.RawRefreshToken);
                }

                return Ok(result.Response);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google sign-in failed due to invalid JWT token.");
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Google Token",
                    Detail = "The provided Google ID token is invalid or expired.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during Google authentication.");
                return BadRequest(new ProblemDetails
                {
                    Title = "Google Authentication Failed",
                    Detail = "An unexpected error occurred during Google sign-in. Please try again or contact support.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        [AllowAnonymous]
        [EnableRateLimiting("otp-send")]
        [HttpPost("signup")]
        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // 1. Check if user already exists
            var existingAppUser = await _userManager.FindByEmailAsync(normalizedEmail);
            var existingDomainUser = _securityService.GetUser(normalizedEmail);

            if (existingAppUser != null || existingDomainUser != null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Account Already Exists",
                    Detail = "An account with this email address already exists. Please sign in instead.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // 2. Validate and prepare organization slug ahead of time
            string? cleanSlug = null;
            if (!string.IsNullOrWhiteSpace(request.OrganizationName))
            {
                var rawSlug = !string.IsNullOrWhiteSpace(request.OrganizationSlug)
                    ? request.OrganizationSlug
                    : request.OrganizationName;

                cleanSlug = Regex.Replace(rawSlug.ToLowerInvariant().Trim(), @"[^a-z0-9\-]", "-");
                cleanSlug = Regex.Replace(cleanSlug, @"-+", "-").Trim('-');
                if (string.IsNullOrWhiteSpace(cleanSlug)) cleanSlug = "org";

                if (_adminService.GetCompanyBySlug(cleanSlug) != null)
                {
                    cleanSlug = $"{cleanSlug}-{RandomNumberGenerator.GetInt32(100, 1000)}";
                }
            }

            // 3. Create Identity user first (ApplicationUser)
            var appUser = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = false
            };

            var createResult = await _userManager.CreateAsync(appUser);
            if (!createResult.Succeeded)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Registration Failed",
                    Detail = string.Join("; ", createResult.Errors.Select(e => e.Description)),
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // 4. Create Domain user with SystemAdministrators role
            var adminRole = _securityService.GetRole("SystemAdministrators") ?? _securityService.GetRole("Administrator");
            var roleIds = adminRole != null ? new List<int> { adminRole.Id } : new List<int>();

            _securityService.AddUser(
                normalizedEmail,
                normalizedEmail,
                request.FirstName.Trim(),
                request.LastName.Trim(),
                roleIds);

            // 5. Save Company now that user registration succeeded
            Core.Domain.Company? company = null;
            if (!string.IsNullOrWhiteSpace(request.OrganizationName) && cleanSlug != null)
            {
                var shortName = request.OrganizationName.Trim().Length <= 5
                    ? request.OrganizationName.Trim().ToUpper()
                    : request.OrganizationName.Trim().Substring(0, 4).ToUpper();

                company = new Core.Domain.Company
                {
                    Name = request.OrganizationName.Trim(),
                    ShortName = shortName,
                    Slug = cleanSlug,
                    CompanyCode = RandomNumberGenerator.GetInt32(100, 1000).ToString(),
                    Plan = "accounting",
                    SubscriptionStatus = "active"
                };

                _adminService.SaveCompany(company);
            }

            // 6. Send Welcome verification code via Resend
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _otpService.SendWelcomeOtpAsync(appUser, request.FirstName.Trim(), company?.Name, ipAddress);

            if (!result.Success)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Verification Email Failed",
                    Detail = result.Error ?? "Failed to deliver verification code email. Please check your address or try again.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            return Ok(new SignUpResponse(
                Success: true,
                Message: result.Message,
                Email: normalizedEmail,
                OrgSlug: company?.Slug,
                ExpiresInSeconds: result.ExpiresInSeconds,
                DevCode: result.DevCode
            ));
        }

        [AllowAnonymous]
        [EnableRateLimiting("otp-send")]
        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _otpService.SendOtpAsync(request.Email, ipAddress);

            if (!result.Success)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "OTP Request Failed",
                    Detail = result.Error,
                    Status = StatusCodes.Status400BadRequest
                });
            }

            return Ok(new
            {
                message = result.Message,
                expiresInSeconds = result.ExpiresInSeconds,
                devCode = result.DevCode
            });
        }

        [AllowAnonymous]
        [EnableRateLimiting("otp-verify")]
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _otpService.VerifyOtpAsync(request.Email, request.Otp, ipAddress, userAgent);

            if (!result.Success || result.Response == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Verification Failed",
                    Detail = result.Error ?? "Invalid or expired code.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (!string.IsNullOrEmpty(result.RawRefreshToken))
            {
                SetRefreshTokenCookie(result.RawRefreshToken);
            }

            return Ok(result.Response);
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshSession([FromBody] RefreshTokenRequest? bodyRequest = null)
        {
            var rawRefreshToken = Request.Cookies[RefreshCookieName] ?? bodyRequest?.RefreshToken;

            if (string.IsNullOrWhiteSpace(rawRefreshToken))
            {
                return Unauthorized(new ProblemDetails
                {
                    Title = "Unauthorized",
                    Detail = "No refresh token provided.",
                    Status = StatusCodes.Status401Unauthorized
                });
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _otpService.RefreshSessionAsync(rawRefreshToken, ipAddress, userAgent);

            if (!result.Success || result.Response == null)
            {
                ClearRefreshTokenCookie();
                return Unauthorized(new ProblemDetails
                {
                    Title = "Session Refresh Failed",
                    Detail = result.Error ?? "Session has expired or been revoked.",
                    Status = StatusCodes.Status401Unauthorized
                });
            }

            if (!string.IsNullOrEmpty(result.NewRawRefreshToken))
            {
                SetRefreshTokenCookie(result.NewRawRefreshToken);
            }

            return Ok(result.Response);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "User Not Found",
                    Detail = "Authenticated user identity could not be located.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var roles = new System.Collections.Generic.HashSet<string>(await _userManager.GetRolesAsync(user));
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

            var userDto = new UserDto(
                user.Id,
                user.Email ?? user.UserName ?? string.Empty,
                firstName,
                lastName,
                roles,
                permissions,
                user.PhoneNumber,
                !string.IsNullOrEmpty(user.PasswordHash));

            return Ok(userDto);
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "User Not Found",
                    Detail = "Authenticated user identity could not be located.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Update domain User entity
            var domainUser = _securityService.GetUser(user.UserName ?? user.Email ?? string.Empty);
            if (domainUser != null)
            {
                if (request.FirstName != null)
                    domainUser.Firstname = request.FirstName.Trim();
                if (request.LastName != null)
                    domainUser.Lastname = request.LastName.Trim();

                _securityService.UpdateUser(domainUser, null);
            }

            // Update ApplicationUser entity
            if (request.PhoneNumber != null)
            {
                user.PhoneNumber = request.PhoneNumber.Trim();
            }

            var identityResult = await _userManager.UpdateAsync(user);
            if (!identityResult.Succeeded)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Update Failed",
                    Detail = string.Join("; ", System.Linq.Enumerable.Select(identityResult.Errors, e => e.Description)),
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Return fresh UserDto
            var roles = new System.Collections.Generic.HashSet<string>(await _userManager.GetRolesAsync(user));
            if (domainUser?.Roles != null)
            {
                foreach (var r in domainUser.Roles)
                {
                    if (r?.SecurityRole?.Name != null)
                        roles.Add(r.SecurityRole.Name);
                }
            }

            var permissions = _securityService.GetPermissionsForUser(user.UserName ?? user.Email ?? string.Empty);

            var updatedDto = new UserDto(
                user.Id,
                user.Email ?? user.UserName ?? string.Empty,
                domainUser?.Firstname ?? string.Empty,
                domainUser?.Lastname ?? string.Empty,
                roles,
                permissions,
                user.PhoneNumber,
                !string.IsNullOrEmpty(user.PasswordHash));

            return Ok(updatedDto);
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "User Not Found",
                    Detail = "Authenticated user identity could not be located.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            IdentityResult result;
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Password Required",
                        Detail = "Your current password is required to set a new password.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
                result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            }
            else
            {
                result = await _userManager.AddPasswordAsync(user, request.NewPassword);
            }

            if (!result.Succeeded)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Password Update Failed",
                    Detail = string.Join("; ", System.Linq.Enumerable.Select(result.Errors, e => e.Description)),
                    Status = StatusCodes.Status400BadRequest
                });
            }

            return Ok(new { success = true, message = "Password updated successfully." });
        }

        [Authorize]
        [HttpGet("sessions")]
        public async Task<IActionResult> GetActiveSessions()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
                return NotFound();

            var currentRawToken = Request.Cookies[RefreshCookieName];
            string? currentHash = null;
            if (!string.IsNullOrEmpty(currentRawToken))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(currentRawToken));
                currentHash = Convert.ToBase64String(hashBytes);
            }

            var now = DateTime.UtcNow;
            var sessions = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(System.Linq.Queryable.OrderByDescending(
                    System.Linq.Queryable.Where(_identityContext.RefreshTokens, t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > now),
                    t => t.CreatedAt));

            var sessionDtos = System.Linq.Enumerable.Select(sessions, s => new UserSessionDto(
                s.Id,
                s.CreatedAt,
                s.ExpiresAt,
                s.CreatedByIp,
                s.UserAgent,
                s.TokenHash == currentHash
            )).ToList();

            return Ok(sessionDtos);
        }

        [Authorize]
        [HttpDelete("sessions/{id:int}")]
        public async Task<IActionResult> RevokeSession(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
                return NotFound();

            var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(_identityContext.RefreshTokens, s => s.Id == id && s.UserId == user.Id);

            if (session == null)
                return NotFound(new ProblemDetails { Title = "Session Not Found", Detail = "The requested session does not exist.", Status = StatusCodes.Status404NotFound });

            session.RevokedAt = DateTime.UtcNow;
            await _identityContext.SaveChangesAsync();

            return Ok(new { success = true, message = "Session revoked successfully." });
        }

        [Authorize]
        [HttpPost("sessions/revoke-others")]
        public async Task<IActionResult> RevokeOtherSessions()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
                user = await _userManager.FindByIdAsync(userId);
            if (user == null && !string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            if (user == null)
                return NotFound();

            var currentRawToken = Request.Cookies[RefreshCookieName];
            string? currentHash = null;
            if (!string.IsNullOrEmpty(currentRawToken))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(currentRawToken));
                currentHash = Convert.ToBase64String(hashBytes);
            }

            var otherSessions = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(System.Linq.Queryable.Where(_identityContext.RefreshTokens, t => t.UserId == user.Id && t.RevokedAt == null && (currentHash == null || t.TokenHash != currentHash)));

            foreach (var s in otherSessions)
            {
                s.RevokedAt = DateTime.UtcNow;
            }

            await _identityContext.SaveChangesAsync();
            return Ok(new { success = true, revokedCount = otherSessions.Count });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var rawRefreshToken = Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrEmpty(rawRefreshToken))
            {
                await _otpService.RevokeSessionAsync(rawRefreshToken);
            }

            ClearRefreshTokenCookie();
            return Ok(new { message = "Logged out successfully" });
        }

        private void SetRefreshTokenCookie(string token)
        {
            var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = isHttps,
                SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
                Expires = DateTime.UtcNow.AddDays(7),
                Path = "/api"
            };
            Response.Cookies.Append(RefreshCookieName, token, cookieOptions);
        }

        private void ClearRefreshTokenCookie()
        {
            var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
            Response.Cookies.Delete(RefreshCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = isHttps,
                SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
                Path = "/api"
            });
        }
    }

    public record RefreshTokenRequest(string? RefreshToken);
}
