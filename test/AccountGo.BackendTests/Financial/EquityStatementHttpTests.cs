// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
using Xunit;

namespace AccountGo.BackendTests.Financial;

[Collection("WorkspaceSetupHttp")]
public sealed class EquityStatementHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _user = null!;
    private int _companyId;
    private const string Org = "equity-statement";
    private static readonly DateTime Start = new(2025, 1, 1);
    private static readonly DateTime End = new(2026, 12, 31, 23, 59, 59);
    private static string Iso(DateTime date) => date.ToString("O", CultureInfo.InvariantCulture);

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _user = await _host.CreateUser("equity@example.test");
        _companyId = await _host.CreateWorkspace(_user.Token, "Equity statement", Org);
        await _host.SetFunctionalRoles(_companyId, _user.Id, "Accountant");
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = EntityType.SoleProprietor;
            setting.BooksStartDate = Start;
            foreach (var year in new[] { 2025, 2026, 2027 })
            {
                if (db.FinancialYears.Any(y => y.StartDate.Year == year)) continue;
                db.FinancialYears.Add(new FinancialYear { CompanyId = _companyId,
                    FiscalYearCode = year.ToString(CultureInfo.InvariantCulture), FiscalYearName = year.ToString(CultureInfo.InvariantCulture),
                    StartDate = new DateTime(year, 1, 1), EndDate = new DateTime(year, 12, 31), IsActive = true });
            }
        });
    }
    public Task DisposeAsync() => _host.DisposeAsync();
    private void WithDb(Action<ApiDbContext> action)
    {
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, Org, CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        action(db);
        db.SaveChanges();
    }
    private Task<HttpResponseMessage> Send(string path, HttpMethod? method = null, object? body = null, string? token = null) =>
        _host.Send(method ?? HttpMethod.Get, "/api/Financials/" + path, body, token ?? _user.Token, Org);
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await AuditTestHost.Json(response);
    }
    private Task<HttpResponseMessage> Report(string suffix = "", DateTime? from = null, DateTime? to = null) =>
        Send($"EquityStatement?from={Iso(from ?? Start)}&to={Iso(to ?? End)}{suffix}");
    private static JsonElement Row(JsonElement statement, string key) =>
        statement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);
    private static decimal Total(JsonElement statement, string key) => Row(statement, key).GetProperty("total").GetDecimal();
    private void Post(string debitCode, string creditCode, decimal amount, DateTime? date = null)
    {
        WithDb(db => db.GeneralLedgerHeaders.Add(new GeneralLedgerHeader
        {
            CompanyId = _companyId, Date = date ?? Start.AddMonths(5), DocumentType = DocumentTypes.JournalEntry, Description = "Equity fixture",
            GeneralLedgerLines =
            {
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == debitCode).Id, DrCr = DrOrCrSide.Dr, Amount = amount },
                new GeneralLedgerLine { CompanyId = _companyId, AccountId = db.Accounts.Single(a => a.AccountCode == creditCode).Id, DrCr = DrOrCrSide.Cr, Amount = amount }
            }
        }));
    }
    private async Task Invoice()
    {
        var invoice = new Dto.Sales.SalesInvoice { InvoiceDate = Start.AddMonths(5), Status = "Open" };
        WithDb(db =>
        {
            var sales = db.Accounts.Single(a => a.AccountCode == "40100");
            var customer = new Customer { No = "EQUITY-CUSTOMER", Party = new Party { Name = "Report customer", PartyType = PartyTypes.Customer },
                PrimaryContact = new Contact { FirstName = "Report", LastName = "Contact", Party = new Party { Name = "Contact", PartyType = PartyTypes.Contact } },
                AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"), SalesAccount = sales,
                CustomerAdvancesAccount = db.Accounts.Single(a => a.AccountCode == "20120") };
            var category = new ItemCategory { Name = "Report services", ItemType = ItemTypes.Service, MeasurementId = db.Measurements.First().Id, SalesAccountId = sales.Id };
            var item = new Item { No = "EQUITY-SERVICE", Code = "EQUITY-SERVICE", Description = "Report service", Price = 1000,
                ItemCategory = category, SmallestMeasurementId = category.MeasurementId, SalesAccount = sales };
            db.Customers.Add(customer); db.Items.Add(item); db.SaveChanges();
            var setting = db.GeneralLedgerSettings.First();
            setting.ShippingChargeAccountId ??= sales.Id;
            invoice.CustomerId = customer.Id;
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine { ItemId = item.Id, Quantity = 1, Amount = 1000, MeasurementId = item.SmallestMeasurementId!.Value });
        });
        var response = await _host.Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice, _user.Token, Org);
        await Ok(response);
    }
    private async Task Seed(EntityType type)
    {
        WithDb(db =>
        {
            db.CompanySettings.Single().EntityType = type;
            db.GeneralLedgerSettings.Single().PermanentAccountId = db.Accounts.Single(a => a.AccountCode == EntityEquityMapping.GetProfile(type).CloseTargetAccountCode).Id;
        });
        Post("10111", "30100", 2000);
        await Invoice();
        var distribution = EntityEquityMapping.GetProfile(type).DistributionAccountCode;
        if (distribution != null) Post(distribution, "10111", 300);
    }
    private async Task Close()
    {
        var response = await Send("YearEnd/2025/Close", HttpMethod.Post, new { acknowledgeWarnings = true });
        await Ok(response);
    }
    private async Task AssertReconciles(JsonElement statement, string basis = "Accrual", DateTime? to = null)
    {
        var sheet = await Ok(await Send($"BalanceSheet?to={Iso(to ?? End)}&basis={basis}"));
        Assert.Equal(sheet.EnumerateArray().Where(r => r.GetProperty("accountClassId").GetInt32() == 3).Sum(r => r.GetProperty("amount").GetDecimal()), Total(statement, "closing"));
        Assert.Equal(0m, statement.GetProperty("differenceToBalanceSheet").GetDecimal());
        foreach (var column in statement.GetProperty("columns").EnumerateArray())
        {
            var key = column.GetProperty("key").GetString()!;
            var movements = statement.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("key").GetString() is not ("closing" or "restatedOpening"))
                .Sum(r => r.GetProperty("amounts").GetProperty(key).GetDecimal());
            Assert.Equal(Row(statement, "closing").GetProperty("amounts").GetProperty(key).GetDecimal(), movements);
        }
    }

    [Fact]
    public async Task SoleProprietor_InvoiceContributionsAndDrawings_BridgeToClosing()
    {
        await Seed(EntityType.SoleProprietor);
        var statement = await Ok(await Report());
        Assert.Equal(0m, Total(statement, "opening")); Assert.Equal(1000m, Total(statement, "profit"));
        Assert.Equal(2000m, Total(statement, "contributions")); Assert.Equal(-300m, Total(statement, "distributions"));
        Assert.Equal(2700m, Total(statement, "closing"));
        Assert.Equal("Owner's capital", statement.GetProperty("columns")[0].GetProperty("label").GetString());
        await AssertReconciles(statement);
    }

    [Theory]
    [InlineData(EntityType.SoleProprietor, "Accrual")]
    [InlineData(EntityType.Partnership, "Accrual")]
    [InlineData(EntityType.Corporation, "Accrual")]
    [InlineData(EntityType.Cooperative, "Accrual")]
    [InlineData(EntityType.NonProfit, "Accrual")]
    [InlineData(EntityType.SoleProprietor, "Cash")]
    [InlineData(EntityType.Partnership, "Cash")]
    [InlineData(EntityType.Corporation, "Cash")]
    [InlineData(EntityType.Cooperative, "Cash")]
    [InlineData(EntityType.NonProfit, "Cash")]
    public async Task EveryEntityAndBasis_ClosedAndUnclosedYears_ReconcileWithoutDoubleCounting(EntityType type, string basis)
    {
        await Seed(type);
        var before = await Ok(await Report("&basis=" + basis));
        await AssertReconciles(before, basis);
        await Close();
        var after = await Ok(await Report("&basis=" + basis));
        Assert.Equal(before.GetProperty("rows").GetRawText(), after.GetProperty("rows").GetRawText());
        await AssertReconciles(after, basis);
        var next = await Ok(await Report("&basis=" + basis, new DateTime(2027, 1, 1), new DateTime(2027, 12, 31)));
        Assert.Equal(Total(after, "closing"), Total(next, "opening"));
    }

    [Fact]
    public async Task Corporation_ClassifiesCapitalEarningsAndCustomEquity_AndOmitsUnusedOtherColumn()
    {
        await Seed(EntityType.Corporation);
        var report = await Ok(await Report());
        Assert.Equal(new[] { "contributed", "earnings" }, report.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
        Assert.Equal(2000m, Row(report, "closing").GetProperty("amounts").GetProperty("contributed").GetDecimal());
        Assert.Equal(700m, Row(report, "closing").GetProperty("amounts").GetProperty("earnings").GetDecimal());
        Post("10111", "30700", 50);
        Post("10111", "30600", 20);
        report = await Ok(await Report());
        Assert.Equal(3, report.GetProperty("columns").GetArrayLength());
        Assert.Equal(70m, Row(report, "other").GetProperty("amounts").GetProperty("other").GetDecimal());
        await AssertReconciles(report);
    }

    [Fact]
    public async Task PriorPeriodAdjustment_RestatesOpening_WithoutChangingProfit()
    {
        await Seed(EntityType.Corporation); await Close();
        Post("10120", "30820", 55, new DateTime(2026, 3, 1));
        var report = await Ok(await Report(from: new DateTime(2026, 1, 1)));
        Assert.Equal(2700m, Total(report, "opening")); Assert.Equal(55m, Total(report, "priorPeriodAdjustments"));
        Assert.Equal(2755m, Total(report, "restatedOpening")); Assert.Equal(0m, Total(report, "profit"));
        await AssertReconciles(report);
    }

    [Theory]
    [InlineData(EntityType.Partnership, "capital", "Partners' capital")]
    [InlineData(EntityType.Cooperative, "contributed", "Member capital")]
    [InlineData(EntityType.NonProfit, "netAssets", "Net assets")]
    public async Task EmptyStatement_UsesEntityColumnsAndRetainsOpeningProfitClosing(EntityType type, string key, string label)
    {
        WithDb(db =>
        {
            db.CompanySettings.Single().EntityType = type;
            db.GeneralLedgerSettings.Single().PermanentAccountId = db.Accounts.Single(a => a.AccountCode == EntityEquityMapping.GetProfile(type).CloseTargetAccountCode).Id;
        });
        var report = await Ok(await Report());
        Assert.Equal(key, report.GetProperty("columns")[0].GetProperty("key").GetString());
        Assert.Equal(label, report.GetProperty("columns")[0].GetProperty("label").GetString());
        Assert.Equal(new[] { "opening", "profit", "closing" }, report.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task Individual_ReturnsBadRequest()
    {
        WithDb(db => db.CompanySettings.Single().EntityType = EntityType.Individual);
        var response = await Report();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("isn't available for personal books", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("Report.pdf?statement=equity")).StatusCode);
    }

    [Fact]
    public async Task Comparative_ReturnsAnIndependentStatement_AndDefaultUsesFiscalYear()
    {
        await Seed(EntityType.SoleProprietor);
        var report = await Ok(await Report("&compareFrom=2024-01-01&compareTo=2024-12-31"));
        var prior = report.GetProperty("comparative");
        Assert.Equal(0m, Total(prior, "closing")); Assert.Equal(0m, prior.GetProperty("differenceToBalanceSheet").GetDecimal());
        var defaults = await Ok(await Send("EquityStatement?to=2026-06-30"));
        Assert.StartsWith("2026-01-01", defaults.GetProperty("from").GetString());
    }

    [Fact]
    public async Task PartnerMode_ShowsPartnerColumnsAndPartnerContributions()
    {
        var partnerId = 0;
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = EntityType.Partnership;
            setting.PartnerAccountsFrom = Start;
            var capital = new Account { CompanyId = _companyId, AccountClassId = 3, AccountCode = "30101", AccountName = "Capital – Avery", DrOrCrSide = DrOrCrSide.Cr };
            var drawings = new Account { CompanyId = _companyId, AccountClassId = 3, AccountCode = "30851", AccountName = "Drawings – Avery", DrOrCrSide = DrOrCrSide.Dr, IsContraAccount = true };
            db.Accounts.AddRange(capital, drawings);
            db.SaveChanges();
            var partner = new Partner { CompanyId = _companyId, Name = "Avery", CapitalAccountId = capital.Id, DrawingsAccountId = drawings.Id, CreatedAt = DateTime.UtcNow, CreatedBy = "test", RowVersion = Guid.NewGuid().ToByteArray() };
            db.Partners.Add(partner);
            db.SaveChanges();
            partnerId = partner.Id;
        });
        Post("10111", "30101", 125);
        var report = await Ok(await Report());
        var key = "partner:" + partnerId.ToString(CultureInfo.InvariantCulture);
        Assert.True(report.GetProperty("partnerMode").GetBoolean());
        Assert.Contains(report.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == key && c.GetProperty("label").GetString() == "Avery");
        Assert.Equal(125m, Row(report, "contributions").GetProperty("amounts").GetProperty(key).GetDecimal());
        Assert.Equal(125m, Row(report, "closing").GetProperty("amounts").GetProperty(key).GetDecimal());
        Assert.Equal(0m, report.GetProperty("differenceToBalanceSheet").GetDecimal());
        await AssertReconciles(report);
    }

    [Theory]
    [InlineData("from=invalid")]
    [InlineData("basis=invalid")]
    [InlineData("from=2026-01-02&to=2026-01-01")]
    [InlineData("compareFrom=2025-01-01")]
    [InlineData("compareTo=2025-12-31")]
    [InlineData("compareFrom=2026-01-01&compareTo=2025-01-01")]
    public async Task InvalidQueries_ReturnBadRequest(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("EquityStatement?" + query)).StatusCode);

    [Fact]
    public async Task ReportsViewPermission_IsRequired()
    {
        await _host.SetFunctionalRoles(_companyId, _user.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await Report()).StatusCode);
    }

    [Theory]
    [InlineData("equity", false)]
    [InlineData("equity", true)]
    [InlineData("changes-in-equity", true)]
    [InlineData("changesinequity", false)]
    public async Task Pdf_PrintsComponentColumnsReconciliationAndComparative(string alias, bool compare)
    {
        await Seed(EntityType.Corporation);
        var response = await Send($"Report.pdf?statement={alias}&from=2025-01-01&to=2026-12-31" +
            (compare ? "&compareFrom=2024-01-01&compareTo=2024-12-31" : ""));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Statement of Changes in Equity", text);
        Assert.Contains("Share capital", text); Assert.Contains("Retained earnings", text);
        Assert.Contains("Agrees with the balance sheet", text);
        Assert.Equal(compare, text.Contains("Previous period:", StringComparison.Ordinal));
    }
}
