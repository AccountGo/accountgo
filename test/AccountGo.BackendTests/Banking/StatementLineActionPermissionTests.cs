// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Filters;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>One denial path per statement expense action (#436 I2 review).</summary>
public class StatementLineActionPermissionTests
{
    [Theory]
    [InlineData(nameof(BankingController.RecordExpenseFromStatementLine), SystemPermissions.PurchasesBillsCreate)]
    [InlineData(nameof(BankingController.RecordExpenseFromStatementLine), SystemPermissions.PurchasesBillsPost)]
    [InlineData(nameof(BankingController.RecordExpenseFromStatementLine), SystemPermissions.PurchasesPaymentsCreate)]
    [InlineData(nameof(BankingController.RecordExpenseFromStatementLine), SystemPermissions.BankingAccountsManage)]
    [InlineData(nameof(BankingController.PayOpenBillFromStatementLine), SystemPermissions.PurchasesPaymentsCreate)]
    [InlineData(nameof(BankingController.PayOpenBillFromStatementLine), SystemPermissions.BankingAccountsManage)]
    [InlineData(nameof(BankingController.ApplyOpenInvoiceFromStatementLine), SystemPermissions.SalesReceiptsCreate)]
    [InlineData(nameof(BankingController.ApplyOpenInvoiceFromStatementLine), SystemPermissions.BankingAccountsManage)]
    [InlineData(nameof(BankingController.BulkRecordExpenseFromStatementLines), SystemPermissions.PurchasesBillsCreate)]
    [InlineData(nameof(BankingController.BulkRecordExpenseFromStatementLines), SystemPermissions.PurchasesBillsPost)]
    [InlineData(nameof(BankingController.BulkRecordExpenseFromStatementLines), SystemPermissions.PurchasesPaymentsCreate)]
    [InlineData(nameof(BankingController.BulkRecordExpenseFromStatementLines), SystemPermissions.BankingAccountsManage)]
    [InlineData(nameof(BankingController.RecordOtherIncomeFromStatementLine), SystemPermissions.SalesInvoicesCreate)]
    [InlineData(nameof(BankingController.RecordOtherIncomeFromStatementLine), SystemPermissions.SalesInvoicesPost)]
    [InlineData(nameof(BankingController.RecordOtherIncomeFromStatementLine), SystemPermissions.SalesReceiptsCreate)]
    [InlineData(nameof(BankingController.RecordOtherIncomeFromStatementLine), SystemPermissions.BankingAccountsManage)]
    public void Action_declares_required_permission(string methodName, string permission)
    {
        var method = typeof(BankingController).GetMethod(methodName);
        Assert.NotNull(method);
        var declared = method!.GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true)
            .Cast<RequirePermissionAttribute>()
            .Select(ReadPermission)
            .ToHashSet();
        Assert.Contains(permission, declared);
    }

    [Fact]
    public async Task RecordExpense_denied_without_bills_create()
    {
        var result = await RunPermissionFilter(
            SystemPermissions.PurchasesBillsCreate,
            permissions: new HashSet<string>
            {
                SystemPermissions.BankingAccountsManage,
                SystemPermissions.PurchasesBillsPost,
                SystemPermissions.PurchasesPaymentsCreate
            });
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(SystemPermissions.PurchasesBillsCreate, problem.Extensions["requiredPermission"]);
    }

    [Fact]
    public async Task PayOpenBill_denied_without_payments_create()
    {
        var result = await RunPermissionFilter(
            SystemPermissions.PurchasesPaymentsCreate,
            permissions: new HashSet<string> { SystemPermissions.BankingAccountsManage });
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(SystemPermissions.PurchasesPaymentsCreate, problem.Extensions["requiredPermission"]);
    }

    [Fact]
    public async Task ApplyOpenInvoice_denied_without_receipts_create()
    {
        var result = await RunPermissionFilter(
            SystemPermissions.SalesReceiptsCreate,
            permissions: new HashSet<string> { SystemPermissions.BankingAccountsManage });
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(SystemPermissions.SalesReceiptsCreate, problem.Extensions["requiredPermission"]);
    }

    [Fact]
    public async Task RecordOtherIncome_denied_without_invoices_create()
    {
        var result = await RunPermissionFilter(
            SystemPermissions.SalesInvoicesCreate,
            permissions: new HashSet<string>
            {
                SystemPermissions.BankingAccountsManage,
                SystemPermissions.SalesInvoicesPost,
                SystemPermissions.SalesReceiptsCreate
            });
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(SystemPermissions.SalesInvoicesCreate, problem.Extensions["requiredPermission"]);
    }

    [Fact]
    public async Task BulkRecordExpense_denied_without_bills_create()
    {
        var result = await RunPermissionFilter(
            SystemPermissions.PurchasesBillsCreate,
            permissions: new HashSet<string>
            {
                SystemPermissions.BankingAccountsManage,
                SystemPermissions.PurchasesBillsPost,
                SystemPermissions.PurchasesPaymentsCreate
            });
        Assert.Equal(403, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(SystemPermissions.PurchasesBillsCreate, problem.Extensions["requiredPermission"]);
    }

    private static async Task<ObjectResult> RunPermissionFilter(string required, IReadOnlySet<string> permissions)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.SetupGet(t => t.Permissions).Returns(permissions);
        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("sub", "1") }, "test"));
        var services = new ServiceCollection().AddSingleton(tenant.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services, User = user };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        await new RequirePermissionAttribute(required).OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(null!));

        return Assert.IsType<ObjectResult>(context.Result);
    }

    private static string ReadPermission(RequirePermissionAttribute attribute)
    {
        var field = typeof(RequirePermissionAttribute).GetField(
            "_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<string>(field!.GetValue(attribute));
    }
}
