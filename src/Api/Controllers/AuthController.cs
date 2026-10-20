using System;
using System.Security.Claims;
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
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IEmailOtpService otpService,
            UserManager<ApplicationUser> userManager,
            ISecurityService securityService,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _otpService = otpService;
            _userManager = userManager;
            _securityService = securityService;
            _configuration = configuration;
            _logger = logger;
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
                permissions);

            return Ok(userDto);
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
