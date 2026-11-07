using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Service.Google;

public class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    public GoogleIdTokenValidator(IConfiguration configuration, ILogger<GoogleIdTokenValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        var enabled = _configuration.GetValue<bool>("Google:Enabled");
        var clientId = _configuration["Google:ClientId"];

        if (!enabled || string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Google authentication is not configured or enabled on this server.");
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { clientId },
            IssuedAtClockTolerance = TimeSpan.FromSeconds(60)
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        if (payload == null)
        {
            throw new InvalidJwtException("Failed to validate Google token: payload is null.");
        }

        var tokenId = !string.IsNullOrWhiteSpace(payload.JwtId)
            ? payload.JwtId
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idToken))).ToLowerInvariant();

        var expiresAtUtc = payload.ExpirationTimeSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(payload.ExpirationTimeSeconds.Value).UtcDateTime
            : DateTime.UtcNow.AddMinutes(60);

        var nonce = payload.Nonce;

        return new GoogleIdentity(
            Subject: payload.Subject ?? string.Empty,
            Email: payload.Email ?? string.Empty,
            EmailVerified: payload.EmailVerified,
            HostedDomain: payload.HostedDomain,
            Nonce: nonce,
            TokenId: tokenId,
            ExpiresAtUtc: expiresAtUtc
        );
    }
}
