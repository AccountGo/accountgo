// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Security.Claims;
using Api.Controllers;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>Phase 3G: choosing the bank whose details print on invoices.</summary>
public class InvoicePaymentBankSettingsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();
    private readonly Company _company;

    public InvoicePaymentBankSettingsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, tenantContext: _tenant);
        _db.Database.EnsureCreated();
        _company = new Company { Name = "Bank Co", Slug = "bank-co" };
        _db.Companies.Add(_company);
        _db.SaveChanges();
        _tenant.SetTenant(_company.Id, _company.Slug, CompanyMemberRole.Owner, Array.Empty<string>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private OrganizationsController Controller()
    {
        var controller = new OrganizationsController(
            _tenant,
            _db,
            new WorkspaceService(_db, new CompanyProvisioningService(_db, _tenant)),
            new CompanyAccountingPolicyService(_db),
            new Services.Billing.CommunityPlanCatalog());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u") }, "test"))
            }
        };
        return controller;
    }

    private int _accountSequence;

    private Bank AddBank(string name, DateTime? archivedAt = null, Company? owner = null)
    {
        var workspace = owner ?? _company;
        _tenant.SetTenant(workspace.Id, workspace.Slug, CompanyMemberRole.Owner, Array.Empty<string>());

        var accountClass = _db.AccountClasses.FirstOrDefault() ?? _db.AccountClasses.Add(new AccountClass { Name = "Assets" }).Entity;
        var account = new Account
        {
            CompanyId = workspace.Id,
            AccountClass = accountClass,
            AccountCode = $"1{++_accountSequence:000}",
            AccountName = name,
            IsCash = true,
        };
        _db.Accounts.Add(account);
        _db.SaveChanges();

        var bank = new Bank
        {
            CompanyId = workspace.Id,
            Name = name,
            Number = "123456",
            AccountId = account.Id,
            IsActive = archivedAt == null,
            ArchivedAt = archivedAt,
        };
        _db.Banks.Add(bank);
        _db.SaveChanges();

        _tenant.SetTenant(_company.Id, _company.Slug, CompanyMemberRole.Owner, Array.Empty<string>());
        return bank;
    }

    private static OrganizationsController.SalesInvoiceSettingsModel Request(int? paymentBankId) => new()
    {
        SellerName = SalesInvoiceSettings.TradingName,
        PaymentBankId = paymentBankId,
    };

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? 200,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 200,
    };

    [Fact]
    public void Save_ActiveBank_PersistsAndIsReturned()
    {
        var bank = AddBank("Operating");

        var result = Controller().UpdateSalesInvoiceSettings(Request(bank.Id));

        var saved = Assert.IsType<OrganizationsController.SalesInvoiceSettingsModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(bank.Id, saved.PaymentBankId);

        var reread = Assert.IsType<OrganizationsController.SalesInvoiceSettingsModel>(
            Assert.IsType<OkObjectResult>(Controller().GetSalesInvoiceSettings()).Value);
        Assert.Equal(bank.Id, reread.PaymentBankId);
    }

    [Fact]
    public void Save_ArchivedBank_IsRefused()
    {
        var archived = AddBank("Old account", archivedAt: DateTime.UtcNow);

        var result = Controller().UpdateSalesInvoiceSettings(Request(archived.Id));

        Assert.Equal(400, StatusOf(result));
        var reread = Assert.IsType<OrganizationsController.SalesInvoiceSettingsModel>(
            Assert.IsType<OkObjectResult>(Controller().GetSalesInvoiceSettings()).Value);
        Assert.Null(reread.PaymentBankId);
    }

    [Fact]
    public void Save_UnknownBank_IsRefused()
    {
        Assert.Equal(400, StatusOf(Controller().UpdateSalesInvoiceSettings(Request(99999))));
    }

    [Fact]
    public void Save_AnotherWorkspacesBank_IsRefused()
    {
        var other = new Company { Name = "Other Co", Slug = "other-co" };
        _db.Companies.Add(other);
        _db.SaveChanges();
        var foreign = AddBank("Foreign", owner: other);

        Assert.Equal(400, StatusOf(Controller().UpdateSalesInvoiceSettings(Request(foreign.Id))));
    }

    [Fact]
    public void Save_NoBank_ClearsTheSelection()
    {
        var bank = AddBank("Operating");
        Controller().UpdateSalesInvoiceSettings(Request(bank.Id));

        var result = Controller().UpdateSalesInvoiceSettings(Request(null));

        var saved = Assert.IsType<OrganizationsController.SalesInvoiceSettingsModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(saved.PaymentBankId);
    }
}
