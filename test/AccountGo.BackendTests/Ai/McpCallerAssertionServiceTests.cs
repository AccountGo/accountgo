#nullable enable
using System;
using System.Collections.Generic;
using Api.Service.Ai;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class McpCallerAssertionServiceTests
    {
        private const string TestSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static McpCallerAssertionService CreateService(Func<DateTimeOffset>? clock = null, string secret = TestSecret)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SECRET_KEY"] = secret,
                })
                .Build();

            return new McpCallerAssertionService(config, clock);
        }

        [Fact]
        public void MintAndValidate_RoundTripsCallerIdentityAndWorkspace()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            var service = CreateService(() => now);

            var token = service.MintAssertion("user-123", 42, "ph-financials", TimeSpan.FromMinutes(15));

            Assert.StartsWith("v1.", token);
            Assert.True(service.TryValidateAssertion(token, out var payload));
            Assert.NotNull(payload);
            Assert.Equal("user-123", payload!.UserId);
            Assert.Equal(42, payload.CompanyId);
            Assert.Equal("ph-financials", payload.CompanySlug);
        }

        [Fact]
        public void TryValidateAssertion_RejectsTamperedSignatureOrPayload()
        {
            var service = CreateService();
            var token = service.MintAssertion("user-123", 42, "ph-financials");

            var parts = token.Split('.');
            var tamperedSig = $"{parts[0]}.{parts[1]}.AAAA{parts[2][4..]}";
            Assert.False(service.TryValidateAssertion(tamperedSig, out _));

            // Signed with a different secret
            var otherService = CreateService(secret: "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210");
            Assert.False(otherService.TryValidateAssertion(token, out _));
        }

        [Fact]
        public void TryValidateAssertion_RejectsExpiredAssertion()
        {
            var issuedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            var issuer = CreateService(() => issuedAt);
            var token = issuer.MintAssertion("user-123", 42, "ph-financials", TimeSpan.FromMinutes(5));

            // Advance clock 10 minutes past issuance (beyond 5m TTL + 1m clock skew)
            var verifier = CreateService(() => issuedAt.AddMinutes(10));
            Assert.False(verifier.TryValidateAssertion(token, out _));
        }
    }
}
