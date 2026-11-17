using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Api.Authorization;
using Api.Controllers;
using Api.Filters;
using Api.Tenancy;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Security;

public sealed class YearEndReadAuthorizationTests
{
    [Theory]
    [InlineData(nameof(FinancialsController.ListYearEndYears), SystemPermissions.FinancialsPeriodsClose, CompanyMemberRole.Member, true)]
    [InlineData(nameof(FinancialsController.PreviewYearEnd), SystemPermissions.FinancialsPeriodsClose, CompanyMemberRole.Member, true)]
    [InlineData(nameof(FinancialsController.ListYearEndYears), SystemPermissions.FinancialsAccountsView, CompanyMemberRole.Member, true)]
    [InlineData(nameof(FinancialsController.PreviewYearEnd), SystemPermissions.FinancialsAccountsView, CompanyMemberRole.Member, true)]
    [InlineData(nameof(FinancialsController.ListYearEndYears), SystemPermissions.ReportsView, CompanyMemberRole.Member, false)]
    [InlineData(nameof(FinancialsController.PreviewYearEnd), SystemPermissions.ReportsView, CompanyMemberRole.Member, false)]
    [InlineData(nameof(FinancialsController.ListYearEndYears), "", CompanyMemberRole.Owner, false)]
    [InlineData(nameof(FinancialsController.PreviewYearEnd), "", CompanyMemberRole.Admin, false)]
    public async Task ReadGuard_RequiresAccountViewOrClosePermission(
        string actionName, string permission, CompanyMemberRole role, bool allowed)
    {
        var method = typeof(FinancialsController).GetMethod(actionName)!;
        var guard = Assert.Single(method.GetCustomAttributes<RequireOperationAttribute>());
        var tenant = new TenantContext();
        using var actor = tenant.ImpersonateUser(1, "reviewer", "year-end", role,
            new HashSet<string>(string.IsNullOrEmpty(permission) ? [] : [permission]));
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(true, null, null));
        var authorization = new AccessAuthorizationService(tenant, entitlements.Object);
        using var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenant)
            .AddSingleton<IEntitlementService>(entitlements.Object)
            .AddSingleton<IAccessAuthorizationService>(authorization)
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "reviewer")], "test"))
        };
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(FinancialsController).GetTypeInfo(),
            MethodInfo = method
        };
        var action = new ActionContext(http, new RouteData(), descriptor);
        var filters = new List<IFilterMetadata>();
        var controller = new object();
        var executing = new ActionExecutingContext(action, filters, new Dictionary<string, object?>(), controller);
        var nextCalled = false;

        await guard.OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(action, filters, controller));
        });

        Assert.Equal(allowed, nextCalled);
        if (!allowed)
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(executing.Result).StatusCode);
        if (permission == SystemPermissions.FinancialsAccountsView)
            Assert.False(authorization.Authorize(AccessOperationCatalog.FinancialsPeriodsClose).Allowed);
    }
}
