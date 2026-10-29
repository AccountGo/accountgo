using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Api.Service
{
    /// <summary>
    /// Time-limited, HMAC-signed links so a customer can open an invoice PDF without signing in.
    /// The PDF itself is never stored; the link only carries company, invoice, recipient email, and expiry.
    /// </summary>
    public static class InvoicePdfLinkSigner
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        public sealed record Claims(int CompanyId, int InvoiceId, string Email, DateTimeOffset ExpiresAt);

        public static string CreateUrl(
            string apiBaseUrl,
            int companyId,
            int invoiceId,
            string email,
            DateTimeOffset expiresAt,
            string secret)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                throw new ArgumentException("API base URL is required.", nameof(apiBaseUrl));
            if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
                throw new ArgumentException("Signing secret must be at least 32 bytes.", nameof(secret));

            var normalizedEmail = NormalizeEmail(email)
                ?? throw new ArgumentException("A valid recipient email is required.", nameof(email));
            var exp = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var sig = Sign(companyId, invoiceId, normalizedEmail, exp, secret);

            var root = apiBaseUrl.TrimEnd('/');
            return $"{root}/api/public/invoices/pdf"
                + $"?c={companyId}"
                + $"&i={invoiceId}"
                + $"&e={Uri.EscapeDataString(normalizedEmail)}"
                + $"&exp={exp}"
                + $"&sig={Uri.EscapeDataString(sig)}";
        }

        /// <summary>
        /// Validates query values. On failure <paramref name="error"/> is "expired" or "invalid"
        /// (same wording for missing/tampered/wrong — do not distinguish for callers that might echo it).
        /// </summary>
        public static bool TryValidate(
            string? companyIdRaw,
            string? invoiceIdRaw,
            string? emailRaw,
            string? expRaw,
            string? sigRaw,
            string secret,
            DateTimeOffset now,
            out Claims? claims,
            out string? error)
        {
            claims = null;
            error = "invalid";

            if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
                return false;
            if (!int.TryParse(companyIdRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var companyId) || companyId <= 0)
                return false;
            if (!int.TryParse(invoiceIdRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var invoiceId) || invoiceId <= 0)
                return false;
            var email = NormalizeEmail(emailRaw);
            if (email == null)
                return false;
            if (string.IsNullOrWhiteSpace(expRaw) || !long.TryParse(expRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var expUnix))
                return false;
            if (string.IsNullOrWhiteSpace(sigRaw))
                return false;

            var expected = Sign(companyId, invoiceId, email, expRaw, secret);
            if (!FixedTimeEquals(expected, sigRaw.Trim()))
                return false;

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
            if (expiresAt <= now)
            {
                error = "expired";
                return false;
            }

            claims = new Claims(companyId, invoiceId, email, expiresAt);
            error = null;
            return true;
        }

        public static string? NormalizeEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;
            var trimmed = email.Trim();
            if (trimmed.Length > 254 || trimmed.IndexOf('@') <= 0)
                return null;
            return trimmed.ToLowerInvariant();
        }

        private static string Sign(int companyId, int invoiceId, string email, string expUnix, string secret)
        {
            var payload = $"v1|{companyId}|{invoiceId}|{email}|{expUnix}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(actual);
            if (a.Length != b.Length)
                return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
