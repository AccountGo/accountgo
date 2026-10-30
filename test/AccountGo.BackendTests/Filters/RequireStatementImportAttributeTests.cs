// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
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

public class RequireStatementImportAttributeTests
{
    [Fact]
    public async Task Deny_Returns403_WithUpgradeAvailable()
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.SetupGet(e => e.Options).Returns(new EntitlementsOptions { SupportEmail = "s@x.test" });
        entitlements.Setup(e => e.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) => action switch
            {
                EntitlementAction.Module => new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "no financials",
                    RequiredModule: "financials", EffectivePlan: "invoicing_free", UpgradeAvailable: true),
                EntitlementAction.Feature => new EntitlementDecision(
                    false, "FEATURE_NOT_ENTITLED", "Statement import is not on this plan.",
                    RequiredFeature: FeatureKeys.BankingStatementImport,
                    EffectivePlan: "invoicing_free",
                    UpgradeAvailable: true),
                _ => new EntitlementDecision(false, "DENIED", "unexpected")
            });
        entitlements.Setup(e => e.Get(1)).Returns(new EntitlementSnapshot(
            Enabled: true, Plan: "invoicing_free", Tier: "free", Limited: true,
            Modules: new[] { "sales", "purchases" }, SelfServicePlans: new[] { "invoicing_pro" },
            Usage: System.Array.Empty<ResourceUsage>(), SupportEmail: "s@x.test",
            Features: System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            EffectivePlan: "invoicing_free"));

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

        await new RequireStatementImportAttribute().OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("FEATURE_NOT_ENTITLED", problem.Extensions["code"]);
        Assert.Equal(FeatureKeys.BankingStatementImport, problem.Extensions["requiredFeature"]);
        Assert.Equal(true, problem.Extensions["upgradeAvailable"]);
    }

    [Fact]
    public async Task Allow_ContinuesPipeline()
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(true, null, null, RequiredModule: "financials", EffectivePlan: "community"));
        entitlements.Setup(e => e.Get(1)).Returns(new EntitlementSnapshot(
            Enabled: true, Plan: PlanKeys.Community, Tier: "free", Limited: false,
            Modules: new[] { "sales", "financials", "purchases" }, SelfServicePlans: new[] { PlanKeys.Community },
            Usage: System.Array.Empty<ResourceUsage>(), SupportEmail: null,
            EffectivePlan: PlanKeys.Community));
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.RequireCompanyId()).Returns(1);
        var services = new ServiceCollection()
            .AddSingleton(entitlements.Object)
            .AddSingleton(tenant.Object)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null!);
        var ran = false;

        await new RequireStatementImportAttribute().OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(ran);
        Assert.Null(context.Result);
    }
}
