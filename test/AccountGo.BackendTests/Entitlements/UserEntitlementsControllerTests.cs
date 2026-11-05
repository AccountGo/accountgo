// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Reflection;
using System.Security.Claims;
using Api.Controllers;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

/// <summary>GET /api/entitlements/me lives on the tenant-free user controller.</summary>
public sealed class UserEntitlementsControllerTests
{
    [Fact]
    public void GetMe_ReturnsFreeWorkspaceUsedAndLimit()
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.EvaluateForUser("user-1", It.IsAny<UserAction>()))
            .Returns(new EntitlementDecision(
                true, null, null,
                Resource: MeteredResource.OwnedWorkspaces,
                Limit: 1,
                Used: 0));

        var plans = new Mock<IPlanCatalog>();
        plans.SetupGet(p => p.DefaultPlanKey).Returns("accounting_free");

        var controller = new UserEntitlementsController(entitlements.Object, plans.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "user-1")
                    }, "test"))
                }
            }
        };

        var result = Assert.IsType<OkObjectResult>(controller.GetMe());
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var free = doc.RootElement.GetProperty("freeWorkspace");
        Assert.Equal(0, free.GetProperty("used").GetInt32());
        Assert.Equal(1, free.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void GetMe_Unauthenticated_ReturnsUnauthorized()
    {
        var controller = new UserEntitlementsController(
            Mock.Of<IEntitlementService>(),
            Mock.Of<IPlanCatalog>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

        Assert.IsType<UnauthorizedResult>(controller.GetMe());
    }

    [Fact]
    public void GetMe_IsTenantFree_NotTenantScoped()
    {
        var type = typeof(UserEntitlementsController);
        Assert.NotNull(type.GetCustomAttribute<TenantFreeAttribute>());
        Assert.Null(type.GetCustomAttribute<TenantScopedAttribute>());
        var method = type.GetMethod(nameof(UserEntitlementsController.GetMe))!;
        Assert.Null(method.GetCustomAttribute<TenantFreeAttribute>());
        Assert.Null(method.GetCustomAttribute<TenantScopedAttribute>());
    }
}
