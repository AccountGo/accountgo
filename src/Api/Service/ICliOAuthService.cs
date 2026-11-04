// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;

namespace Api.Service;

public sealed record CliOAuthPendingRequestDto(
    string RequestId,
    string ClientId,
    string ClientDisplayName,
    string RedirectUri,
    int LoopbackPort,
    string Scope,
    string SignedInEmail,
    bool RequiresStepUp);

public sealed record CliOAuthCodeResult(string RedirectUrl);

public sealed record CliOAuthTokenResponse(
    string access_token,
    string token_type,
    int expires_in,
    string refresh_token,
    string scope);

public interface ICliOAuthService
{
    string CreatePendingAuthorization(
        string responseType,
        string clientId,
        string redirectUri,
        string scope,
        string state,
        string codeChallenge,
        string codeChallengeMethod);

    Task<CliOAuthPendingRequestDto> GetPendingRequestAsync(
        string requestId,
        ClaimsPrincipal principal,
        CancellationToken ct = default);

    Task<CliOAuthCodeResult> DecideAsync(
        string requestId,
        string decision,
        string? deviceLabel,
        ClaimsPrincipal principal,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task<CliOAuthTokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId,
        string code,
        string redirectUri,
        string codeVerifier,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task<CliOAuthTokenResponse> RefreshAsync(
        string clientId,
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task RevokeAsync(string token, CancellationToken ct = default);

    Task CleanupExpiredAsync(CancellationToken ct = default);
}
