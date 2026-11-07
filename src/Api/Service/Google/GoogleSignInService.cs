using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Service.Google;

public class GoogleSignInService : IGoogleSignInService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationIdentityDbContext _identityContext;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly ILogger<GoogleSignInService> _logger;

    public GoogleSignInService(
        UserManager<ApplicationUser> userManager,
        ApplicationIdentityDbContext identityContext,
        ISecurityAuditService securityAuditService,
        ILogger<GoogleSignInService> logger)
    {
        _userManager = userManager;
        _identityContext = identityContext;
        _securityAuditService = securityAuditService;
        _logger = logger;
    }

    public async Task<GoogleSignInResolveResult> ResolveAsync(
        GoogleIdentity identity,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        // 1. Find by login (Google, sub).
        var user = await _userManager.FindByLoginAsync("Google", identity.Subject);
        if (user != null)
        {
            if (!UserEligibility.IsEligible(user))
            {
                return new GoogleSignInResolveResult(null, "account_unavailable");
            }
            return new GoogleSignInResolveResult(user, null);
        }

        // 2. Not found: verify email authority.
        if (!GoogleIdentityPolicy.IsAuthoritativeEmail(identity.Email, identity.HostedDomain))
        {
            return new GoogleSignInResolveResult(null, "account_linking_required");
        }

        // 3. Find by email. Must exist, not deleted, and email confirmed.
        user = await _userManager.FindByEmailAsync(identity.Email);
        if (user == null || user.DeletedAt != null || !user.EmailConfirmed)
        {
            return new GoogleSignInResolveResult(null, "account_not_found");
        }

        if (!UserEligibility.IsEligible(user))
        {
            return new GoogleSignInResolveResult(null, "account_unavailable");
        }

        // 4. User already has a Google login with a different sub.
        var logins = await _userManager.GetLoginsAsync(user);
        if (logins.Any(l => l.LoginProvider == "Google" && l.ProviderKey != identity.Subject))
        {
            return new GoogleSignInResolveResult(null, "google_already_linked");
        }

        // 5. Link with AddLoginAsync and handle unique-key race gracefully.
        bool linkSucceeded = false;
        try
        {
            var linkResult = await _userManager.AddLoginAsync(user, new UserLoginInfo("Google", identity.Subject, "Google"));
            linkSucceeded = linkResult.Succeeded;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "DbUpdateException during AddLoginAsync for Google sub {Sub} and user {UserId}", identity.Subject, user.Id);
            linkSucceeded = false;
        }

        if (!linkSucceeded)
        {
            // Drop only the failed login entry from the change tracker
            var loginEntry = _identityContext.ChangeTracker.Entries<IdentityUserLogin<string>>()
                .FirstOrDefault(e => e.Entity.LoginProvider == "Google" && e.Entity.ProviderKey == identity.Subject);
            if (loginEntry != null)
            {
                loginEntry.State = EntityState.Detached;
            }

            // Detach user so FindByLoginAsync can reload fresh state
            var userEntry = _identityContext.Entry(user);
            if (userEntry != null)
            {
                userEntry.State = EntityState.Detached;
            }

            var existingLinkUser = await _userManager.FindByLoginAsync("Google", identity.Subject);
            if (existingLinkUser != null && existingLinkUser.Id == user.Id)
            {
                // Another concurrent request linked the same user; proceed safely.
                user = existingLinkUser;
            }
            else
            {
                return new GoogleSignInResolveResult(null, "account_link_conflict");
            }
        }
        else
        {
            // 6. Audit event: auth.external_login.linked (provider, method, userId, SHA-256 of sub).
            try
            {
                var subHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Subject))).ToLowerInvariant();
                await _securityAuditService.LogEventAsync(
                    "auth.external_login.linked",
                    "success",
                    user.Id,
                    null,
                    new { provider = "Google", method = "auto_email", subjectHash = subHash });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit event for linked Google sub {Sub} and user {UserId}", identity.Subject, user.Id);
            }
        }

        return new GoogleSignInResolveResult(user, null);
    }
}
