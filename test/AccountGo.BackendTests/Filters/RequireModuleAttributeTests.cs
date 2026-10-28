using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Filters;
using Core.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Services.Administration;
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
        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.IsModuleEnabled(It.IsAny<string>())).Returns<string>(key => System.Array.IndexOf(enabled, key) >= 0);
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        var services = new ServiceCollection().AddSingleton(admin.Object).AddSingleton(tenant.Object).BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null);
        var ran = false;

        await new RequireModuleAttribute("sales", "purchases").OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null);
        });

        Assert.Equal(passes, ran);
        if (!passes)
            Assert.Equal(403, ((ObjectResult)context.Result).StatusCode);
    }

    // Lapsed paid plan: SUBSCRIPTION_INACTIVE only when paying again would unlock the module.
    [Theory]
    [InlineData("enterprise", null, "inventory", "SUBSCRIPTION_INACTIVE")]
    [InlineData("accounting_pro", null, "inventory", "MODULE_NOT_SUBSCRIBED")]
    [InlineData("accounting_pro", "[\"sales\",\"financials\",\"purchases\"]", "reports", "MODULE_NOT_SUBSCRIBED")]
    public async Task LapsedPaidPlan_DenyCodeReflectsWhetherBillingWouldUnlock(
        string plan, string activeModulesJson, string module, string expectedCode)
    {
        var company = new Company { Id = 1, Plan = plan, ActiveModulesJson = activeModulesJson, SubscriptionStatus = "canceled" };
        var companies = new Mock<IRepository<Company>>();
        companies.Setup(c => c.GetById(It.IsAny<object>())).Returns(company);
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.SetupGet(t => t.CompanyId).Returns(1);
        var services = new ServiceCollection()
            .AddSingleton(companies.Object)
            .AddSingleton(tenant.Object)
            .AddSingleton(new Mock<IAdministrationService>().Object)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null);
        var ran = false;

        await new RequireModuleAttribute(module).OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null);
        });

        Assert.False(ran);
        var problem = Assert.IsType<ProblemDetails>(((ObjectResult)context.Result).Value);
        Assert.Equal(403, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
        Assert.Equal(module, problem.Extensions["requiredModule"]);
    }
}
