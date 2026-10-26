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
