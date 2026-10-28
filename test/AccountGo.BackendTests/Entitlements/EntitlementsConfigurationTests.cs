using System;
using System.Collections.Generic;
using Api.Options;
using Core.Entitlements;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AccountGo.BackendTests.Entitlements
{
    public class EntitlementsConfigurationTests
    {
        private static EntitlementsOptions Read(Dictionary<string, string?> values) =>
            EntitlementsConfiguration.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        [Fact]
        public void NothingConfigured_IsDisabledAndEveryPlanIsSelfService()
        {
            var options = Read(new());

            Assert.False(options.Enabled);
            Assert.Empty(options.FreeLimits);
            Assert.True(options.IsSelfServicePlan("enterprise"));
            Assert.True(options.EngineShadow);
        }

        [Fact]
        public void EngineShadow_DefaultsTrue_UnlessExplicitlyFalse()
        {
            Assert.True(Read(new()).EngineShadow);
            Assert.True(Read(new() { ["ENTITLEMENTS_ENGINE_SHADOW"] = "true" }).EngineShadow);
            Assert.False(Read(new() { ["ENTITLEMENTS_ENGINE_SHADOW"] = "false" }).EngineShadow);
        }

        [Fact]
        public void LegacyModuleGate_DefaultsFalse_UnlessExplicitlyTrue()
        {
            Assert.False(Read(new()).LegacyModuleGate);
            Assert.False(Read(new() { ["ENTITLEMENTS_LEGACY_MODULE_GATE"] = "false" }).LegacyModuleGate);
            Assert.True(Read(new() { ["ENTITLEMENTS_LEGACY_MODULE_GATE"] = "true" }).LegacyModuleGate);
        }

        [Fact]
        public void FeaturesEnabled_DefaultsTrue_UnlessExplicitlyFalse()
        {
            Assert.True(Read(new()).FeaturesEnabled);
            Assert.True(Read(new() { ["ENTITLEMENT_FEATURES_ENABLED"] = "true" }).FeaturesEnabled);
            Assert.False(Read(new() { ["ENTITLEMENT_FEATURES_ENABLED"] = "false" }).FeaturesEnabled);
        }

        [Fact]
        public void HostedValues_AreReadFromFlatKeys()
        {
            var options = Read(new()
            {
                ["ENTITLEMENTS_ENABLED"] = "true",
                ["ENTITLEMENTS_SELF_SERVICE_PLANS"] = " Invoicing, accounting ,",
                ["ENTITLEMENTS_FREE_SALES_DOCUMENTS_PER_MONTH"] = "5",
                ["ENTITLEMENTS_FREE_BILLS_PER_MONTH"] = "5",
                ["ENTITLEMENTS_FREE_SEATS"] = "1",
                ["ENTITLEMENTS_FREE_BANK_ACCOUNTS"] = "2",
                ["ENTITLEMENTS_FREE_OWNED_WORKSPACES"] = "1",
                ["SUPPORT_EMAIL"] = "support@example.com",
            });

            Assert.True(options.Enabled);
            Assert.Equal(new[] { "invoicing", "accounting" }, options.SelfServicePlans);
            Assert.True(options.IsSelfServicePlan("ACCOUNTING"));
            Assert.False(options.IsSelfServicePlan("enterprise"));
            Assert.Equal(5, options.FreeLimits[MeteredResource.SalesDocuments]);
            Assert.Equal(5, options.FreeLimits[MeteredResource.Bills]);
            Assert.Equal(1, options.FreeLimits[MeteredResource.Seats]);
            Assert.Equal(2, options.FreeLimits[MeteredResource.BankAccounts]);
            Assert.Equal(1, options.FreeLimits[MeteredResource.OwnedWorkspaces]);
            Assert.Equal("support@example.com", options.SupportEmail);
        }

        [Theory]
        [InlineData("five")]
        [InlineData("-1")]
        public void InvalidLimit_FailsAtStartup(string value)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Read(new() { ["ENTITLEMENTS_FREE_SEATS"] = value }));
            Assert.Contains("ENTITLEMENTS_FREE_SEATS", ex.Message);
        }

        [Fact]
        public void MissingLimit_MeansUnlimitedForThatResource()
        {
            var options = Read(new() { ["ENTITLEMENTS_ENABLED"] = "true", ["ENTITLEMENTS_FREE_SEATS"] = "1" });

            Assert.False(options.FreeLimits.ContainsKey(MeteredResource.SalesDocuments));
            Assert.Empty(options.SelfServicePlans);
            Assert.True(options.IsSelfServicePlan("enterprise"));
        }
    }
}
