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
            db.FinancialYears.RemoveRange(db.FinancialYears.ToList());
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
    private void Post(string debitCode, string creditCode, decimal amount)
    {
        WithDb(db => db.GeneralLedgerHeaders.Add(new GeneralLedgerHeader
        {
            CompanyId = _companyId, Date = new DateTime(2026, 5, 1), DocumentType = DocumentTypes.JournalEntry, Description = "Partner setup fixture",
            GeneralLedgerLines =
            {
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == debitCode).Id, DrCr = DrOrCrSide.Dr, Amount = amount },
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == creditCode).Id, DrCr = DrOrCrSide.Cr, Amount = amount }
            }
        }));
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
        var capitalParent = db.Accounts.Single(a => a.AccountCode == "30100").ParentAccountId;
        var drawingsParent = db.Accounts.Single(a => a.AccountCode == "30800").ParentAccountId;
        Assert.All(db.Partners, partner =>
        {
            Assert.Equal(capitalParent, db.Accounts.Single(a => a.Id == partner.CapitalAccountId).ParentAccountId);
            Assert.Equal(drawingsParent, db.Accounts.Single(a => a.Id == partner.DrawingsAccountId).ParentAccountId);
        });
    }

    [Fact]
    public async Task SetupRejectsSharesWithMoreThanFourDecimalPlaces()
    {
        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60.00001m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 39.99999m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetupRejectsNegativeDrawingsAndAmountsBelowTheCent()
    {
        var negativeDrawings = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = -1m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 1m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.BadRequest, negativeDrawings.StatusCode);

        var overPrecise = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0.001m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = -0.001m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.BadRequest, overPrecise.StatusCode);
    }

    [Fact]
    public async Task ReplaceSharesRejectsPercentagesWithMoreThanFourDecimalPlaces()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var partners = await _host.Send(HttpMethod.Get, "/api/Financials/Partners", token: _user.Token, org: Org);
        var json = await AuditTestHost.Json(partners);
        var firstId = json.GetProperty("partners")[0].GetProperty("id").GetInt32();
        var secondId = json.GetProperty("partners")[1].GetProperty("id").GetInt32();

        var response = await _host.Send(HttpMethod.Put, "/api/Financials/Partners/Shares/FY2025", new[]
        {
            new { partnerId = firstId, percent = 60.00001m }, new { partnerId = secondId, percent = 39.99999m }
        }, _user.Token, Org);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetupRejectsWhenAnEarlierFiscalYearIsStillOpen()
    {
        WithDb(db => db.FinancialYears.Add(new FinancialYear
        {
            CompanyId = _companyId, FiscalYearCode = "FY2024", FiscalYearName = "FY2024",
            StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31), IsActive = true
        }));

        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Close earlier fiscal years", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetupReclassifiesCreditDrawingsBalancesWithPositiveSplitAmounts()
    {
        WithDb(db => db.GeneralLedgerHeaders.Add(new GeneralLedgerHeader
        {
            CompanyId = _companyId, Date = new DateTime(2026, 5, 1), DocumentType = DocumentTypes.JournalEntry, Description = "Opening balances",
            GeneralLedgerLines =
            {
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == "10111").Id, DrCr = DrOrCrSide.Dr, Amount = 100m },
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == "30100").Id, DrCr = DrOrCrSide.Cr, Amount = 100m },
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == "30800").Id, DrCr = DrOrCrSide.Cr, Amount = 100m },
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == "10111").Id, DrCr = DrOrCrSide.Dr, Amount = 100m }
            }
        }));
        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 100m, drawings = 60m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 40m }
            }
        }, _user.Token, Org);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        WithDb(db =>
        {
            var journal = Assert.Single(db.JournalEntryHeaders.Where(j => j.Memo == "Partner accounts set up"));
            var lines = db.JournalEntryLines.Where(line => line.JournalEntryHeaderId == journal.Id).ToList();
            Assert.Equal(DrOrCrSide.Dr, lines.Single(l => l.AccountId == db.Accounts.Single(a => a.AccountCode == "30800").Id).DrCr);
            Assert.Equal(100m, lines.Single(l => l.AccountId == db.Accounts.Single(a => a.AccountCode == "30800").Id).Amount);
            var drawings = db.Partners.OrderBy(p => p.Id).Select(p => db.Accounts.Single(a => a.Id == p.DrawingsAccountId).Id).ToArray();
            Assert.Equal(new[] { 60m, 40m }, drawings.Select(id => lines.Single(l => l.AccountId == id).Amount));
            Assert.All(drawings, id => Assert.Equal(DrOrCrSide.Cr, lines.Single(l => l.AccountId == id).DrCr));
        });
    }

    [Fact]
    public async Task EquityStatementStillReconcilesAfterPartnerSetup()
    {
        Post("10111", "30100", 100m);
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 60m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 40m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var response = await _host.Send(HttpMethod.Get, "/api/Financials/EquityStatement?from=2025-01-01&to=2026-12-31", token: _user.Token, org: Org);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, (await AuditTestHost.Json(response)).GetProperty("differenceToBalanceSheet").GetDecimal());
    }

    [Fact]
    public async Task AddWithoutNameReturnsBadRequestInsteadOfServerError()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners", new
        {
            name = (string?)null, fiscalYearCode = "FY2025", percent = 10m,
            shares = new[] { new { partnerId = 1, percent = 50m }, new { partnerId = 2, percent = 40m } }
        }, _user.Token, Org);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddPartnerRejectsAStaleShareVersion()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners", new
        {
            name = "Casey", fiscalYearCode = "FY2025", percent = 10m,
            shares = new[] { new { partnerId = 1, percent = 50m }, new { partnerId = 2, percent = 40m } }
        }, _user.Token, Org, headers: new System.Collections.Generic.Dictionary<string, string> { ["If-Match"] = "stale-version" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetupMismatchReportsBothRequiredBalances()
    {
        Post("10111", "30100", 100m);
        Post("30800", "10111", 20m);

        var response = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 90m, drawings = 5m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 5m }
            }
        }, _user.Token, Org);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("100.00", message);
        Assert.Contains("20.00", message);
    }

    [Fact]
    public async Task ShareValidationRejectsWrongTotalZeroShareSinglePartnerAndClosedYear()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await AuditTestHost.Json(await _host.Send(HttpMethod.Get, "/api/Financials/Partners", token: _user.Token, org: Org));
        var ids = body.GetProperty("partners").EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToArray();
        foreach (var shares in new[]
        {
            new[] { new { partnerId = ids[0], percent = 60m }, new { partnerId = ids[1], percent = 30m } },
            new[] { new { partnerId = ids[0], percent = 100m }, new { partnerId = ids[1], percent = 0m } },
            new[] { new { partnerId = ids[0], percent = 100m } }
        })
        {
            var invalid = await _host.Send(HttpMethod.Put, "/api/Financials/Partners/Shares/FY2025", shares, _user.Token, Org);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        WithDb(db => db.FinancialYears.Single(y => y.FiscalYearCode == "FY2025").ClosedAt = new DateTime(2026, 10, 1));
        var closed = await _host.Send(HttpMethod.Put, "/api/Financials/Partners/Shares/FY2025", new[]
        {
            new { partnerId = ids[0], percent = 60m }, new { partnerId = ids[1], percent = 40m }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
    }

    [Fact]
    public async Task ArchiveRequiresNoOpenYearShareAndSucceedsAfterPartnerIsRemovedFromTheYear()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 40m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m },
                new { name = "Casey", percent = 20m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var initial = await AuditTestHost.Json(await _host.Send(HttpMethod.Get, "/api/Financials/Partners", token: _user.Token, org: Org));
        var partners = initial.GetProperty("partners").EnumerateArray().ToArray();
        var first = partners[0].GetProperty("id").GetInt32(); var second = partners[1].GetProperty("id").GetInt32(); var third = partners[2].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Send(HttpMethod.Post, $"/api/Financials/Partners/{third}/Archive", token: _user.Token, org: Org)).StatusCode);

        var removeShare = await _host.Send(HttpMethod.Put, "/api/Financials/Partners/Shares/FY2025", new[]
        {
            new { partnerId = first, percent = 50m }, new { partnerId = second, percent = 50m }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, removeShare.StatusCode);
        WithDb(db =>
        {
            var partner = db.Partners.Single(p => p.Id == third);
            Assert.False(db.PartnerShares.Any(s => s.PartnerId == third));
            Assert.False(db.GeneralLedgerLines.Any(line => line.AccountId == partner.CapitalAccountId || line.AccountId == partner.DrawingsAccountId));
        });
        var archived = await _host.Send(HttpMethod.Post, $"/api/Financials/Partners/{third}/Archive", token: _user.Token, org: Org);
        Assert.True(archived.StatusCode == HttpStatusCode.OK, await archived.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PartnerListQueryCountDoesNotGrowPerPartner()
    {
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, _user.Token, Org);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        WithDb(db =>
        {
            var capitalParent = db.Accounts.Single(a => a.AccountCode == "30100").ParentAccountId;
            var drawingsParent = db.Accounts.Single(a => a.AccountCode == "30800").ParentAccountId;
            for (var i = 3; i <= 20; i++)
            {
                var capital = new Account { CompanyId = _companyId, AccountClassId = 3, ParentAccountId = capitalParent, AccountCode = "301" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), AccountName = "Capital " + i, DrOrCrSide = DrOrCrSide.Cr };
                var drawings = new Account { CompanyId = _companyId, AccountClassId = 3, ParentAccountId = drawingsParent, AccountCode = "308" + (50 + i).ToString("00", System.Globalization.CultureInfo.InvariantCulture), AccountName = "Drawings " + i, DrOrCrSide = DrOrCrSide.Dr, IsContraAccount = true };
                db.Accounts.AddRange(capital, drawings);
                db.SaveChanges();
                db.Partners.Add(new Partner { CompanyId = _companyId, Name = "Partner " + i, CapitalAccountId = capital.Id, DrawingsAccountId = drawings.Id, CreatedAt = new DateTime(2026, 1, 1), CreatedBy = "test", RowVersion = Guid.NewGuid().ToByteArray() });
            }
        });

        var response = await _host.Send(HttpMethod.Get, "/api/Financials/Partners", token: _user.Token, org: Org);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, (await AuditTestHost.Json(response)).GetProperty("partners").GetArrayLength());
        Assert.InRange(AuditTestHost.ServerTimingQueryCount(response), 1, 25);
    }

    [Fact]
    public async Task PartnerSetupIsUnavailableUntilTheFeatureFlagIsEnabled()
    {
        var host = new AuditTestHost(partnerSetupEnabled: false);
        await host.InitializeAsync();
        try
        {
            var user = await host.CreateUser("partners-disabled@example.test");
            var companyId = await host.CreateWorkspace(user.Token, "Partner setup disabled", "partner-setup-disabled");
            await host.SetFunctionalRoles(companyId, user.Id, "Accountant");
            using (var scope = host.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(companyId, "partner-setup-disabled", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanySettings.Single().EntityType = EntityType.Partnership;
                db.SaveChanges();
            }

            var response = await host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new { date = new DateTime(2026, 6, 30), partners = Array.Empty<object>() }, user.Token, "partner-setup-disabled");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task PartnerEndpointsRequireTheirFinancialAccountPermissions()
    {
        var viewer = await _host.CreateUser("partner-viewer@example.test");
        await _host.AddMember(_companyId, viewer.Id, CompanyMemberRole.Member, "Auditor / Viewer");

        var list = await _host.Send(HttpMethod.Get, "/api/Financials/Partners", token: viewer.Token, org: Org);
        var setup = await _host.Send(HttpMethod.Post, "/api/Financials/Partners/Setup", new
        {
            date = new DateTime(2026, 6, 30),
            partners = new[]
            {
                new { name = "Avery", percent = 60m, capital = 0m, drawings = 0m },
                new { name = "Blake", percent = 40m, capital = 0m, drawings = 0m }
            }
        }, viewer.Token, Org);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
    }
}
