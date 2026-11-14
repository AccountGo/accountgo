using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Api.Service.Google;
using System.Text;
using Dto.Security;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services.Administration;
using Services.Billing;
using Services.Security;

namespace Api.Controllers
{
    [Core.Tenancy.TenantFree]
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseController
    {
        private const string RefreshCookieName = "accountgo_refresh_token";

        private readonly IEmailOtpService _otpService;
        private readonly IAuthenticationService _authService;
        private readonly ISessionService _sessionService;
        private readonly IMfaGate _mfaGate;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ISecurityService _securityService;
        private readonly IAdministrationService _adminService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly ApplicationIdentityDbContext _identityContext;
        private readonly ApiDbContext _apiDb;
        private readonly WorkspaceService _workspaces;
        private readonly AccountDeletionService _accountDeletion;
        private readonly IPlanCatalog _plans;
        private readonly WorkspaceInitializationApplicator _workspaceInit;
        private readonly Core.Authorization.IWorkspacePolicyTransaction _policyTransaction;
        private readonly ISecurityAuditService? _securityAudit;
        private readonly IGoogleIdTokenValidator _googleValidator;
        private readonly IGoogleSignInService _googleSignInService;

        public AuthController(
            IEmailOtpService otpService,
            IAuthenticationService authService,
            ISessionService sessionService,
            IMfaGate mfaGate,
            UserManager<ApplicationUser> userManager,
            ISecurityService securityService,
            IAdministrationService adminService,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            ApplicationIdentityDbContext identityContext,
            ApiDbContext apiDb,
            WorkspaceService workspaces,
            AccountDeletionService accountDeletion,
            IPlanCatalog plans,
            IGoogleIdTokenValidator googleValidator,
            IGoogleSignInService googleSignInService,
            WorkspaceInitializationApplicator workspaceInit = null,
            Core.Authorization.IWorkspacePolicyTransaction policyTransaction = null,
            ISecurityAuditService? securityAudit = null)
        {
            _otpService = otpService;
            _authService = authService;
            _sessionService = sessionService;
            _mfaGate = mfaGate;
            _userManager = userManager;
            _securityService = securityService;
            _adminService = adminService;
            _configuration = configuration;
            _logger = logger;
            _identityContext = identityContext;
            _apiDb = apiDb;
            _workspaces = workspaces;
            _accountDeletion = accountDeletion;
            _plans = plans;
            _googleValidator = googleValidator;
            _googleSignInService = googleSignInService;
            _workspaceInit = workspaceInit;
            _policyTransaction = policyTransaction;
            _securityAudit = securityAudit;
        }

        private async Task AuditAsync(string eventType, string outcome, string? userId, object? metadata = null)
        {
            if (_securityAudit == null) return;
            try
            {
                await _securityAudit.LogEventAsync(eventType, outcome, userId, null, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Security audit write failed for {EventType}.", eventType);
            }
        }

        private Task AuditSignInFailureAsync(string method, string reason, string? userId = null) =>
            AuditAsync("auth.signin.failure", "failure", userId, new { method, reason });

        [AllowAnonymous]
        [EnableRateLimiting("google-start")]
        [HttpGet("google/start")]
        public IActionResult GoogleStart()
        {
            var enabled = _configuration.GetValue<bool>("Google:Enabled");
            var clientId = _configuration["Google:ClientId"];
            if (!enabled || string.IsNullOrWhiteSpace(clientId))
            {
                return Ok(new { enabled = false });
            }

            var nonceBytes = RandomNumberGenerator.GetBytes(32);
            var nonce = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(nonceBytes);

            Response.Cookies.Append("__Host-ag_gnonce", nonce, new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // explicit per design: container sees plain HTTP behind Worker
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(600)
            });

            return Ok(new { enabled = true, clientId, nonce });
        }

        [AllowAnonymous]
        [EnableRateLimiting("otp-verify")]
        [HttpPost("google")]
        public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Read and then ensure nonce cookie is deleted on every outcome
            Request.Cookies.TryGetValue("__Host-ag_gnonce", out var cookieNonce);
            Response.Cookies.Delete("__Host-ag_gnonce", new CookieOptions
            {
                Path = "/",
                Secure = true,
                SameSite = SameSiteMode.Strict,
                HttpOnly = true
            });

            // 1. Sec-Fetch-Site check
            if (Request.Headers.TryGetValue("Sec-Fetch-Site", out var sfs) && !string.IsNullOrEmpty(sfs))
            {
                if (!string.Equals(sfs, "same-origin", StringComparison.OrdinalIgnoreCase))
                {
                    await AuditSignInFailureAsync("google", "cross_site");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        CreateProblemDetails("Cross-site request prohibited", "Cross-site Google sign-in is not allowed.", StatusCodes.Status403Forbidden, "cross_site"));
                }
            }

            // Check if feature is enabled or configured
            var enabled = _configuration.GetValue<bool>("Google:Enabled");
            var clientId = _configuration["Google:ClientId"];
            if (!enabled || string.IsNullOrWhiteSpace(clientId))
            {
                await AuditSignInFailureAsync("google", "google_disabled");
                return NotFound(CreateProblemDetails("Google Sign-In Disabled", "Google authentication is not enabled or configured on this server.", StatusCodes.Status404NotFound, "google_disabled"));
            }

            // 2. Validate token
            GoogleIdentity identity;
            try
            {
                identity = await _googleValidator.ValidateAsync(request.IdToken, cancellationToken);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Google sign-in failed due to invalid JWT token.");
                await AuditSignInFailureAsync("google", "invalid_token");
                return Unauthorized(CreateProblemDetails("Invalid Google Token", "The provided Google ID token is invalid or expired.", StatusCodes.Status401Unauthorized, "invalid_token"));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Google certificate fetch failed during Google authentication.");
                await AuditSignInFailureAsync("google", "google_unavailable");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    CreateProblemDetails("Google Service Unavailable", "Google certificate service is unavailable right now. Use your email code.", StatusCodes.Status503ServiceUnavailable, "google_unavailable"));
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
            {
                _logger.LogWarning(ex, "Google token validation timed out.");
                await AuditSignInFailureAsync("google", "google_unavailable");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    CreateProblemDetails("Google Service Unavailable", "Google authentication timed out. Use your email code.", StatusCodes.Status503ServiceUnavailable, "google_unavailable"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error validating Google token.");
                await AuditSignInFailureAsync("google", "unexpected_error");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    CreateProblemDetails("Google Authentication Failed", "An unexpected error occurred during Google sign-in. Please try again or contact support.", StatusCodes.Status500InternalServerError, "unexpected_error"));
            }

            // 3. Nonce check
            if (string.IsNullOrWhiteSpace(cookieNonce))
            {
                await AuditSignInFailureAsync("google", "nonce_missing");
                return Unauthorized(CreateProblemDetails("Missing Nonce", "That sign-in expired. Try again.", StatusCodes.Status401Unauthorized, "nonce_missing"));
            }

            if (string.IsNullOrWhiteSpace(identity.Nonce) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(identity.Nonce), Encoding.UTF8.GetBytes(cookieNonce)))
            {
                await AuditSignInFailureAsync("google", "nonce_mismatch");
                return Unauthorized(CreateProblemDetails("Invalid Nonce", "That sign-in expired. Try again.", StatusCodes.Status401Unauthorized, "nonce_mismatch"));
            }

            // 4. One-time redemption check
            var redemption = new ExternalTokenRedemption
            {
                Provider = "Google",
                TokenId = identity.TokenId,
                ExpiresAtUtc = identity.ExpiresAtUtc,
                RedeemedAtUtc = DateTime.UtcNow
            };
            _identityContext.ExternalTokenRedemptions.Add(redemption);
            try
            {
                await _identityContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _identityContext.Entry(redemption).State = EntityState.Detached;

                // Check whether the token was already redeemed (primary-key clash)
                bool isReplay = false;
                try
                {
                    isReplay = await _identityContext.ExternalTokenRedemptions
                        .AsNoTracking()
                        .AnyAsync(r => r.Provider == "Google" && r.TokenId == identity.TokenId, cancellationToken);
                }
                catch (Exception checkEx)
                {
                    _logger.LogError(checkEx, "Failed to verify token redemption replay state after DbUpdateException.");
                }

                if (isReplay)
                {
                    await AuditSignInFailureAsync("google", "token_replayed");
                    return Unauthorized(CreateProblemDetails("Token Replayed", "That sign-in expired. Try again.", StatusCodes.Status401Unauthorized, "token_replayed"));
                }

                _logger.LogError(ex, "Database error recording Google token redemption for TokenId {TokenId}", identity.TokenId);
                await AuditSignInFailureAsync("google", "unexpected_error");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    CreateProblemDetails("Google Authentication Failed", "An unexpected error occurred during Google sign-in. Please try again or contact support.", StatusCodes.Status500InternalServerError, "unexpected_error"));
            }

            // 5. Identity claims check
            if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email))
            {
                await AuditSignInFailureAsync("google", "invalid_token");
                return Unauthorized(CreateProblemDetails("Invalid Google Token", "The provided Google ID token is invalid or unverified.", StatusCodes.Status401Unauthorized, "invalid_token"));
            }

            var allowedHostedDomain = _configuration["Google:HostedDomain"];
            if (!string.IsNullOrEmpty(allowedHostedDomain) &&
                !string.Equals(identity.HostedDomain, allowedHostedDomain, StringComparison.OrdinalIgnoreCase))
            {
                await AuditSignInFailureAsync("google", "hosted_domain_mismatch");
                return StatusCode(StatusCodes.Status403Forbidden,
                    CreateProblemDetails("Hosted Domain Mismatch", "This hosted domain is not permitted for Google sign-in.", StatusCodes.Status403Forbidden, "hosted_domain_mismatch"));
            }

            // 6. Identity Resolution
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();
            var correlationId = HttpContext.TraceIdentifier;

            GoogleSignInResolveResult resolution;
            try
            {
                resolution = await _googleSignInService.ResolveAsync(identity, ip, userAgent, correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error resolving Google identity.");
                await AuditSignInFailureAsync("google", "unexpected_error");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    CreateProblemDetails("Google Authentication Failed", "An unexpected error occurred during Google sign-in. Please try again or contact support.", StatusCodes.Status500InternalServerError, "unexpected_error"));
            }
            if (resolution.User == null)
            {
                var denyCode = resolution.DenyCode ?? "unexpected_error";
                await AuditSignInFailureAsync("google", denyCode);

                return denyCode switch
                {
                    "account_not_found" => NotFound(CreateProblemDetails(
                        "Account Not Found",
                        $"No AccountGo account uses {identity.Email}.",
                        StatusCodes.Status404NotFound,
                        "account_not_found",
                        new Dictionary<string, object?> { ["email"] = identity.Email })),

                    "account_linking_required" => StatusCode(StatusCodes.Status409Conflict, CreateProblemDetails(
                        "Account Linking Required",
                        "Google can't confirm this address for us. Sign in with your email code, then connect Google in Settings.",
                        StatusCodes.Status409Conflict,
                        "account_linking_required")),

                    "google_already_linked" => StatusCode(StatusCodes.Status409Conflict, CreateProblemDetails(
                        "Google Account Already Linked",
                        "Your account is connected to a different Google account.",
                        StatusCodes.Status409Conflict,
                        "google_already_linked")),

                    "account_unavailable" => StatusCode(StatusCodes.Status403Forbidden, CreateProblemDetails(
                        "Account Unavailable",
                        "This account can't sign in. Contact support.",
                        StatusCodes.Status403Forbidden,
                        "account_unavailable")),

                    "account_link_conflict" => StatusCode(StatusCodes.Status409Conflict, CreateProblemDetails(
                        "Account Link Conflict",
                        "Unable to link this Google account due to a concurrent conflict. Sign in with your email code.",
                        StatusCodes.Status409Conflict,
                        "account_link_conflict")),

                    _ => StatusCode(StatusCodes.Status500InternalServerError, CreateProblemDetails(
                        "Google Authentication Failed",
                        "An unexpected error occurred during Google sign-in. Please try again or contact support.",
                        StatusCodes.Status500InternalServerError,
                        "unexpected_error"))
                };
            }

            var user = resolution.User;

            // 7. MFA Gate
            Request.Cookies.TryGetValue("ag_td", out var agTdToken);
            var mfaDecision = await _mfaGate.EvaluateAfterFactorAAsync(user, "google", agTdToken);
            if (mfaDecision.Result == MfaGateResult.Challenge)
            {
                var pendingToken = await _authService.CreateToken(user, purpose: "mfa_pending");
                return Ok(new
                {
                    status = "mfa_required",
                    mfaToken = pendingToken.AccessToken,
                    methods = mfaDecision.Methods,
                    policy = mfaDecision.Policy
                });
            }
            if (mfaDecision.Result == MfaGateResult.Enrollment)
            {
                var enrollToken = await _authService.CreateToken(user, purpose: "mfa_enroll");
                return Ok(new
                {
                    status = "mfa_enrollment_required",
                    mfaToken = enrollToken.AccessToken,
                    methods = mfaDecision.Methods,
                    policy = mfaDecision.Policy
                });
            }

            // 8. Session creation & cookies
            (bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error) result;
            try
            {
                result = await _sessionService.CreateSessionForUserAsync(user, ip, userAgent, amr: "google");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating session for Google sign-in.");
                await AuditSignInFailureAsync("google", "unexpected_error", user.Id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    CreateProblemDetails("Google Authentication Failed", "An unexpected error occurred during Google sign-in. Please try again or contact support.", StatusCodes.Status500InternalServerError, "unexpected_error"));
            }
            if (!result.Success || result.Response == null)
            {
                await AuditSignInFailureAsync("google", "session_creation_failed", user.Id);
                return BadRequest(CreateProblemDetails(
                    "Session Creation Failed",
                    result.Error ?? "Unable to create session.",
                    StatusCodes.Status400BadRequest,
                    "session_creation_failed"));
            }

            if (!string.IsNullOrEmpty(result.RawRefreshToken))
            {
                SetRefreshTokenCookie(result.RawRefreshToken, result.Response?.SessionExpiresAt);
            }

            await AuditAsync("auth.signin.success", "success", user.Id, new { method = "google" });
            return Ok(result.Response);
        }

        private static ProblemDetails CreateProblemDetails(
            string title, string detail, int status, string code, IDictionary<string, object?>? additionalExtensions = null)
        {
            var pd = new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = status
            };
            pd.Extensions["code"] = code;
            if (additionalExtensions != null)
            {
                foreach (var (k, v) in additionalExtensions)
                {
                    pd.Extensions[k] = v;
                }
            }
            return pd;
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
                await AuditAsync("auth.signup", "failure", null, new { reason = "account_exists" });
                return BadRequest(new ProblemDetails
                {
                    Title = "Account Already Exists",
                    Detail = "An account with this email address already exists. Please sign in instead.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // 2. Validate and prepare organization slug ahead of time
            string? cleanSlug = null;
            if (string.IsNullOrWhiteSpace(request.InvitationToken) && !string.IsNullOrWhiteSpace(request.OrganizationName))
            {
                var rawSlug = !string.IsNullOrWhiteSpace(request.OrganizationSlug)
                    ? request.OrganizationSlug
                    : request.OrganizationName;

                cleanSlug = WorkspaceService.NormalizeSlug(rawSlug);
                if (cleanSlug.Length == 0 || cleanSlug.Length > 100)
                {
                    await AuditAsync("auth.signup", "failure", null, new { reason = "invalid_workspace_slug" });
                    return Problem(statusCode: 400, title: "Invalid workspace slug");
                }
            }

            var currency = (request.Currency ?? "USD").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
            {
                await AuditAsync("auth.signup", "failure", null, new { reason = "invalid_currency" });
                return Problem(statusCode: 400, title: "Invalid currency");
            }

            var hasPurpose = !string.IsNullOrWhiteSpace(request.WorkspacePurpose);
            var hasCollaboration = !string.IsNullOrWhiteSpace(request.CollaborationIntent);
            var wantsNewExperience = hasPurpose || hasCollaboration;
            if (wantsNewExperience)
            {
                // Signup validates the shape of the About answers so the client gets early feedback, but it
                // NEVER initializes the workspace, grants access, or sets the single-actor policy here: the
                // email is unverified at this point (EmailConfirmed is false until OTP). Initialization —
                // including the setup switch and supported-scope checks — happens only through the authenticated
                // create/initialization path after the owner verifies and signs in.
                if (!hasPurpose || !hasCollaboration
                    || !Enum.TryParse<Core.Domain.WorkspacePurpose>(request.WorkspacePurpose, ignoreCase: true, out _)
                    || !Enum.TryParse<Core.Domain.WorkspaceCollaborationIntent>(request.CollaborationIntent, ignoreCase: true, out _))
                {
                    await AuditAsync("auth.signup", "failure", null, new { reason = "about_answers_required" });
                    return Problem(statusCode: 400, title: "About answers required",
                        detail: "Both workspacePurpose and collaborationIntent are required for the new workspace experience.");
                }
            }

            // Identity and domain writes share a transaction; failed provisioning or email delivery leaves no orphan.
            var signUpFailureReason = "unexpected_error";
            var signUpSucceeded = false;
            string? signUpUserId = null;
            try
            {
                using (var transaction = await _apiDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
                {
                    _identityContext.Database.SetDbConnection(_apiDb.Database.GetDbConnection());
                    await _identityContext.Database.UseTransactionAsync(transaction.GetDbTransaction());
                    var policyScope = _policyTransaction?.BeginExternalScope();
                    if (cleanSlug != null && _workspaces.SlugExists(cleanSlug))
                    {
                        policyScope?.RolledBack();
                        signUpFailureReason = "workspace_slug_taken";
                        return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                    }

                    var appUser = new ApplicationUser
                    {
                        UserName = normalizedEmail,
                        Email = normalizedEmail,
                        EmailConfirmed = false
                    };
                    signUpUserId = appUser.Id;

                    var createResult = await _userManager.CreateAsync(appUser);
                    if (!createResult.Succeeded)
                    {
                        signUpFailureReason = "registration_failed";
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Registration Failed",
                            Detail = string.Join("; ", createResult.Errors.Select(e => e.Description)),
                            Status = StatusCodes.Status400BadRequest
                        });
                    }

                    _securityService.AddUser(normalizedEmail, normalizedEmail,
                        request.FirstName?.Trim() ?? string.Empty, request.LastName?.Trim() ?? string.Empty, new List<int>());

                    Core.Domain.Company? company = null;
                    if (cleanSlug != null)
                    {
                        // Create the workspace unclassified (name/slug prefill only). Initialization — Full
                        // workspace access and the single-actor journal policy — is deliberately NOT applied
                        // here: the account's email is not yet verified (EmailConfirmed is false until OTP).
                        // Granting access or policy to an unverified address would violate the trusted-audience
                        // boundary. The owner completes About and initializes through the authenticated
                        // create/initialization path after verifying and signing in, where enrollment is
                        // re-checked against the verified identity.
                        company = _workspaces.Create(appUser.Id, request.OrganizationName, cleanSlug, _plans.DefaultPlanKey, currency,
                            markPendingSignupContinuation: true);
                    }

                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    // Keep delivery inside the transaction so a failed send rolls back signup and workspace creation.
                    var result = await _otpService.SendWelcomeOtpAsync(appUser, string.IsNullOrWhiteSpace(request.FirstName) ? "there" : request.FirstName.Trim(), company?.Name, ipAddress);
                    if (!result.Success)
                    {
                        // Transaction will roll back; discard this scope's queued work.
                        policyScope?.RolledBack();
                        signUpFailureReason = "verification_email_failed";
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Verification Email Failed",
                            Detail = result.Error ?? "Failed to deliver verification code email. Please check your address or try again.",
                            Status = StatusCodes.Status400BadRequest
                        });
                    }
                    await transaction.CommitAsync();
                    // Externally-owned transaction committed; drain ONLY this scope's deferred committed-state
                    // verification. Never throws.
                    policyScope?.Committed();
                    signUpSucceeded = true;
                    return Ok(new SignUpResponse(
                        Success: true, Message: result.Message, Email: normalizedEmail, OrgSlug: company?.Slug,
                        ExpiresInSeconds: result.ExpiresInSeconds, DevCode: result.DevCode));
                }
            }
            finally
            {
                // Audit after the signup transaction scope is disposed so the event is not rolled back with it.
                try
                {
                    await _identityContext.Database.UseTransactionAsync((System.Data.Common.DbTransaction?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unable to detach the signup transaction from the identity context before auditing.");
                }
                if (signUpSucceeded)
                    await AuditAsync("auth.signup", "success", signUpUserId);
                else
                    await AuditAsync("auth.signup", "failure", null, new { reason = signUpFailureReason });
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

            Request.Cookies.TryGetValue("ag_td", out var agTdToken);
            var result = await _otpService.VerifyOtpAsync(request.Email, request.Otp, ipAddress, userAgent, agTdToken);

            if (!result.Success)
            {
                await AuditSignInFailureAsync("otp", ClassifyOtpFailure(result.Error));
                return BadRequest(new ProblemDetails
                {
                    Title = "Verification Failed",
                    Detail = result.Error ?? "Invalid or expired code.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (result.Status == "mfa_required")
            {
                return Ok(new
                {
                    status = "mfa_required",
                    mfaToken = result.MfaToken,
                    methods = result.Methods,
                    policy = result.Policy
                });
            }

            if (result.Status == "mfa_enrollment_required")
            {
                return Ok(new
                {
                    status = "mfa_enrollment_required",
                    mfaToken = result.MfaToken,
                    methods = result.Methods,
                    policy = result.Policy
                });
            }

            if (result.Response == null)
            {
                await AuditSignInFailureAsync("otp", ClassifyOtpFailure(result.Error));
                return BadRequest(new ProblemDetails
                {
                    Title = "Verification Failed",
                    Detail = result.Error ?? "Invalid or expired code.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (!string.IsNullOrEmpty(result.RawRefreshToken))
            {
                SetRefreshTokenCookie(result.RawRefreshToken, result.Response?.SessionExpiresAt);
            }

            var verifiedUser = await _userManager.FindByEmailAsync(request.Email.Trim());
            if (verifiedUser == null)
            {
                await AuditSignInFailureAsync("otp", "user_not_found");
                return Unauthorized();
            }
            result.Response.Organizations = (from m in _apiDb.CompanyMembers
                                             join c in _apiDb.Companies on m.CompanyId equals c.Id
                                             where m.UserId == verifiedUser.Id && m.Status == Core.Domain.CompanyMemberStatus.Active
                                             select new OrganizationDto { Slug = c.Slug ?? "", Name = c.Name, Role = m.Role.ToString() }).ToList();
            await AuditAsync("auth.signin.success", "success", verifiedUser.Id, new { method = "otp" });
            return Ok(result.Response);
        }

        private static string ClassifyOtpFailure(string? error)
        {
            if (error != null && error.StartsWith("Code locked", StringComparison.OrdinalIgnoreCase))
                return "otp_locked";
            if (error == null || error.StartsWith("Invalid or expired", StringComparison.OrdinalIgnoreCase))
                return "invalid_or_expired_code";
            return "session_creation_failed";
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
            var orgSlug = Request.Headers["X-AccountGo-Org"].FirstOrDefault()?.Trim();

            var result = await _otpService.RefreshSessionAsync(rawRefreshToken, ipAddress, userAgent, orgSlug);

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
                SetRefreshTokenCookie(result.NewRawRefreshToken, result.Response.SessionExpiresAt);
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

            var permissions = _securityService.GetPermissionsForUser(domainUser);

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

            var permissions = _securityService.GetPermissionsForUser(domainUser);

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
                    await AuditAsync("auth.password.change", "failure", user.Id, new { reason = "current_password_required" });
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
                await AuditAsync("auth.password.change", "failure", user.Id, new { reason = "update_failed" });
                return BadRequest(new ProblemDetails
                {
                    Title = "Password Update Failed",
                    Detail = string.Join("; ", System.Linq.Enumerable.Select(result.Errors, e => e.Description)),
                    Status = StatusCodes.Status400BadRequest
                });
            }

            await AuditAsync("auth.password.change", "success", user.Id);
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
                s.TokenHash == currentHash,
                s.Channel ?? "web",
                s.DeviceLabel,
                s.ClientId
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
            await AuditAsync("auth.session.revoke_others", "success", user.Id, new { revokedCount = otherSessions.Count });
            return Ok(new { success = true, revokedCount = otherSessions.Count });
        }

        public class DeleteAccountRequest
        {
            public string? ConfirmEmail { get; set; }
        }

        [Authorize]
        [HttpGet("account/deletion-blockers")]
        public async Task<IActionResult> DeletionBlockers()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();
            var preview = await _accountDeletion.PreviewAsync(userId);
            return Ok(new
            {
                workspaces = preview.Workspaces.Select(w => new { name = w.Name, slug = w.Slug }),
                reviewerConflicts = preview.ReviewerConflicts.Select(c => new { name = c.Name, slug = c.Slug, reasonCode = c.ReasonCode, message = c.Message }),
                platformAdministrator = preview.PlatformAdministrator
            });
        }

        [Authorize]
        [Authorize(Policy = "MfaStepUp")]
        [HttpDelete("account")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var outcome = await _accountDeletion.DeleteAsync(userId, request?.ConfirmEmail);
            if (outcome.Status == AccountDeletionStatus.Missing)
            {
                return Unauthorized(new ProblemDetails
                {
                    Title = "Account closed",
                    Detail = "This account has been deleted.",
                    Status = StatusCodes.Status401Unauthorized
                });
            }
            if (outcome.Status == AccountDeletionStatus.ConfirmationMismatch)
                return Problem(statusCode: 400, title: AccountDeletionService.ConfirmationTitle);
            if (outcome.Status == AccountDeletionStatus.Blocked)
            {
                var title = outcome.Preview.PlatformAdministrator
                    ? AccountDeletionService.PlatformAdminBlockTitle
                    : outcome.Preview.Workspaces.Count == 0 && outcome.Preview.ReviewerConflicts.Count > 0
                        ? AccountDeletionService.ReviewerConflictTitle
                        : AccountDeletionService.WorkspaceBlockTitle;
                return Problem(statusCode: 409, title: title, extensions: BlockerExtensions(outcome.Preview));
            }

            ClearRefreshTokenCookie();
            return NoContent();
        }

        private static Dictionary<string, object?> BlockerExtensions(AccountDeletionPreview preview)
        {
            return new Dictionary<string, object?>
            {
                ["workspaces"] = preview.Workspaces.Select(w => new Dictionary<string, string>
                {
                    ["name"] = w.Name,
                    ["slug"] = w.Slug
                }).ToList(),
                ["reviewerConflicts"] = preview.ReviewerConflicts.Select(c => new Dictionary<string, string>
                {
                    ["name"] = c.Name,
                    ["slug"] = c.Slug,
                    ["reasonCode"] = c.ReasonCode,
                    ["message"] = c.Message
                }).ToList(),
                ["reasonCode"] = preview.ReviewerConflicts.Count > 0
                    ? Core.Authorization.WorkspacePolicyReasonCodes.LastReviewerUnavailable
                    : null,
                ["platformAdministrator"] = preview.PlatformAdministrator
            };
        }

        // Anonymous so an automatic sign-out still revokes the cookie after the in-memory access token has expired.
        // The refresh cookie is SameSite=Lax and path-scoped, so another site cannot sign the user out.
        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromQuery] string? reason = null)
        {
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            var userId = callerId;
            var revoked = false;
            var rawRefreshToken = Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrEmpty(rawRefreshToken))
            {
                if (userId == null)
                {
                    var hash = SessionService.HashRefreshToken(rawRefreshToken);
                    userId = await _identityContext.RefreshTokens
                        .Where(t => t.TokenHash == hash)
                        .Select(t => t.UserId)
                        .FirstOrDefaultAsync();
                }
                revoked = await _otpService.RevokeSessionAsync(rawRefreshToken);
            }

            ClearRefreshTokenCookie();
            // Record only sign-outs that ended something: a replayed cookie or a second tab signing out the same,
            // already-revoked sign-in writes nothing. Browser-reported reason: "idle" or "expired" (12-hour limit).
            var automatic = reason is "idle" or "expired";
            if (userId != null && automatic && revoked)
                await AuditAsync("auth.session.timeout", "success", userId, new { reason });
            else if (userId != null && !automatic && (revoked || callerId != null))
                await AuditAsync("auth.logout", "success", userId);
            return Ok(new { message = "Logged out successfully" });
        }

        private void SetRefreshTokenCookie(string token, DateTime? sessionExpiresAt)
        {
            var isHttps = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = isHttps,
                // Lax: SPA and API share the registrable domain (or same-origin via proxy); avoids CSRF via cross-site cookie send.
                SameSite = SameSiteMode.Lax,
                // The server enforces idle and absolute limits; the cookie just never outlives the sign-in.
                Expires = sessionExpiresAt ?? DateTime.UtcNow.AddHours(Core.Domain.SessionTimeoutPolicy.DefaultAbsoluteHours),
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
                SameSite = SameSiteMode.Lax,
                Path = "/api"
            });
        }
    }

    public record RefreshTokenRequest(string? RefreshToken);
}
