#nullable enable
using System.Collections.Generic;
using Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Filters;

public class ReconciledPeriodConfirmationFilterTests
{
    private static bool Run(string? queryValue)
    {
        var guard = new Mock<IReconciledPeriodGuard>();
        var services = new ServiceCollection().AddSingleton(guard.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (queryValue != null)
            http.Request.QueryString = new QueryString($"?confirmReconciledPeriod={queryValue}");
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        new ReconciledPeriodConfirmationFilter().OnActionExecuting(context);

        return guard.Invocations.Count > 0 && guard.Invocations[0].Method.Name == nameof(IReconciledPeriodGuard.Confirm);
    }

    [Fact]
    public void TrueQueryValue_ConfirmsTheGuard() => Assert.True(Run("true"));

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("")]
    public void OtherValues_DoNotConfirm(string? value) => Assert.False(Run(value));
}
