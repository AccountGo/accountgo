// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Api.Service;
using Xunit;

namespace AccountGo.BackendTests.Auditing
{
    public sealed class SecurityAuditMetadataTests
    {
        private static readonly string[] ForbiddenKeys =
        {
            "password", "Password", "currentPassword", "newPassword",
            "token", "accessToken", "refresh_token", "approvalTokenHash",
            "secret", "clientSecret", "SECRET",
            "code", "Code", "otpCode", "verificationCode", "recoveryCodes",
            "otp", "Otp", "OTP", "submittedOtp"
        };

        [Theory]
        [InlineData("password")]
        [InlineData("PASSWORD")]
        [InlineData("userPassword")]
        [InlineData("accessToken")]
        [InlineData("tokenHash")]
        [InlineData("clientSecret")]
        [InlineData("otpCode")]
        [InlineData("code")]
        [InlineData("smsOtp")]
        public void IsForbidden_MatchesSecretLikeNames_CaseInsensitively(string key)
        {
            Assert.True(SecurityAuditMetadata.IsForbidden(key));
        }

        [Theory]
        [InlineData("method")]
        [InlineData("outcome")]
        [InlineData("reason")]
        [InlineData("operation")]
        [InlineData("targetUserId")]
        [InlineData("securityRoleIds")]
        public void IsForbidden_AllowsOrdinaryNames(string key)
        {
            Assert.False(SecurityAuditMetadata.IsForbidden(key));
        }

        [Fact]
        public void Sanitize_Null_ReturnsNull()
        {
            Assert.Null(SecurityAuditMetadata.Sanitize(null));
            Assert.Null(SecurityAuditMetadata.Serialize(null));
        }

        [Fact]
        public void Sanitize_DropsEveryForbiddenTopLevelKey_AndKeepsTheRest()
        {
            var metadata = ForbiddenKeys.ToDictionary(key => key, key => (object)("value-for-" + key));
            metadata["method"] = "otp";
            metadata["reason"] = "invalid_or_expired_code";

            var sanitized = Assert.IsAssignableFrom<JsonObject>(SecurityAuditMetadata.Sanitize(metadata));

            Assert.Equal(new[] { "method", "reason" }, sanitized.Select(p => p.Key).OrderBy(k => k));
            Assert.Equal("otp", sanitized["method"]!.GetValue<string>());
            Assert.Equal("invalid_or_expired_code", sanitized["reason"]!.GetValue<string>());
        }

        [Fact]
        public void Sanitize_DropsForbiddenKeysInNestedObjects()
        {
            var metadata = new
            {
                method = "otp",
                request = new
                {
                    email = "user@example.com",
                    Password = "hunter2",
                    inner = new { accessToken = "abc", note = "kept", deeper = new { otp = "123456", ok = true } }
                }
            };

            var sanitized = Assert.IsAssignableFrom<JsonObject>(SecurityAuditMetadata.Sanitize(metadata));
            var request = Assert.IsAssignableFrom<JsonObject>(sanitized["request"]);
            var inner = Assert.IsAssignableFrom<JsonObject>(request["inner"]);
            var deeper = Assert.IsAssignableFrom<JsonObject>(inner["deeper"]);

            Assert.False(request.ContainsKey("Password"));
            Assert.Equal("user@example.com", request["email"]!.GetValue<string>());
            Assert.False(inner.ContainsKey("accessToken"));
            Assert.Equal("kept", inner["note"]!.GetValue<string>());
            Assert.False(deeper.ContainsKey("otp"));
            Assert.True(deeper["ok"]!.GetValue<bool>());
        }

        [Fact]
        public void Sanitize_DropsForbiddenKeysInsideArraysOfObjects()
        {
            var metadata = new
            {
                items = new object[]
                {
                    new { id = 1, secret = "s1", label = "first" },
                    new { id = 2, verificationCode = "999999", label = "second" },
                    new { id = 3, nested = new[] { new { token = "t", keep = "yes" } } }
                }
            };

            var sanitized = Assert.IsAssignableFrom<JsonObject>(SecurityAuditMetadata.Sanitize(metadata));
            var items = Assert.IsAssignableFrom<JsonArray>(sanitized["items"]);

            Assert.Equal(3, items.Count);
            Assert.All(items.Select(i => Assert.IsAssignableFrom<JsonObject>(i)), item =>
            {
                Assert.False(item.ContainsKey("secret"));
                Assert.False(item.ContainsKey("verificationCode"));
            });
            Assert.Equal("first", items[0]!["label"]!.GetValue<string>());
            Assert.Equal("second", items[1]!["label"]!.GetValue<string>());
            var nested = Assert.IsAssignableFrom<JsonArray>(items[2]!["nested"]);
            var nestedItem = Assert.IsAssignableFrom<JsonObject>(nested[0]);
            Assert.False(nestedItem.ContainsKey("token"));
            Assert.Equal("yes", nestedItem["keep"]!.GetValue<string>());
        }

        [Fact]
        public void Serialize_ProducesJsonWithoutForbiddenKeysOrValues()
        {
            var json = SecurityAuditMetadata.Serialize(new
            {
                method = "otp",
                otpCode = "246810",
                nested = new { Token = "tok-123", password = "pw-456", fine = 1 }
            });

            Assert.NotNull(json);
            Assert.DoesNotContain("246810", json);
            Assert.DoesNotContain("tok-123", json);
            Assert.DoesNotContain("pw-456", json);
            Assert.Contains("\"method\":\"otp\"", json);
            Assert.Contains("\"fine\":1", json);
        }

        [Fact]
        public void Sanitize_PreservesNonSecretScalarTypes()
        {
            var sanitized = Assert.IsAssignableFrom<JsonObject>(SecurityAuditMetadata.Sanitize(new
            {
                count = 3,
                flag = true,
                roleIds = new List<int> { 1, 2 },
                missing = (string?)null
            }));

            Assert.Equal(3, sanitized["count"]!.GetValue<int>());
            Assert.True(sanitized["flag"]!.GetValue<bool>());
            Assert.Equal(2, Assert.IsAssignableFrom<JsonArray>(sanitized["roleIds"]).Count);
            Assert.True(sanitized.ContainsKey("missing"));
        }
    }
}
