// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
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

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationIdentityDbContext _identityContext;
    private readonly IAuthenticationService _authService;
    private readonly ISecurityService _securityService;
    private readonly ApiDbContext? _apiDb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        UserManager<ApplicationUser> userManager,
        ApplicationIdentityDbContext identityContext,
        IAuthenticationService authService,
        ISecurityService securityService,
        IConfiguration configuration,
        ILogger<SessionService> logger,
        ApiDbContext? apiDb = null)
    {
        _userManager = userManager;
        _identityContext = identityContext;
        _authService = authService;
        _securityService = securityService;
        _configuration = configuration;
        _logger = logger;
        _apiDb = apiDb;
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
        var expiresInMinutes = Convert.ToInt32(_configuration["JwtSettings:expires"] ?? "15");
        var expiresInSeconds = expiresInMinutes * 60;

        var authResponse = new AuthResponse(
            tokenResult.AccessToken,
            expiresInSeconds,
            userDto);

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

        var account = await _userManager.FindByIdAsync(session.UserId);
        if (account == null || account.DeletedAt != null)
            return (false, null, null, "Session has expired or been revoked.");

        var now = DateTime.UtcNow;
        if (session.IsRevoked)
        {
            if (session.ReplacedByTokenHash != null && session.RevokedAt > now - RotationGracePeriod)
            {
                var replacement = await _identityContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == session.ReplacedByTokenHash);
                if (replacement is { RevokedAt: null })
                    replacement.RevokedAt = now;
                return await RotateAsync(session, ipAddress, userAgent, now);
            }

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

    private async Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RotateAsync(
        RefreshTokenSession session, string? ipAddress, string? userAgent, DateTime now)
    {
        if (session.IsExpired)
            return (false, null, null, "Session has expired. Please sign in again.");

        var user = await _userManager.FindByIdAsync(session.UserId);
        if (user == null || user.DeletedAt != null)
            return (false, null, null, "User account no longer exists.");

        var newRawToken = GenerateSecureToken();
        var newTokenHash = ComputeSha256(newRawToken);

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

        var tokenResult = await _authService.CreateToken(user, populateExp: false);
        var userDto = await BuildUserDtoAsync(user);
        var expiresInMinutes = Convert.ToInt32(_configuration["JwtSettings:expires"] ?? "15");
        var expiresInSeconds = expiresInMinutes * 60;

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

        var permissions = _securityService.GetPermissionsForUser(user.UserName ?? user.Email ?? string.Empty);

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
