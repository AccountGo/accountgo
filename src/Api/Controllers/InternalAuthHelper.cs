using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api.Controllers
{
    public static class InternalAuthHelper
    {
        /// <summary>
        /// Validates the dedicated internal sync key (INTERNAL_SYNC_KEY).
        /// Falls back to SECRET_KEY only when INTERNAL_SYNC_KEY is unset, and logs a warning —
        /// JWT signing material must not be the long-term credential for DB restore.
        /// </summary>
        public static bool ValidateInternalKey(
            IConfiguration configuration,
            IHeaderDictionary headers,
            out int errorStatus,
            out string? errorMessage,
            ILogger? logger = null)
        {
            var expectedKey = ResolveInternalSyncKey(configuration, logger);

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                errorStatus = 503;
                errorMessage = "Internal sync is unavailable: INTERNAL_SYNC_KEY is not configured.";
                return false;
            }

            var providedKey = headers["x-internal-key"].ToString();
            if (string.IsNullOrEmpty(providedKey))
            {
                errorStatus = 401;
                errorMessage = "Invalid internal sync key";
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);

            if (expectedBytes.Length != providedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                errorStatus = 401;
                errorMessage = "Invalid internal sync key";
                return false;
            }

            errorStatus = 200;
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Restricts internal endpoints to loopback callers (Cloudflare container → 127.0.0.1 / ::1).
        /// Uses the connection remote address before trusting X-Forwarded-For for this check.
        /// </summary>
        public static bool IsLoopbackRequest(HttpContext httpContext)
        {
            var remote = httpContext.Connection.RemoteIpAddress;
            if (remote == null)
                return false;

            if (remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();

            return IPAddress.IsLoopback(remote);
        }

        private static string? ResolveInternalSyncKey(IConfiguration configuration, ILogger? logger)
        {
            var dedicated = configuration["INTERNAL_SYNC_KEY"]
                ?? Environment.GetEnvironmentVariable("INTERNAL_SYNC_KEY");

            if (!string.IsNullOrWhiteSpace(dedicated))
                return dedicated;

            var jwtSecret = configuration["SECRET_KEY"]
                ?? configuration["SECRET:key"]
                ?? configuration["SECRET__key"]
                ?? Environment.GetEnvironmentVariable("SECRET_KEY")
                ?? Environment.GetEnvironmentVariable("SECRET__key");

            if (!string.IsNullOrWhiteSpace(jwtSecret))
            {
                logger?.LogWarning(
                    "INTERNAL_SYNC_KEY is not set; falling back to SECRET_KEY for internal sync. " +
                    "Configure a dedicated INTERNAL_SYNC_KEY distinct from the JWT signing key.");
            }

            return jwtSecret;
        }
    }
}
