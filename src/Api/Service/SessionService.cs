// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Dto.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Security;

namespace Api.Service;

public class SessionService : ISessionService
{
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromMinutes(5);
    /// <summary>Max grace sibling issuances per rotated parent (GraceReuseCount). The 4th attempt revokes the family.</summary>
    private const int MaxGraceReuseCount = 3;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationIdentityDbContext _identityContext;
    private readonly IAuthenticationService _authService;
    private readonly ISecurityService _securityService;
    private readonly ApiDbContext? _apiDb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionService> _logger;
    private readonly ISecurityAuditService? _securityAudit;

    public SessionService(
        UserManager<ApplicationUser> userManager,
        ApplicationIdentityDbContext identityContext,
        IAuthenticationService authService,
        ISecurityService securityService,
        IConfiguration configuration,
        ILogger<SessionService> logger,
        ApiDbContext? apiDb = null,
        ISecurityAuditService? securityAudit = null)
    {
        _userManager = userManager;
        _identityContext = identityContext;
        _authService = authService;
        _securityService = securityService;
        _configuration = configuration;
        _logger = logger;
        _apiDb = apiDb;
        _securityAudit = securityAudit;
    }

    /// <summary>Best-effort <c>auth.refresh.reuse</c> event. Never includes token material.</summary>
    private async Task AuditRefreshReuseAsync(RefreshTokenSession session, string reason)
    {
        if (_securityAudit == null) return;
        try
        {
            await _securityAudit.LogEventAsync(
                "auth.refresh.reuse",
                "denied",
                session.UserId,
                null,
                new { reason, familyId = session.FamilyId });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Security audit write failed for auth.refresh.reuse.");
        }
    }

    public Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionForUserAsync(
        ApplicationUser user,
        string? ipAddress,
        string? userAgent,
        string? amr = null)
    {
        var amrList = !string.IsNullOrEmpty(amr) ? new[] { amr } : null;
        return CreateSessionAsync(user, ipAddress, userAgent, amr: amrList);
    }

    public async Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionAsync(
        ApplicationUser user,
        string? ipAddress,
        string? userAgent,
        string? mfaMethod = null,
        int? trustedDeviceId = null,
        IEnumerable<string>? amr = null,
        bool mfaSatisfied = false)
    {
        if (user.DeletedAt != null)
            return (false, null, null, "User account no longer exists.");

        var amrList = amr?.ToList() ?? new List<string>();
        if (mfaSatisfied && !amrList.Contains("mfa"))
        {
            amrList.Add("mfa");
        }
        if (!string.IsNullOrEmpty(mfaMethod) && !amrList.Contains(mfaMethod))
        {
            amrList.Add(mfaMethod);
        }

        var tokenResult = await _authService.CreateToken(
            user,
            purpose: "access",
            lifetime: null,
            amr: amrList,
            mfa: mfaSatisfied,
            mfaTimestamp: mfaSatisfied ? DateTimeOffset.UtcNow : null);

        var rawRefreshToken = GenerateSecureToken();
        var tokenHash = ComputeSha256(rawRefreshToken);
        var now = DateTime.UtcNow;
        var idle = await ResolveIdleTimeoutAsync(user.Id);
        var absoluteExpiresAt = now + AbsoluteLifetime();

        var session = new RefreshTokenSession
        {
            UserId = user.Id,
            FamilyId = NewFamilyId(),
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = IdleExpiry(now, idle, absoluteExpiresAt),
            AbsoluteExpiresAt = absoluteExpiresAt,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            GraceReuseCount = 0,
            MfaMethod = mfaMethod,
            Amr = amrList.Count > 0 ? string.Join(' ', amrList) : null,
            TrustedDeviceId = trustedDeviceId
        };

        _identityContext.RefreshTokens.Add(session);
        await _identityContext.SaveChangesAsync();

        var userDto = await BuildUserDtoAsync(user);
        var expiresInMinutes = Convert.ToInt32(_configuration["JwtSettings:expires"] ?? "15");
        var expiresInSeconds = expiresInMinutes * 60;

        var authResponse = new AuthResponse(
            tokenResult.AccessToken,
            expiresInSeconds,
            userDto)
        {
            IdleTimeoutSeconds = (int)idle.TotalSeconds,
            SessionExpiresAt = absoluteExpiresAt
        };

        return (true, authResponse, rawRefreshToken, null);
    }

    public async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RefreshSessionAsync(
        string rawRefreshToken,
        string? ipAddress,
        string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return (false, null, null, "Missing refresh token.");

        var tokenHash = ComputeSha256(rawRefreshToken);
        var session = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (session == null)
            return (false, null, null, "Invalid session token.");

        EnsureFamilyId(session);

        var account = await _userManager.FindByIdAsync(session.UserId);
        if (account == null || account.DeletedAt != null)
            return (false, null, null, "Session has expired or been revoked.");

        var now = DateTime.UtcNow;
        if (session.IsRevoked)
        {
            var withinGrace = session.ReplacedByTokenHash != null
                && session.RevokedAt.HasValue
                && session.RevokedAt.Value > now - RotationGracePeriod;

            if (withinGrace && session.AbsoluteExpiresAt.HasValue && now >= session.AbsoluteExpiresAt.Value)
            {
                return (false, null, null, "Session has expired. Please sign in again.");
            }

            if (withinGrace)
            {
                // Bound: 4th grace reuse of this parent revokes the family (does not mint).
                if (session.GraceReuseCount >= MaxGraceReuseCount)
                {
                    await RevokeFamilyAsync(
                        session,
                        ipAddress,
                        userAgent,
                        "Grace reuse bound exceeded");
                    await AuditRefreshReuseAsync(session, "grace_reuse_bound_exceeded");
                    return (false, null, null, "Session security violation detected. Please sign in again.");
                }

                // Issue a sibling; keep the existing replacement active (concurrent tabs / lost response).
                session.GraceReuseCount++;
                return await IssueSiblingAsync(session, ipAddress, userAgent, now);
            }

            await RevokeFamilyAsync(
                session,
                ipAddress,
                userAgent,
                "Revoked refresh token reuse outside grace window");
            await AuditRefreshReuseAsync(session, "reuse_outside_grace_window");
            return (false, null, null, "Session security violation detected. Please sign in again.");
        }

        if (session.IsExpired)
        {
            return (false, null, null, "Session has expired. Please sign in again.");
        }

        return await RotateAsync(session, ipAddress, userAgent, now);
    }

    private async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RotateAsync(
        RefreshTokenSession session, string? ipAddress, string? userAgent, DateTime now)
    {
        if (session.IsExpired)
            return (false, null, null, "Session has expired. Please sign in again.");

        var user = await _userManager.FindByIdAsync(session.UserId);
        if (user == null || user.DeletedAt != null)
            return (false, null, null, "User account no longer exists.");

        EnsureFamilyId(session);

        var newRawToken = GenerateSecureToken();
        var newTokenHash = ComputeSha256(newRawToken);

        session.RevokedAt ??= now;
        session.ReplacedByTokenHash = newTokenHash;

        var idle = await ResolveIdleTimeoutAsync(user.Id);
        // Rows from before the 12-hour limit get one from now.
        var absoluteExpiresAt = session.AbsoluteExpiresAt ?? now + AbsoluteLifetime();

        var newSession = new RefreshTokenSession
        {
            UserId = user.Id,
            FamilyId = session.FamilyId,
            TokenHash = newTokenHash,
            CreatedAt = now,
            ExpiresAt = IdleExpiry(now, idle, absoluteExpiresAt),
            AbsoluteExpiresAt = absoluteExpiresAt,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            GraceReuseCount = 0,
            MfaMethod = session.MfaMethod,
            Amr = session.Amr,
            TrustedDeviceId = session.TrustedDeviceId
        };

        _identityContext.RefreshTokens.Add(newSession);
        await _identityContext.SaveChangesAsync();

        return await BuildRefreshResponseAsync(user, newRawToken, idle, absoluteExpiresAt);
    }

    /// <summary>
    /// Grace path: mint another active token in the same family without revoking the prior replacement.
    /// </summary>
    private async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> IssueSiblingAsync(
        RefreshTokenSession parent, string? ipAddress, string? userAgent, DateTime now)
    {
        var user = await _userManager.FindByIdAsync(parent.UserId);
        if (user == null || user.DeletedAt != null)
            return (false, null, null, "User account no longer exists.");

        EnsureFamilyId(parent);

        var newRawToken = GenerateSecureToken();
        var newTokenHash = ComputeSha256(newRawToken);

        var idle = await ResolveIdleTimeoutAsync(user.Id);
        var absoluteExpiresAt = parent.AbsoluteExpiresAt ?? now + AbsoluteLifetime();

        var sibling = new RefreshTokenSession
        {
            UserId = user.Id,
            FamilyId = parent.FamilyId,
            TokenHash = newTokenHash,
            CreatedAt = now,
            ExpiresAt = IdleExpiry(now, idle, absoluteExpiresAt),
            AbsoluteExpiresAt = absoluteExpiresAt,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            GraceReuseCount = 0,
            MfaMethod = parent.MfaMethod,
            Amr = parent.Amr,
            TrustedDeviceId = parent.TrustedDeviceId
        };

        _identityContext.RefreshTokens.Add(sibling);
        await _identityContext.SaveChangesAsync();

        return await BuildRefreshResponseAsync(user, newRawToken, idle, absoluteExpiresAt);
    }

    private async Task RevokeFamilyAsync(
        RefreshTokenSession trigger,
        string? ipAddress,
        string? userAgent,
        string reason)
    {
        EnsureFamilyId(trigger);
        var familyId = trigger.FamilyId;

        _logger.LogWarning(
            "SECURITY ALERT: {Reason}. Revoking refresh-token family. UserId={UserId} FamilyId={FamilyId} Ip={Ip} UserAgent={UserAgent}",
            reason,
            trigger.UserId,
            familyId,
            ipAddress,
            userAgent);

        var now = DateTime.UtcNow;
        var familySessions = await _identityContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();

        foreach (var s in familySessions)
            s.RevokedAt = now;

        await _identityContext.SaveChangesAsync();
    }

    private async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> BuildRefreshResponseAsync(
        ApplicationUser user,
        string newRawToken,
        TimeSpan idle,
        DateTime absoluteExpiresAt)
    {
        var tokenResult = await _authService.CreateToken(user, populateExp: false);
        var userDto = await BuildUserDtoAsync(user);
        var expiresInMinutes = Convert.ToInt32(_configuration["JwtSettings:expires"] ?? "15");
        var expiresInSeconds = expiresInMinutes * 60;

        var authResponse = new AuthResponse(
            tokenResult.AccessToken,
            expiresInSeconds,
            userDto)
        {
            IdleTimeoutSeconds = (int)idle.TotalSeconds,
            SessionExpiresAt = absoluteExpiresAt
        };

        return (true, authResponse, newRawToken, null);
    }

    private static void EnsureFamilyId(RefreshTokenSession session)
    {
        if (!string.IsNullOrEmpty(session.FamilyId))
            return;
        // Pre-migration / test rows: stable family from Id when available, else new GUID.
        session.FamilyId = session.Id > 0 ? session.Id.ToString() : NewFamilyId();
    }

    private static string NewFamilyId() => Guid.NewGuid().ToString("N");

    public async Task<bool> RevokeSessionAsync(string rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return false;

        var tokenHash = ComputeSha256(rawRefreshToken);
        var session = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        if (session == null)
            return false;

        // Sign-out ends the whole sign-in: grace siblings held by other tabs must not keep it alive.
        EnsureFamilyId(session);
        var now = DateTime.UtcNow;
        var familyId = session.FamilyId;
        var active = await _identityContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();
        // Rows without a stored FamilyId only get one in memory, so the query above can miss the cookie's own row.
        if (session.RevokedAt == null && !active.Contains(session))
            active.Add(session);
        if (active.Count == 0)
            return false;

        foreach (var t in active)
            t.RevokedAt = now;
        await _identityContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RevokeAllSessionsForUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var activeSessions = await _identityContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();

        if (activeSessions.Count == 0)
            return true;

        var now = DateTime.UtcNow;
        foreach (var s in activeSessions)
        {
            s.RevokedAt = now;
        }

        await _identityContext.SaveChangesAsync();
        return true;
    }

    public async Task<UserDto> BuildUserDtoAsync(ApplicationUser user)
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

        var permissions = _securityService.GetPermissionsForUser(domainUser);

        return new UserDto(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            firstName,
            lastName,
            roles,
            permissions,
            user.PhoneNumber,
            !string.IsNullOrEmpty(user.PasswordHash),
            user.MfaReEnrollRequired);
    }

    /// <summary>Strictest idle timeout across the user's active, non-deleted workspaces.</summary>
    private async Task<TimeSpan> ResolveIdleTimeoutAsync(string userId)
    {
        var minutes = SessionTimeoutPolicy.DefaultIdleMinutes;
        if (_apiDb != null)
        {
            var strictest = await _apiDb.CompanyMembers
                .IgnoreQueryFilters()
                .Where(m => m.UserId == userId
                    && m.Status == CompanyMemberStatus.Active
                    && m.Company.DeletedAt == null)
                .Join(
                    _apiDb.CompanySettings.IgnoreQueryFilters(),
                    m => m.CompanyId,
                    s => s.CompanyId,
                    (m, s) => (int?)s.IdleTimeoutMinutes)
                .MinAsync();
            if (strictest.HasValue)
                minutes = SessionTimeoutPolicy.Normalize(strictest.Value);
        }
        return TimeSpan.FromMinutes(minutes);
    }

    private TimeSpan AbsoluteLifetime() => TimeSpan.FromHours(AbsoluteHours(_configuration));

    /// <summary>Hours from sign-in until the session ends regardless of activity (<c>Session:AbsoluteHours</c>).</summary>
    internal static int AbsoluteHours(IConfiguration configuration) =>
        int.TryParse(configuration["Session:AbsoluteHours"], out var hours) && hours > 0
            ? hours
            : SessionTimeoutPolicy.DefaultAbsoluteHours;

    /// <summary>
    /// Idle deadline for a refresh token issued now. Slack covers the browser's activity-refresh interval, so the
    /// browser (which knows about mouse and keyboard activity) always signs out first while a tab is open.
    /// </summary>
    private static DateTime IdleExpiry(DateTime now, TimeSpan idle, DateTime absoluteExpiresAt)
    {
        var idleExpiry = now + idle + SessionTimeoutPolicy.ServerSlack;
        return idleExpiry < absoluteExpiresAt ? idleExpiry : absoluteExpiresAt;
    }

    internal static string HashRefreshToken(string rawRefreshToken) => ComputeSha256(rawRefreshToken);

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
}
