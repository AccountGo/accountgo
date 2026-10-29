using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Filters;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Filters;

public class RequireModuleAttributeTests
{
    [Theory]
    [InlineData(new[] { "sales" }, true)]
    [InlineData(new[] { "purchases" }, true)]
    [InlineData(new[] { "financials" }, false)]
    [InlineData(new string[0], false)]
    public async Task SeveralModules_PassWhenAnyIsEnabled(string[] enabled, bool passes)
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.SetupGet(e => e.Options).Returns(new EntitlementsOptions());
        entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns<int, EntitlementAction>((_, action) =>
            {
                var module = Assert.IsType<EntitlementAction.Module>(action);
                var hit = System.Array.Find(module.AnyOf, k => System.Array.IndexOf(enabled, k) >= 0);
                return hit != null
                    ? new EntitlementDecision(true, null, null, RequiredModule: hit)
                    : new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "denied", RequiredModule: module.AnyOf[0]);
            });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        tenant.SetupGet(t => t.CompanyId).Returns(1);
        var services = new ServiceCollection()
            .AddSingleton(entitlements.Object)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = HttpMethods.Get;
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);
        var ran = false;

        await new RequireModuleAttribute("sales", "purchases").OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.Equal(passes, ran);
        if (!passes)
            Assert.Equal(403, ((ObjectResult)context.Result!).StatusCode);
    }

    [Theory]
    [InlineData("inventory", "SUBSCRIPTION_INACTIVE")]
    [InlineData("donations", "SUBSCRIPTION_INACTIVE")]
    public async Task EngineDeny_PropagatesCode(string module, string expectedCode)
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.SetupGet(e => e.Options).Returns(new EntitlementsOptions());
        entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(false, expectedCode, "denied", RequiredModule: module,
                SubscriptionStatus: "canceled"));

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        tenant.SetupGet(t => t.CompanyId).Returns(1);
        var services = new ServiceCollection()
            .AddSingleton(entitlements.Object)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = HttpMethods.Post;
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);

        await new RequireModuleAttribute(module).OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        var problem = Assert.IsType<ProblemDetails>(((ObjectResult)context.Result!).Value);
        Assert.Equal(403, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
        Assert.Equal(module, problem.Extensions["requiredModule"]);
    }

    [Fact]
    public async Task AllowWhenReadOnly_UsesReadAccess_OnPost()
    {
        ModuleAccess? seen = null;
        var entitlements = new Mock<IEntitlementService>();
        entitlements.SetupGet(e => e.Options).Returns(new EntitlementsOptions());
        entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns<int, EntitlementAction>((_, action) =>
            {
                var module = Assert.IsType<EntitlementAction.Module>(action);
                seen = module.Access;
                return new EntitlementDecision(true, null, null, RequiredModule: "inventory");
            });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        tenant.SetupGet(t => t.CompanyId).Returns(1);

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowWhenReadOnlyAttribute()),
            "fake-export");
        var services = new ServiceCollection()
            .AddSingleton(entitlements.Object)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = HttpMethods.Post;
        http.SetEndpoint(endpoint);
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);
        var ran = false;

        await new RequireModuleAttribute("inventory").OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(ran);
        Assert.Equal(ModuleAccess.Read, seen);
    }
}
