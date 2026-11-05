// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Data;
using Api.Service;
using Core.Billing;
using Core.Domain;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Organizations;

/// <summary>Create with a fake billing provider stores pending on the free plan and returns checkoutUrl.</summary>
public sealed class WorkspaceCreateCheckoutTests
{
    [Fact]
    public async Task Create_PaidPlan_RequiresCheckout_ReturnsPendingAndCheckoutUrl()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        var tenant = new Mock<ITenantContext>();
        await using var db = new ApiDbContext(options, tenant.Object);
        await db.Database.EnsureCreatedAsync();

        db.SuppressTenantSecurityCheck = true;
        tenant.Setup(t => t.Impersonate(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CompanyMemberRole>()))
            .Returns(Mock.Of<System.IDisposable>());

        var plans = new FakePaidPlanCatalog();
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.EvaluateForUser(It.IsAny<string>(), It.IsAny<UserAction>()))
            .Returns(new EntitlementDecision(true, null, null, RequiresCheckout: true));

        var billing = new Mock<IBillingProvider>();
        billing.Setup(b => b.RequestPlanChangeAsync(It.IsAny<int>(), "accounting_pro", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanChangeResult(false, "https://billing.test/checkout", null));

        var workspaces = new WorkspaceService(db, new CompanyProvisioningService(db, tenant.Object), tenant.Object, entitlements.Object);
        var financial = new Mock<Services.Financial.IFinancialService>(MockBehavior.Loose);
        var controller = new OrganizationsController(
            tenant.Object, db, workspaces,
            new CompanyAccountingPolicyService(db),
            plans,
            financial.Object,
            entitlements: entitlements.Object,
            billing: billing.Object)
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
        controller.ControllerContext.HttpContext.Request.Headers.Referer = "https://app.test/create";

        var result = await controller.Create(new OrganizationsController.CreateOrganizationRequest
        {
            Name = "Paid Co",
            Slug = "paid-co",
            Plan = "accounting_pro",
            Currency = "USD",
            ReturnUrl = "https://app.test/org/paid-co"
        }, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var payload = created.Value!;
        var activation = payload.GetType().GetProperty("activationState")!.GetValue(payload)?.ToString();
        var plan = payload.GetType().GetProperty("plan")!.GetValue(payload)?.ToString();
        var checkoutUrl = payload.GetType().GetProperty("checkoutUrl")!.GetValue(payload)?.ToString();
        Assert.Equal(CompanyActivationStates.PendingCheckout, activation);
        Assert.Equal("accounting_free", plan);
        Assert.Equal("https://billing.test/checkout", checkoutUrl);

        var company = await db.Companies.IgnoreQueryFilters().SingleAsync(c => c.Slug == "paid-co");
        Assert.Equal(CompanyActivationStates.PendingCheckout, company.ActivationState);
        Assert.Equal("accounting_free", company.Plan);
    }

    private sealed class FakePaidPlanCatalog : IPlanCatalog
    {
        public string DefaultPlanKey => "accounting_free";

        public IReadOnlyList<PlanDefinitionDto> GetPlans() => new[]
        {
            new PlanDefinitionDto
            {
                Key = "accounting_free", Name = "Accounting Free", SelfService = true, MonthlyPrice = 0, Product = "accounting",
                IncludedModules = new List<string> { "sales", "financials" }, RequiredModules = new List<string> { "sales" }
            },
            new PlanDefinitionDto
            {
                Key = "accounting_pro", Name = "Accounting Pro", SelfService = true, MonthlyPrice = 99, Product = "accounting",
                IncludedModules = new List<string> { "sales", "financials" }, RequiredModules = new List<string> { "sales" }
            }
        };

        public bool TryResolvePlan(string? key, out PlanDefinitionDto plan)
        {
            plan = GetPlans().First(p => string.Equals(p.Key, key, System.StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public PlanDefinitionDto ResolveStoredPlan(string? storedPlan) =>
            TryResolvePlan(storedPlan, out var p) ? p : GetPlans()[0];
    }
}
