// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Api.Controllers;
using Core.Billing;
using Core.Domain;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Services.Administration;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Controllers;

public sealed class SubscriptionControllerPlanChangeTests
{
    [Fact]
    public async Task UpdateSubscription_ErrorReason_ReturnsBadRequestMessage()
    {
        var billing = new Mock<IBillingProvider>();
        billing.SetupGet(b => b.RequiresProviderCheckout).Returns(true);
        billing.Setup(b => b.RequestPlanChangeAsync(1, "plan_b", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanChangeResult(false, null, "cannot change"));

        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto { Plan = "plan_a", CompanyId = 1 });

        var controller = Create(admin.Object, billing.Object);
        var result = await controller.UpdateSubscription(
            new UpdateSubscriptionDto { Plan = "plan_b", ReturnUrl = "https://app.example/settings" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("cannot change", bad.Value!.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSubscription_RedirectUrl_ReturnsOkRedirectPayload()
    {
        var billing = new Mock<IBillingProvider>();
        billing.SetupGet(b => b.RequiresProviderCheckout).Returns(true);
        billing.Setup(b => b.RequestPlanChangeAsync(1, "plan_b", "https://app.example/settings", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanChangeResult(false, "https://billing.example/checkout", null));

        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto { Plan = "plan_a", CompanyId = 1 });

        var controller = Create(admin.Object, billing.Object);
        var result = await controller.UpdateSubscription(
            new UpdateSubscriptionDto { Plan = "plan_b", ReturnUrl = "https://app.example/settings" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("https://billing.example/checkout", ok.Value!.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSubscription_AppliedImmediately_RunsAdminUpdate()
    {
        var billing = new Mock<IBillingProvider>();
        billing.SetupGet(b => b.RequiresProviderCheckout).Returns(true);
        billing.Setup(b => b.RequestPlanChangeAsync(1, "plan_b", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanChangeResult(true, null, null));

        var updated = new TenantSubscriptionDto { Plan = "plan_b", CompanyId = 1, ActiveModules = new List<string> { "sales" } };
        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto { Plan = "plan_a", CompanyId = 1 });
        admin.Setup(a => a.UpdateSubscription(It.IsAny<UpdateSubscriptionDto>())).Returns(updated);

        var controller = Create(admin.Object, billing.Object);
        var result = await controller.UpdateSubscription(
            new UpdateSubscriptionDto { Plan = "plan_b", ReturnUrl = "https://app.example/settings" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        admin.Verify(a => a.UpdateSubscription(It.IsAny<UpdateSubscriptionDto>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSubscription_MissingAbsoluteReturnUrl_ReturnsBadRequest()
    {
        var billing = new Mock<IBillingProvider>();
        billing.SetupGet(b => b.RequiresProviderCheckout).Returns(true);

        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto { Plan = "plan_a", CompanyId = 1 });

        var controller = Create(admin.Object, billing.Object);
        controller.ControllerContext.HttpContext.Request.Headers.Referer = "";
        var result = await controller.UpdateSubscription(
            new UpdateSubscriptionDto { Plan = "plan_b" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("return URL", bad.Value!.ToString(), System.StringComparison.OrdinalIgnoreCase);
        billing.Verify(
            b => b.RequestPlanChangeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSubscription_PlanGainingFinancials_WithUnpostedInvoice_Returns409_AndNeverCallsBilling()
    {
        var billing = new Mock<IBillingProvider>();
        billing.SetupGet(b => b.RequiresProviderCheckout).Returns(true);
        var admin = new Mock<IAdministrationService>();
        admin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto
        {
            Plan = "plan_a", CompanyId = 1, ActiveModules = new List<string> { "sales", "purchases" },
        });
        var books = new Mock<Services.Financial.IBooksReadinessService>();
        books.Setup(b => b.Check()).Returns(new Services.Financial.BooksReadiness(
            new[] { new Services.Financial.UnpostedDocument("invoice", 7, "INV-0007", new System.DateTime(2026, 10, 1)) },
            System.Array.Empty<Services.Financial.UnpostedDocument>()));

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        var controller = new SubscriptionController(admin.Object, new CommunityPlanCatalog(), billing.Object, tenant.Object, books.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.UpdateSubscription(
            new UpdateSubscriptionDto { Plan = "community", ReturnUrl = "https://app.example/settings" }, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("BOOKS_NOT_READY", problem.Extensions["code"]);
        billing.Verify(
            b => b.RequestPlanChangeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        admin.Verify(a => a.UpdateSubscription(It.IsAny<UpdateSubscriptionDto>()), Times.Never);
    }

    private static Services.Financial.IBooksReadinessService ReadyBooks()
    {
        var books = new Mock<Services.Financial.IBooksReadinessService>();
        books.Setup(b => b.Check()).Returns(new Services.Financial.BooksReadiness(
            System.Array.Empty<Services.Financial.UnpostedDocument>(), System.Array.Empty<Services.Financial.UnpostedDocument>()));
        return books.Object;
    }

    private static SubscriptionController Create(IAdministrationService admin, IBillingProvider billing)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        var controller = new SubscriptionController(admin, new CommunityPlanCatalog(), billing, tenant.Object, ReadyBooks())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Referer = "https://app.example/";
        return controller;
    }
}
