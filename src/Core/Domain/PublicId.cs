// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Core.Domain
{
    /// <summary>
    /// Implemented by entities that carry a permanent prefixed public ID alongside their integer primary key.
    /// </summary>
    public interface IHasPublicId
    {
        string PublicId { get; set; }
    }

    /// <summary>
    /// Declares the permanent 2–5 lowercase letter prefix for an <see cref="IHasPublicId"/> entity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PublicIdPrefixAttribute : Attribute
    {
        public string Prefix { get; }

        public PublicIdPrefixAttribute(string prefix)
        {
            if (!PublicIdGenerator.IsValidPrefix(prefix))
                throw new ArgumentException($"Invalid public ID prefix '{prefix}'. Expected 2-5 lowercase ASCII letters.", nameof(prefix));

            Prefix = prefix;
        }
    }

    /// <summary>
    /// Generates and validates Stripe-style prefixed public IDs: <c>{prefix}_{26-char-body}</c>.
    /// New IDs use a 128-bit ULID (48-bit Unix milliseconds + 80 random bits) encoded in lowercase Crockford base32.
    /// Backfilled IDs use 26 lowercase hex characters (104 random bits), which is a subset of the validation alphabet.
    /// </summary>
    public static class PublicIdGenerator
    {
        private const string CrockfordAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        public const string PrefixPattern = @"^[a-z]{2,5}$";
        public const string FormatPattern = @"^[a-z]{2,5}_[0-9a-z]{26}$";
        public const int BodyLength = 26;
        public const int MaxLength = 32;

        private static readonly Regex PrefixRegex = new(PrefixPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FormatRegex = new(FormatPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidPrefix(string? prefix) =>
            !string.IsNullOrEmpty(prefix) && PrefixRegex.IsMatch(prefix);

        public static bool IsValidFormat(string? value) =>
            !string.IsNullOrEmpty(value) && FormatRegex.IsMatch(value);

        public static bool TryParse(string? value, out string prefix, out string body)
        {
            prefix = string.Empty;
            body = string.Empty;
            if (!IsValidFormat(value))
                return false;

            var underscore = value!.IndexOf('_');
            prefix = value[..underscore];
            body = value[(underscore + 1)..];
            return true;
        }

        public static string New(string prefix)
        {
            if (!IsValidPrefix(prefix))
                throw new ArgumentException($"Invalid public ID prefix '{prefix}'.", nameof(prefix));

            var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Span<byte> random = stackalloc byte[10];
            RandomNumberGenerator.Fill(random);

            return string.Create(prefix.Length + 1 + BodyLength, (prefix, timestampMs, r0: random[0], r1: random[1], r2: random[2], r3: random[3], r4: random[4], r5: random[5], r6: random[6], r7: random[7], r8: random[8], r9: random[9]), static (chars, state) =>
            {
                state.prefix.AsSpan().CopyTo(chars);
                var offset = state.prefix.Length;
                chars[offset++] = '_';

                // 48-bit timestamp -> 10 Crockford base32 characters (big-endian)
                var ts = (ulong)state.timestampMs;
                chars[offset + 0] = CrockfordAlphabet[(int)((ts >> 45) & 0x1F)];
                chars[offset + 1] = CrockfordAlphabet[(int)((ts >> 40) & 0x1F)];
                chars[offset + 2] = CrockfordAlphabet[(int)((ts >> 35) & 0x1F)];
                chars[offset + 3] = CrockfordAlphabet[(int)((ts >> 30) & 0x1F)];
                chars[offset + 4] = CrockfordAlphabet[(int)((ts >> 25) & 0x1F)];
                chars[offset + 5] = CrockfordAlphabet[(int)((ts >> 20) & 0x1F)];
                chars[offset + 6] = CrockfordAlphabet[(int)((ts >> 15) & 0x1F)];
                chars[offset + 7] = CrockfordAlphabet[(int)((ts >> 10) & 0x1F)];
                chars[offset + 8] = CrockfordAlphabet[(int)((ts >> 5) & 0x1F)];
                chars[offset + 9] = CrockfordAlphabet[(int)(ts & 0x1F)];

                // 80 random bits (10 bytes) -> 16 Crockford base32 characters
                chars[offset + 10] = CrockfordAlphabet[(state.r0 >> 3) & 0x1F];
                chars[offset + 11] = CrockfordAlphabet[((state.r0 << 2) | (state.r1 >> 6)) & 0x1F];
                chars[offset + 12] = CrockfordAlphabet[(state.r1 >> 1) & 0x1F];
                chars[offset + 13] = CrockfordAlphabet[((state.r1 << 4) | (state.r2 >> 4)) & 0x1F];
                chars[offset + 14] = CrockfordAlphabet[((state.r2 << 1) | (state.r3 >> 7)) & 0x1F];
                chars[offset + 15] = CrockfordAlphabet[(state.r3 >> 2) & 0x1F];
                chars[offset + 16] = CrockfordAlphabet[((state.r3 << 3) | (state.r4 >> 5)) & 0x1F];
                chars[offset + 17] = CrockfordAlphabet[state.r4 & 0x1F];
                chars[offset + 18] = CrockfordAlphabet[(state.r5 >> 3) & 0x1F];
                chars[offset + 19] = CrockfordAlphabet[((state.r5 << 2) | (state.r6 >> 6)) & 0x1F];
                chars[offset + 20] = CrockfordAlphabet[(state.r6 >> 1) & 0x1F];
                chars[offset + 21] = CrockfordAlphabet[((state.r6 << 4) | (state.r7 >> 4)) & 0x1F];
                chars[offset + 22] = CrockfordAlphabet[((state.r7 << 1) | (state.r8 >> 7)) & 0x1F];
                chars[offset + 23] = CrockfordAlphabet[(state.r8 >> 2) & 0x1F];
                chars[offset + 24] = CrockfordAlphabet[((state.r8 << 3) | (state.r9 >> 5)) & 0x1F];
                chars[offset + 25] = CrockfordAlphabet[state.r9 & 0x1F];
            });
        }
    }
}
