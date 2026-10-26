using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Filters;
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
}
