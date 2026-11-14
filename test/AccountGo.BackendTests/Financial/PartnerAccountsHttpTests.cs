// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Financial;

[Collection("WorkspaceSetupHttp")]
public sealed class PartnerAccountsHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _user = null!;
    private const string Org = "partner-accounts-test";
    private int _companyId;

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _user = await _host.CreateUser("partners@example.test");
        _companyId = await _host.CreateWorkspace(_user.Token, "Partner accounts", Org);
        await _host.SetFunctionalRoles(_companyId, _user.Id, "Accountant");
        WithDb(db =>
        {
            var settings = db.CompanySettings.Single();
            settings.EntityType = EntityType.Partnership;
            settings.BooksStartDate = new DateTime(2025, 1, 1);
            db.FinancialYears.Add(new FinancialYear { CompanyId = _companyId, FiscalYearCode = "FY2025", FiscalYearName = "FY2025", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
        });
    }

    public Task DisposeAsync() => _host.DisposeAsync();

    private void WithDb(Action<ApiDbContext> action)
    {
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, Org, CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); action(db); db.SaveChanges();
    }

    [Fact]
    public async Task SetupWithNoExistingBalancesCreatesPartnerAccountsAndSharesWithoutPostingJournal()
    {
        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, Org, CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(new[] { "30101", "30102" }, db.Partners.OrderBy(p => p.Id).Select(p => db.Accounts.Single(a => a.Id == p.CapitalAccountId).AccountCode).ToArray());
        Assert.Equal(new[] { "30851", "30852" }, db.Partners.OrderBy(p => p.Id).Select(p => db.Accounts.Single(a => a.Id == p.DrawingsAccountId).AccountCode).ToArray());
        Assert.Empty(db.JournalEntryHeaders);
        Assert.Equal(2, db.PartnerShares.Count());
        Assert.Equal(new DateTime(2026, 6, 30), db.CompanySettings.Single().PartnerAccountsFrom);
    }
}
