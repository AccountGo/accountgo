#nullable disable
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Entitlements;
using Microsoft.Extensions.DependencyInjection;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>Community entitlements: unlimited quotas, community plan, no features.</summary>
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Entitlements_Community_NothingIsLimitedOrRecordedAsQuota()
        {
            for (var i = 0; i < 3; i++)
                Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice())).IsSuccessStatusCode);

            var state = await Json(await Send(HttpMethod.Get, "/api/entitlements"));
            Assert.False(state.GetProperty("enabled").GetBoolean());
            Assert.False(state.GetProperty("limited").GetBoolean());
            Assert.Equal(CommunityPlanCatalog.PlanKey, state.GetProperty("plan").GetString());
            Assert.Equal(CommunityPlanCatalog.PlanKey, state.GetProperty("effectivePlan").GetString());
            Assert.Equal(1, state.GetProperty("selfServicePlans").GetArrayLength());
            Assert.Equal(CommunityPlanCatalog.PlanKey, state.GetProperty("selfServicePlans")[0].GetString());
            Assert.Equal(JsonValueKind.Null, state.GetProperty("limits").GetProperty("salesDocuments").GetProperty("limit").ValueKind);
            Assert.Equal(JsonValueKind.Array, state.GetProperty("features").ValueKind);
            Assert.Equal(0, state.GetProperty("features").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, state.GetProperty("lifecycle").ValueKind);

            // Community always meters create events (usage rows are audit, not quotas).
            using var scope = _factory.Services.CreateScope();
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                Assert.Equal(3, scope.ServiceProvider.GetRequiredService<ApiDbContext>().EntitlementUsages.Count());
        }

        [Fact]
        public async Task CapacityHeader_FollowsTheWorkspaceTier_AndIsAbsentWithoutAWorkspace()
        {
            // The hosted Worker routes a workspace to the paid or free container pool by this header.
            var free = await Send(HttpMethod.Get, "/api/entitlements");
            Assert.True(free.IsSuccessStatusCode);
            Assert.Equal("free", Assert.Single(free.Headers.GetValues(Api.Middleware.EntitlementsVersionMiddleware.CapacityHeaderName)));

            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).Tier = CompanyTiers.Comp;
                db.SaveChanges();
            });
            var comp = await Send(HttpMethod.Get, "/api/entitlements");
            Assert.Equal("paid", Assert.Single(comp.Headers.GetValues(Api.Middleware.EntitlementsVersionMiddleware.CapacityHeaderName)));

            var noWorkspace = await Send(HttpMethod.Get, "/api/organizations", scoped: false);
            Assert.True(noWorkspace.IsSuccessStatusCode);
            Assert.False(noWorkspace.Headers.Contains(Api.Middleware.EntitlementsVersionMiddleware.CapacityHeaderName));
        }

        [Theory]
        [InlineData("pro", "paid")]
        [InlineData("comp", "paid")]
        [InlineData("free", "free")]
        [InlineData("", "free")]
        public void CapacityOf_MapsTierToPool(string tier, string pool) =>
            Assert.Equal(pool, Api.Middleware.EntitlementsVersionMiddleware.CapacityOf(tier));
    }
}
