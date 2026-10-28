// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Filters;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Filters;

public class EntitlementProblemFactoryTests
{
    [Theory]
    [InlineData("QUOTA_EXCEEDED")]
    [InlineData("MODULE_NOT_SUBSCRIBED")]
    [InlineData("FEATURE_NOT_ENTITLED")]
    public void NonInactiveCodes_RawJson_HasExactlyOneStatusKey_EqualTo403(string code)
    {
        var decision = code switch
        {
            "QUOTA_EXCEEDED" => new EntitlementDecision(
                false, "QUOTA_EXCEEDED", "quota",
                Resource: MeteredResource.SalesDocuments, Limit: 1, Used: 1,
                SubscriptionStatus: "active", EffectivePlan: "accounting_free"),
            "MODULE_NOT_SUBSCRIBED" => new EntitlementDecision(
                false, "MODULE_NOT_SUBSCRIBED", "module",
                RequiredModule: "inventory", SubscriptionStatus: "active",
                EffectivePlan: "accounting_free"),
            "FEATURE_NOT_ENTITLED" => new EntitlementDecision(
                false, "FEATURE_NOT_ENTITLED", "feature",
                RequiredFeature: "mfa", SubscriptionStatus: "active",
                EffectivePlan: "accounting_free"),
            _ => throw new InvalidOperationException(code)
        };

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.SetupGet(t => t.Permissions).Returns(new HashSet<string>());
        var problem = EntitlementProblemFactory.FromDecision(decision, tenant.Object);
        var json = SerializeLikeAspNetProblemDetails(problem);

        Assert.Equal(1, Regex.Matches(json, "\"status\"").Count);
        Assert.Contains("\"status\":403", Regex.Replace(json, @"\s+", ""));
        Assert.True(problem.Extensions.ContainsKey("subscriptionStatus"));
        Assert.False(problem.Extensions.ContainsKey("status"));
    }

    [Fact]
    public void SubscriptionInactive_WritesBothStatusExtension_AndSubscriptionStatus()
    {
        var decision = new EntitlementDecision(
            false, "SUBSCRIPTION_INACTIVE", "inactive",
            RequiredModule: "inventory", SubscriptionStatus: "canceled",
            EffectivePlan: "accounting_free", UpgradeAvailable: true);
        var problem = EntitlementProblemFactory.FromDecision(decision, tenant: null);
        Assert.Equal("canceled", problem.Extensions["status"]);
        Assert.Equal("canceled", problem.Extensions["subscriptionStatus"]);

        var json = SerializeLikeAspNetProblemDetails(problem);
        // Collision still exists for inactive (legacy "status" extension) — last write wins for parsers;
        // we keep pre-E2 behaviour for SUBSCRIPTION_INACTIVE only.
        Assert.True(Regex.Matches(json, "\"status\"").Count >= 1);
    }

    [Fact]
    public void FromException_Quota_DoesNotWriteStatusExtension()
    {
        var ex = new QuotaExceededException(MeteredResource.SalesDocuments, 1, 1, "detail")
        {
            SubscriptionStatus = "active",
            EffectivePlan = "accounting_free"
        };
        var problem = EntitlementProblemFactory.FromException(ex, tenant: null);
        var json = SerializeLikeAspNetProblemDetails(problem);
        Assert.Equal(1, Regex.Matches(json, "\"status\"").Count);
        Assert.False(problem.Extensions.ContainsKey("status"));
        Assert.Equal("active", problem.Extensions["subscriptionStatus"]);
    }

    /// <summary>Mirrors ASP.NET ProblemDetailsJsonConverter flattening (Status + Extensions as siblings).</summary>
    private static string SerializeLikeAspNetProblemDetails(ProblemDetails problem)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (problem.Type != null) writer.WriteString("type", problem.Type);
            if (problem.Title != null) writer.WriteString("title", problem.Title);
            if (problem.Status != null) writer.WriteNumber("status", problem.Status.Value);
            if (problem.Detail != null) writer.WriteString("detail", problem.Detail);
            foreach (var (key, value) in problem.Extensions)
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
