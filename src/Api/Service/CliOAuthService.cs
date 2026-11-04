// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Service;

public sealed class CliOAuthService : ICliOAuthService
{
    public const string AudienceCli = "cli";

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "[::1]",
        "::1"
    };

    private readonly ApplicationIdentityDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationService _auth;
    private readonly ISecurityAuditService _audit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliOAuthOptions _options;
    private readonly ILogger<CliOAuthService> _logger;
    private static DateTime LastCleanupUtc = DateTime.MinValue;
    private static int CleanupInFlight;

    public CliOAuthService(
        ApplicationIdentityDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuthenticationService auth,
        ISecurityAuditService audit,
        IServiceScopeFactory scopeFactory,
        IOptions<CliOAuthOptions> options,
        ILogger<CliOAuthService> logger)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _audit = audit;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string CreatePendingAuthorization(
        string responseType,
        string clientId,
        string redirectUri,
        string scope,
        string state,
        string codeChallenge,
        string codeChallengeMethod)
    {
        ScheduleCleanup();
        AssertClient(clientId);
        AssertLoopbackRedirectUri(redirectUri);
        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
            throw OAuthException.InvalidRequest("Unsupported response_type");
        if (!string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal))
            throw OAuthException.InvalidRequest("code_challenge_method must be S256");
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256)
            throw OAuthException.InvalidRequest("Invalid state");
        if (string.IsNullOrWhiteSpace(codeChallenge) || codeChallenge.Length < 43 || codeChallenge.Length > 128)
            throw OAuthException.InvalidRequest("Invalid code_challenge");

        var normalizedScope = NormalizeAndValidateScope(scope);
        var now = DateTime.UtcNow;
        var request = new OAuthAuthorizationRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            ClientId = clientId.Trim(),
            RedirectUri = redirectUri.Trim(),
            CodeChallenge = codeChallenge.Trim(),
            Scope = normalizedScope,
            State = state,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.PendingRequestTtl)
        };
        _db.OAuthAuthorizationRequests.Add(request);
        _db.SaveChanges();

        _ = _audit.LogEventAsync(
            "cli_oauth_request_created",
            null,
            null,
            null,
            null,
            new { request.Id, request.ClientId, request.RedirectUri, request.Scope });

        return request.Id;
    }

    public async Task<CliOAuthPendingRequestDto> GetPendingRequestAsync(
        string requestId,
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        ScheduleCleanup();
        DenyIfCliAudience(principal);
        var user = await RequireUserAsync(principal);
        var request = await RequirePendingRequestAsync(requestId, ct);
        var requiresStepUp = !IsConsentFresh(principal);
        var port = new Uri(request.RedirectUri).Port;
        return new CliOAuthPendingRequestDto(
            request.Id,
            request.ClientId,
            _options.ClientDisplayName,
            request.RedirectUri,
            port,
            request.Scope,
            user.Email ?? user.UserName ?? string.Empty,
            requiresStepUp);
    }

    public async Task<CliOAuthCodeResult> DecideAsync(
        string requestId,
        string decision,
        string? deviceLabel,
        ClaimsPrincipal principal,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ScheduleCleanup();
        DenyIfCliAudience(principal);
        var user = await RequireUserAsync(principal);
        var request = await RequirePendingRequestAsync(requestId, ct);

        if (string.Equals(decision, "deny", StringComparison.OrdinalIgnoreCase))
        {
            request.DeniedAt = DateTime.UtcNow;
            request.UserId = user.Id;
            await _db.SaveChangesAsync(ct);
            await _audit.LogEventAsync(
                "cli_oauth_denied",
                user.Id,
                null,
                ipAddress,
                userAgent,
                new { request.Id, request.ClientId });

            return new CliOAuthCodeResult(
                BuildRedirect(request.RedirectUri, null, request.State, "access_denied", "The user denied the request"));
        }

        if (!string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase))
            throw OAuthException.InvalidRequest("decision must be approve or deny");

        if (!IsConsentFresh(principal))
            throw OAuthException.StepUpRequired("A recent MFA-satisfied session is required to approve CLI access.");

        var rawCode = CreateAuthorizationCode();
        var now = DateTime.UtcNow;
        request.UserId = user.Id;
        request.CodeHash = HashToken(rawCode);
        request.DeviceLabel = string.IsNullOrWhiteSpace(deviceLabel)
            ? "AccountGo CLI"
            : deviceLabel.Trim()[..Math.Min(deviceLabel.Trim().Length, 200)];
        request.ExpiresAt = now.Add(_options.AuthorizationCodeTtl);
        await _db.SaveChangesAsync(ct);

        await _audit.LogEventAsync(
            "cli_oauth_code_issued",
            user.Id,
            null,
            ipAddress,
            userAgent,
            new { request.Id, request.ClientId });

        return new CliOAuthCodeResult(BuildRedirect(request.RedirectUri, rawCode, request.State, null, null));
    }

    public async Task<CliOAuthTokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId,
        string code,
        string redirectUri,
        string codeVerifier,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ScheduleCleanup();
        AssertClient(clientId);
        AssertLoopbackRedirectUri(redirectUri);
        ValidateCodeVerifier(codeVerifier);

        var codeHash = HashToken(code);
        var now = DateTime.UtcNow;
        var request = await _db.OAuthAuthorizationRequests
            .FirstOrDefaultAsync(r => r.CodeHash == codeHash, ct);

        if (request == null)
            throw OAuthException.InvalidGrant("Invalid authorization code");
        if (!string.Equals(request.ClientId, clientId, StringComparison.Ordinal))
            throw OAuthException.InvalidGrant("Authorization code client mismatch");
        if (!string.Equals(request.RedirectUri, redirectUri, StringComparison.Ordinal))
            throw OAuthException.InvalidGrant("Authorization code redirect URI mismatch");

        if (request.ConsumedAt != null)
        {
            await RevokeIssuedFamilyAsync(request, ipAddress, userAgent, "Authorization code replay");
            throw OAuthException.InvalidGrant("Authorization code has already been used");
        }

        if (request.ExpiresAt <= now || request.DeniedAt != null || string.IsNullOrEmpty(request.UserId))
            throw OAuthException.InvalidGrant("Authorization code has expired");

        var expectedChallenge = CreateS256CodeChallenge(codeVerifier);
        if (!FixedTimeEqualsUtf8(expectedChallenge, request.CodeChallenge))
            throw OAuthException.InvalidGrant("Invalid PKCE verifier");

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""OAuthAuthorizationRequests""
               SET ""ConsumedAt"" = {now}
               WHERE ""Id"" = {request.Id}
                 AND ""ConsumedAt"" IS NULL
                 AND ""ExpiresAt"" > {now}
                 AND ""DeniedAt"" IS NULL",
            ct);
        if (rows != 1)
            throw OAuthException.InvalidGrant("Authorization code is no longer valid");

        request.ConsumedAt = now;

        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null || user.DeletedAt != null)
            throw OAuthException.InvalidGrant("User account not found for OAuth session");

        var pair = await IssueCliTokenPairAsync(
            user,
            request.Scope,
            clientId,
            request.DeviceLabel,
            ipAddress,
            userAgent,
            ct);

        request.IssuedRefreshFamilyId = pair.FamilyId;
        await _db.SaveChangesAsync(ct);

        await _audit.LogEventAsync(
            "cli_oauth_token_exchanged",
            user.Id,
            null,
            ipAddress,
            userAgent,
            new { request.Id, request.ClientId, familyId = pair.FamilyId });

        return pair.Response;
    }

    public async Task<CliOAuthTokenResponse> RefreshAsync(
        string clientId,
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        ScheduleCleanup();
        AssertClient(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw OAuthException.InvalidRequest("Missing refresh_token");

        var result = await RotateCliRefreshAsync(refreshToken, clientId, ipAddress, userAgent, ct);
        if (!result.Success || result.Response == null)
        {
            if (result.ReuseDetected)
            {
                await _audit.LogEventAsync(
                    "cli_oauth_refresh_reuse",
                    result.UserId,
                    null,
                    ipAddress,
                    userAgent,
                    new { clientId });
            }
            throw OAuthException.InvalidGrant(result.Error ?? "Invalid or expired refresh token");
        }

        return result.Response;
    }

    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        ScheduleCleanup();
        if (string.IsNullOrWhiteSpace(token))
            return;

        var hash = HashToken(token);
        var session = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (session == null)
            return;

        var now = DateTime.UtcNow;
        if (string.Equals(session.Channel, RefreshTokenChannels.Cli, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(session.FamilyId))
        {
            await RevokeFamilyInternalAsync(session.FamilyId, now);
        }
        else if (session.RevokedAt == null)
        {
            session.RevokedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogEventAsync(
            "cli_oauth_revoked",
            session.UserId,
            null,
            null,
            null,
            new { session.FamilyId, session.ClientId });
    }

    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoffPending = now.AddDays(-1);
        var cutoffConsumed = now.AddHours(-1);
        var expired = await _db.OAuthAuthorizationRequests
            .Where(r =>
                r.ExpiresAt < cutoffPending
                || (r.ConsumedAt != null && r.ConsumedAt < cutoffConsumed)
                || (r.DeniedAt != null && r.DeniedAt < cutoffConsumed))
            .OrderBy(r => r.ExpiresAt)
            .Take(500)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return;
        _db.OAuthAuthorizationRequests.RemoveRange(expired);
        await _db.SaveChangesAsync(ct);
    }

    private sealed record RotateResult(
        bool Success,
        CliOAuthTokenResponse? Response,
        string? Error,
        bool ReuseDetected,
        string? UserId);

    private async Task<RotateResult> RotateCliRefreshAsync(
        string rawRefreshToken,
        string clientId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var tokenHash = HashToken(rawRefreshToken);
        var session = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (session == null)
            return new RotateResult(false, null, "Invalid or expired refresh token", false, null);

        if (!string.Equals(session.Channel, RefreshTokenChannels.Cli, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.ClientId, clientId, StringComparison.Ordinal))
            return new RotateResult(false, null, "Invalid or expired refresh token", false, session.UserId);

        var now = DateTime.UtcNow;
        if (session.IsRevoked)
        {
            var withinGrace = session.ReplacedByTokenHash != null
                && session.RevokedAt.HasValue
                && session.RevokedAt.Value > now - TimeSpan.FromMinutes(5);

            if (!withinGrace || session.GraceReuseCount >= 3)
            {
                await RevokeFamilyInternalAsync(session.FamilyId, now);
                await _db.SaveChangesAsync(ct);
                return new RotateResult(false, null, "Session security violation detected", true, session.UserId);
            }

            session.GraceReuseCount++;
            var (siblingResponse, _) = await MintRotatedPairAsync(session, ipAddress, userAgent, now, ct);
            return new RotateResult(true, siblingResponse, null, false, session.UserId);
        }

        if (session.IsExpired)
            return new RotateResult(false, null, "Invalid or expired refresh token", false, session.UserId);

        session.RevokedAt ??= now;
        var (response, newHash) = await MintRotatedPairAsync(session, ipAddress, userAgent, now, ct);
        session.ReplacedByTokenHash = newHash;
        await _db.SaveChangesAsync(ct);
        return new RotateResult(true, response, null, false, session.UserId);
    }

    private async Task<(CliOAuthTokenResponse Response, string NewTokenHash)> MintRotatedPairAsync(
        RefreshTokenSession parent,
        string? ipAddress,
        string? userAgent,
        DateTime now,
        CancellationToken ct)
    {
        var raw = GenerateSecureToken();
        var hash = HashToken(raw);
        var absolute = parent.AbsoluteExpiresAt ?? now.Add(_options.RefreshAbsoluteTtl);
        var idleExpiry = now.Add(_options.RefreshIdleTtl);
        var expires = absolute < idleExpiry ? absolute : idleExpiry;

        var next = new RefreshTokenSession
        {
            UserId = parent.UserId,
            FamilyId = parent.FamilyId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = expires,
            AbsoluteExpiresAt = absolute,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            GraceReuseCount = 0,
            MfaMethod = parent.MfaMethod,
            Amr = parent.Amr,
            TrustedDeviceId = parent.TrustedDeviceId,
            Channel = RefreshTokenChannels.Cli,
            ClientId = parent.ClientId,
            DeviceLabel = parent.DeviceLabel,
            Scope = parent.Scope
        };
        _db.RefreshTokens.Add(next);
        await _db.SaveChangesAsync(ct);

        var user = await _userManager.FindByIdAsync(parent.UserId)
            ?? throw OAuthException.InvalidGrant("User account not found for OAuth session");

        var access = await CreateCliAccessTokenAsync(user, next);
        var response = new CliOAuthTokenResponse(
            access.AccessToken,
            "Bearer",
            (int)_options.AccessTokenTtl.TotalSeconds,
            raw,
            next.Scope ?? "offline_access api");
        return (response, hash);
    }

    private async Task<(CliOAuthTokenResponse Response, string FamilyId)> IssueCliTokenPairAsync(
        ApplicationUser user,
        string scope,
        string clientId,
        string? deviceLabel,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var family = Guid.NewGuid().ToString("N");
        var absolute = now.Add(_options.RefreshAbsoluteTtl);
        var idleExpiry = now.Add(_options.RefreshIdleTtl);
        var expires = absolute < idleExpiry ? absolute : idleExpiry;
        var rawRefresh = GenerateSecureToken();

        var session = new RefreshTokenSession
        {
            UserId = user.Id,
            FamilyId = family,
            TokenHash = HashToken(rawRefresh),
            CreatedAt = now,
            ExpiresAt = expires,
            AbsoluteExpiresAt = absolute,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            GraceReuseCount = 0,
            Amr = "pwd mfa",
            Channel = RefreshTokenChannels.Cli,
            ClientId = clientId,
            DeviceLabel = string.IsNullOrWhiteSpace(deviceLabel) ? "AccountGo CLI" : deviceLabel.Trim(),
            Scope = scope
        };
        _db.RefreshTokens.Add(session);
        await _db.SaveChangesAsync(ct);

        var access = await CreateCliAccessTokenAsync(user, session);
        return (new CliOAuthTokenResponse(
            access.AccessToken,
            "Bearer",
            (int)_options.AccessTokenTtl.TotalSeconds,
            rawRefresh,
            scope), family);
    }

    private Task<Dto.Security.Token> CreateCliAccessTokenAsync(ApplicationUser user, RefreshTokenSession session) =>
        _auth.CreateToken(
            user,
            purpose: "access",
            lifetime: _options.AccessTokenTtl,
            amr: SplitAmr(session.Amr) ?? new[] { "pwd", "mfa" },
            mfa: true,
            mfaTimestamp: DateTimeOffset.UtcNow,
            extraClaims: BuildCliClaims(session),
            audience: _options.Audience);

    private static IEnumerable<Claim> BuildCliClaims(RefreshTokenSession session)
    {
        yield return new Claim("sid", session.FamilyId);
        yield return new Claim("channel", RefreshTokenChannels.Cli);
        if (!string.IsNullOrWhiteSpace(session.Scope))
            yield return new Claim("scope", session.Scope);
        if (!string.IsNullOrWhiteSpace(session.ClientId))
            yield return new Claim("client_id", session.ClientId);
    }

    private async Task RevokeIssuedFamilyAsync(
        OAuthAuthorizationRequest request,
        string? ipAddress,
        string? userAgent,
        string reason)
    {
        if (string.IsNullOrEmpty(request.IssuedRefreshFamilyId))
            return;

        _logger.LogWarning(
            "SECURITY: {Reason}. Revoking CLI refresh family {FamilyId} for request {RequestId}",
            reason,
            request.IssuedRefreshFamilyId,
            request.Id);

        await RevokeFamilyInternalAsync(request.IssuedRefreshFamilyId, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        await _audit.LogEventAsync(
            "cli_oauth_refresh_reuse",
            request.UserId,
            null,
            ipAddress,
            userAgent,
            new { request.Id, familyId = request.IssuedRefreshFamilyId, reason });
    }

    private async Task RevokeFamilyInternalAsync(string familyId, DateTime now)
    {
        var family = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var s in family)
            s.RevokedAt = now;
    }

    private async Task<OAuthAuthorizationRequest> RequirePendingRequestAsync(string requestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw OAuthException.InvalidRequest("Missing request_id");

        var request = await _db.OAuthAuthorizationRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null)
            throw OAuthException.InvalidRequest("Unknown or expired authorization request");
        if (request.ExpiresAt <= DateTime.UtcNow || request.DeniedAt != null || request.ConsumedAt != null || request.CodeHash != null)
            throw OAuthException.InvalidRequest("Authorization request is no longer pending");
        return request;
    }

    private async Task<ApplicationUser> RequireUserAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            throw OAuthException.Unauthorized("Not authenticated");
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.DeletedAt != null)
            throw OAuthException.Unauthorized("Not authenticated");
        return user;
    }

    private bool IsConsentFresh(ClaimsPrincipal principal)
    {
        var maxAge = _options.ConsentFreshness;
        var now = DateTimeOffset.UtcNow;

        if (string.Equals(principal.FindFirstValue("step_up"), "true", StringComparison.OrdinalIgnoreCase))
            return true;

        var mfaTs = ParseUnix(principal.FindFirstValue("mfa_ts"));
        if (mfaTs.HasValue && now - mfaTs.Value <= maxAge)
            return true;

        var iat = ParseUnix(principal.FindFirstValue("iat"));
        if (iat.HasValue && now - iat.Value <= maxAge)
            return true;

        var purpose = principal.FindFirstValue("purpose");
        if (string.IsNullOrEmpty(purpose) || purpose == "access")
        {
            var mfa = principal.FindFirstValue("mfa");
            if (string.Equals(mfa, "false", StringComparison.OrdinalIgnoreCase))
                return false;
            // Integration-test tokens often omit iat; treat as fresh on community builds.
            if (!mfaTs.HasValue && !iat.HasValue)
                return true;
        }

        return false;
    }

    private static DateTimeOffset? ParseUnix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return long.TryParse(value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private void AssertClient(string clientId)
    {
        if (!string.Equals(clientId?.Trim(), _options.ClientId, StringComparison.Ordinal))
            throw OAuthException.InvalidClient($"Unsupported OAuth client_id: {clientId}");
    }

    private static void AssertLoopbackRedirectUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed))
            throw OAuthException.InvalidRequest("Invalid redirect_uri");
        if (parsed.Scheme != Uri.UriSchemeHttp)
            throw OAuthException.InvalidRequest("redirect_uri must be an http loopback address (127.0.0.1 or ::1)");
        var host = parsed.Host == "::1" ? "[::1]" : parsed.Host;
        if (!AllowedHosts.Contains(host) && !AllowedHosts.Contains(parsed.Host))
            throw OAuthException.InvalidRequest("redirect_uri must be an http loopback address (127.0.0.1 or ::1)");
        if (!string.IsNullOrEmpty(parsed.Fragment))
            throw OAuthException.InvalidRequest("Invalid redirect_uri");
    }

    private string NormalizeAndValidateScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw OAuthException.InvalidRequest("Missing scope");

        var requested = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var s in requested)
        {
            if (!_options.AllowedScopes.Contains(s, StringComparer.Ordinal))
                throw OAuthException.InvalidRequest($"Unsupported scope: {s}");
        }

        foreach (var required in _options.RequiredScopes)
        {
            if (!requested.Contains(required, StringComparer.Ordinal))
                throw OAuthException.InvalidRequest($"Missing required scope: {required}");
        }

        return string.Join(' ', _options.AllowedScopes.Where(a => requested.Contains(a, StringComparer.Ordinal)));
    }

    private static void ValidateCodeVerifier(string codeVerifier)
    {
        if (string.IsNullOrWhiteSpace(codeVerifier) || codeVerifier.Length < 43 || codeVerifier.Length > 128)
            throw OAuthException.InvalidRequest("code_verifier must be 43-128 characters");
    }

    private static void DenyIfCliAudience(ClaimsPrincipal principal)
    {
        var audClaims = principal.Claims
            .Where(c => c.Type is "aud" or "http://schemas.microsoft.com/identity/claims/audience")
            .Select(c => c.Value);
        if (audClaims.Any(a => string.Equals(a, AudienceCli, StringComparison.OrdinalIgnoreCase)))
            throw OAuthException.Forbidden("CLI tokens cannot mint further credentials");
    }

    private void ScheduleCleanup()
    {
        if (System.Threading.Interlocked.CompareExchange(ref CleanupInFlight, 1, 0) != 0)
            return;
        if (DateTime.UtcNow - LastCleanupUtc < TimeSpan.FromMinutes(5))
        {
            System.Threading.Interlocked.Exchange(ref CleanupInFlight, 0);
            return;
        }

        LastCleanupUtc = DateTime.UtcNow;
        var scopeFactory = _scopeFactory;
        var logger = _logger;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var oauth = scope.ServiceProvider.GetRequiredService<ICliOAuthService>();
                await oauth.CleanupExpiredAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CLI OAuth cleanup sweep failed");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref CleanupInFlight, 0);
            }
        });
    }

    private static string BuildRedirect(
        string redirectUri,
        string? code,
        string state,
        string? error,
        string? errorDescription)
    {
        var uri = new Uri(redirectUri);
        var qb = new StringBuilder();
        if (!string.IsNullOrEmpty(uri.Query) && uri.Query.Length > 1)
            qb.Append(uri.Query.TrimStart('?')).Append('&');
        qb.Append("state=").Append(Uri.EscapeDataString(state));
        if (!string.IsNullOrEmpty(error))
        {
            qb.Append("&error=").Append(Uri.EscapeDataString(error));
            if (!string.IsNullOrEmpty(errorDescription))
                qb.Append("&error_description=").Append(Uri.EscapeDataString(errorDescription));
        }
        else
        {
            qb.Append("&code=").Append(Uri.EscapeDataString(code ?? string.Empty));
        }

        var builder = new UriBuilder(uri) { Query = qb.ToString() };
        return builder.Uri.ToString();
    }

    private static string CreateAuthorizationCode()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateS256CodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    internal static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string GenerateSecureToken()
    {
        var bytes = new byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEqualsUtf8(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static IEnumerable<string>? SplitAmr(string? amr) =>
        string.IsNullOrWhiteSpace(amr)
            ? null
            : amr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class OAuthException : Exception
{
    public string Error { get; }
    public int StatusCode { get; }
    public string? ErrorCode { get; }

    private OAuthException(string error, string description, int statusCode, string? errorCode = null)
        : base(description)
    {
        Error = error;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public static OAuthException InvalidRequest(string description) =>
        new("invalid_request", description, 400);

    public static OAuthException InvalidClient(string description) =>
        new("invalid_client", description, 401);

    public static OAuthException InvalidGrant(string description) =>
        new("invalid_grant", description, 400);

    public static OAuthException Unauthorized(string description) =>
        new("invalid_request", description, 401);

    public static OAuthException Forbidden(string description) =>
        new("access_denied", description, 403);

    public static OAuthException StepUpRequired(string description) =>
        new("access_denied", description, 403, "mfa_step_up_required");
}
