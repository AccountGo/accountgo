#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Api.Service.Ai
{
    /// <summary>
    /// HMAC-SHA256 implementation of <see cref="IMcpCallerAssertionService"/>.
    /// Wire format: <c>v1.&lt;base64url(json)&gt;.&lt;base64url(hmac)&gt;</c>.
    /// </summary>
    public sealed class McpCallerAssertionService : IMcpCallerAssertionService
    {
        public const string HeaderName = "X-Mcp-Caller-Assertion";
        private const string VersionPrefix = "v1";
        private const string KeyContext = "AccountGo.McpCallerAssertion.v1";
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

        private readonly byte[] _signingKey;
        private readonly Func<DateTimeOffset> _clock;

        private sealed class WirePayload
        {
            [JsonPropertyName("uid")]
            public string? UserId { get; set; }

            [JsonPropertyName("cid")]
            public int CompanyId { get; set; }

            [JsonPropertyName("org")]
            public string? CompanySlug { get; set; }

            [JsonPropertyName("iat")]
            public long IssuedAt { get; set; }

            [JsonPropertyName("exp")]
            public long ExpiresAt { get; set; }
        }

        public McpCallerAssertionService(
            IConfiguration configuration,
            Func<DateTimeOffset>? clock = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var secretKey = configuration["SECRET_KEY"]
                ?? configuration["SECRET:key"]
                ?? configuration["SECRET__key"]
                ?? Environment.GetEnvironmentVariable("SECRET_KEY");

            if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
            {
                throw new InvalidOperationException(
                    "SECRET_KEY must be configured with at least 32 bytes to sign MCP caller assertions.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            _signingKey = hmac.ComputeHash(Encoding.UTF8.GetBytes(KeyContext));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public string MintAssertion(string userId, int companyId, string companySlug, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required.", nameof(userId));
            if (companyId <= 0)
                throw new ArgumentOutOfRangeException(nameof(companyId), "Company ID must be positive.");
            if (string.IsNullOrWhiteSpace(companySlug))
                throw new ArgumentException("Company slug is required.", nameof(companySlug));

            var now = _clock();
            var lifetime = ttl is { TotalSeconds: > 0 } ? ttl.Value : DefaultTtl;
            var wire = new WirePayload
            {
                UserId = userId.Trim(),
                CompanyId = companyId,
                CompanySlug = companySlug.Trim(),
                IssuedAt = now.ToUnixTimeSeconds(),
                ExpiresAt = now.Add(lifetime).ToUnixTimeSeconds(),
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(wire);
            var encodedPayload = Base64UrlEncode(jsonBytes);
            var signingInput = $"{VersionPrefix}.{encodedPayload}";
            var signatureBytes = ComputeMac(signingInput);
            var encodedSignature = Base64UrlEncode(signatureBytes);

            return $"{signingInput}.{encodedSignature}";
        }

        public bool TryValidateAssertion(string? assertion, out McpCallerAssertionPayload? payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(assertion))
            {
                return false;
            }

            var trimmed = assertion.Trim();
            var parts = trimmed.Split('.');
            if (parts.Length != 3 || !string.Equals(parts[0], VersionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            byte[] providedMac;
            byte[] payloadBytes;
            try
            {
                payloadBytes = Base64UrlDecode(parts[1]);
                providedMac = Base64UrlDecode(parts[2]);
            }
            catch
            {
                return false;
            }

            var signingInput = $"{parts[0]}.{parts[1]}";
            var expectedMac = ComputeMac(signingInput);
            if (!CryptographicOperations.FixedTimeEquals(providedMac, expectedMac))
            {
                return false;
            }

            WirePayload? wire;
            try
            {
                wire = JsonSerializer.Deserialize<WirePayload>(payloadBytes);
            }
            catch
            {
                return false;
            }

            if (wire == null
                || string.IsNullOrWhiteSpace(wire.UserId)
                || wire.CompanyId <= 0
                || string.IsNullOrWhiteSpace(wire.CompanySlug))
            {
                return false;
            }

            var now = _clock().ToUnixTimeSeconds();
            var skewSeconds = (long)ClockSkew.TotalSeconds;
            if (wire.ExpiresAt <= now - skewSeconds || wire.IssuedAt > now + skewSeconds)
            {
                return false;
            }

            payload = new McpCallerAssertionPayload(
                wire.UserId,
                wire.CompanyId,
                wire.CompanySlug,
                wire.IssuedAt,
                wire.ExpiresAt);
            return true;
        }

        private byte[] ComputeMac(string signingInput)
        {
            using var hmac = new HMACSHA256(_signingKey);
            return hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static byte[] Base64UrlDecode(string input)
        {
            var padded = input.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 0:
                    break;
                default:
                    throw new FormatException("Invalid base64url length.");
            }
            return Convert.FromBase64String(padded);
        }
    }
}
