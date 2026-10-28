// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Filters;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Services.Entitlements;
using Xunit;

namespace AccountGo.BackendTests.Filters;

public class RequireFeatureAttributeTests
{
    [Fact]
    public async Task UnresolvedTenant_ReturnsSame400AsRequireModule()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(false);
        var services = new ServiceCollection()
            .AddSingleton(Mock.Of<IEntitlementService>())
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);

        await new RequireFeatureAttribute("mfa").OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("X-AccountGo-Org header required", problem.Detail);
    }

    [Fact]
    public async Task Deny_Returns403_FeatureNotEntitled()
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.SetupGet(e => e.Options).Returns(new EntitlementsOptions { SupportEmail = "s@x.test" });
        entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(
                false, "FEATURE_NOT_ENTITLED", "denied",
                RequiredFeature: "mfa", EffectivePlan: "accounting_free", UpgradeAvailable: false));

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        tenant.SetupGet(t => t.Permissions).Returns(new HashSet<string>());
        var services = new ServiceCollection()
            .AddSingleton(entitlements.Object)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);

        await new RequireFeatureAttribute("mfa").OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("FEATURE_NOT_ENTITLED", problem.Extensions["code"]);
        Assert.Equal("mfa", problem.Extensions["requiredFeature"]);
        Assert.Equal("https://accountgo.dev/errors/feature-not-entitled", problem.Type);
        Assert.Equal(false, problem.Extensions["canManageBilling"]);
        Assert.Equal("s@x.test", problem.Extensions["contact"]);
    }

    [Fact]
    public async Task FeatureGate_DoesNotQueryUsageMembersOrBanks()
    {
        var company = new Company
        {
            Id = 1,
            Name = "Co",
            Plan = "accounting_free",
            SubscriptionStatus = "active",
            DisabledModulesJson = "[]"
        };
        var usageHits = 0;
        var memberHits = 0;
        var bankHits = 0;
        var companies = Repo(new List<Company> { company });
        var usage = CountingRepo<EntitlementUsage>(() => usageHits++);
        var members = CountingRepo<CompanyMember>(() => memberHits++);
        var banks = CountingRepo<Bank>(() => bankHits++);
        var invites = Repo(new List<CompanyInvitation>());
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        tenant.SetupGet(t => t.Permissions).Returns(new HashSet<string>());
        var engine = new EntitlementService(
            new EntitlementsOptions { Enabled = true },
            companies.Object, members.Object, invites.Object,
            usage.Object, banks.Object, tenant.Object);

        var services = new ServiceCollection()
            .AddSingleton<IEntitlementService>(engine)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);

        await new RequireFeatureAttribute("mfa").OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        Assert.Equal(403, ((ObjectResult)context.Result!).StatusCode);
        Assert.Equal(0, usageHits);
        Assert.Equal(0, memberHits);
        Assert.Equal(0, bankHits);
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() => data.AsQueryable());
        return mock;
    }

    private static Mock<IRepository<T>> CountingRepo<T>(Action onTable) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(() =>
        {
            onTable();
            return new List<T>().AsQueryable();
        });
        return mock;
    }
}
