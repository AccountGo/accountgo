// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AccountGo.BackendTests.Auditing;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Banking;

[Collection("WorkspaceSetupHttp")]
public sealed class StatementOwnerMovementHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _user = null!;
    private int _companyId;
    private int _bankId;
    private int _cashId;
    private static readonly DateTime Date = new(2026, 6, 15);
    private const string Org = "owner-movements";

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _user = await _host.CreateUser("owner-movements@example.test");
        _companyId = await _host.CreateWorkspace(_user.Token, "Owner movements", Org);
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = EntityType.SoleProprietor;
            setting.BooksStartDate = new DateTime(2026, 1, 1);
            if (!db.FinancialYears.Any(y => y.StartDate <= Date && y.EndDate >= Date))
                db.FinancialYears.Add(new FinancialYear { CompanyId = _companyId, FiscalYearCode = "2026", FiscalYearName = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
            db.SaveChanges();
            var cash = new Account { CompanyId = _companyId, AccountClassId = (int)AccountClasses.Assets, AccountCode = "BANK472", AccountName = "Checking", IsCash = true, DrOrCrSide = DrOrCrSide.Dr };
            db.Accounts.Add(cash);
            db.SaveChanges();
            _cashId = cash.Id;
            var bank = new Bank { CompanyId = _companyId, Name = "Checking", AccountId = cash.Id, IsActive = true, CurrencyCode = "USD" };
            db.Banks.Add(bank);
            db.SaveChanges();
            _bankId = bank.Id;
            foreach (var code in new[] { "30800", "30100" })
                if (!db.Accounts.Any(a => a.AccountCode == code))
                    db.Accounts.Add(new Account { CompanyId = _companyId, AccountCode = code, AccountName = code == "30800" ? "My drawings" : "My capital", AccountClassId = (int)AccountClasses.Equity, DrOrCrSide = DrOrCrSide.Cr });
            db.SaveChanges();
        });
        await _host.SetFunctionalRoles(_companyId, _user.Id, "Accountant");
    }

    public Task DisposeAsync() => _host.DisposeAsync();
    private void WithDb(Action<ApiDbContext> action)
    {
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, role: CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        action(db);
        db.SaveChanges();
    }
    private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null) => _host.Send(method, "/api/Banking/" + path, body, token ?? _user.Token, Org);
    private int Line(decimal amount, string payee = "Personal transfer", DateTime? date = null)
    {
        var id = 0;
        WithDb(db =>
        {
            var import = new BankStatementImport { CompanyId = _companyId, BankId = _bankId, Source = BankStatementSources.Csv, ImportedAt = DateTime.UtcNow };
            db.BankStatementImports.Add(import);
            db.SaveChanges();
            var line = new BankStatementLine { CompanyId = _companyId, BankId = _bankId, BankStatementImportId = import.Id, Amount = amount, PostedDate = date ?? Date, Payee = payee, Reference = "REF472" };
            db.BankStatementLines.Add(line);
            db.SaveChanges();
            id = line.Id;
        });
        return id;
    }
    private Task<HttpResponseMessage> Record(int id, bool rememberRule = false, string suffix = "") => Send(HttpMethod.Post, $"StatementLines/{id}/RecordOwnerMovement{suffix}", new { rememberRule });
    private void AssertNothingPosted(params int[] ids) => WithDb(db =>
    {
        Assert.Empty(db.BankTransactions);
        Assert.All(db.BankStatementLines.Where(l => ids.Contains(l.Id)), l => Assert.Null(l.MatchedGeneralLedgerLineId));
    });
    private static async Task AssertOk(HttpResponseMessage response) => Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MissingBody_UsesSingleDefaultsOrRefusesBulkWithoutPosting(bool bulk, bool jsonNull)
    {
        var id = Line(-25);
        var route = bulk ? "StatementLines/BulkRecordOwnerMovement" : $"StatementLines/{id}/RecordOwnerMovement";
        using var client = _host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Banking/" + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _user.Token);
        request.Headers.Add("X-AccountGo-Org", Org);
        if (jsonNull) request.Content = JsonContent.Create<object?>(null);
        using var response = await client.SendAsync(request);
        Assert.Equal(bulk ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        if (bulk)
        {
            Assert.Equal("Select at least one statement line.", (await AuditTestHost.Json(response)).GetProperty("message").GetString());
            AssertNothingPosted(id);
            WithDb(db => { Assert.Empty(db.GeneralLedgerHeaders); Assert.Empty(db.BankStatementDescriptionRules); });
        }
        else
        {
            Assert.True((await AuditTestHost.Json(response)).GetProperty("isMatched").GetBoolean());
            WithDb(db =>
            {
                var transaction = Assert.Single(db.BankTransactions);
                Assert.Equal(25m, transaction.Amount);
                Assert.Equal("Personal transfer", transaction.Memo);
                Assert.Empty(db.BankStatementDescriptionRules);
            });
        }
    }

    [Theory]
    [InlineData(-40, "30800", DrOrCrSide.Dr, DrOrCrSide.Cr)]
    [InlineData(100, "30100", DrOrCrSide.Cr, DrOrCrSide.Dr)]
    public async Task Record_PostsEquityAndBank_MatchesAndLeavesProfitUnchanged(decimal amount, string code, DrOrCrSide equitySide, DrOrCrSide bankSide)
    {
        var id = Line(amount);
        await AssertOk(await Record(id));
        WithDb(db =>
        {
            var transaction = Assert.Single(db.BankTransactions);
            Assert.Equal("REF472", transaction.ReferenceNo);
            var lines = db.GeneralLedgerLines.Where(l => l.GeneralLedgerHeaderId == transaction.GeneralLedgerHeaderId).ToList();
            Assert.Equal(2, lines.Count);
            var equity = db.Accounts.Single(a => a.AccountCode == code);
            Assert.Contains(lines, l => l.AccountId == equity.Id && l.DrCr == equitySide && l.Amount == Math.Abs(amount));
            var bankLine = Assert.Single(lines, l => l.AccountId == _cashId);
            Assert.Equal(bankSide, bankLine.DrCr);
            Assert.Equal(bankLine.Id, db.BankStatementLines.Single(l => l.Id == id).MatchedGeneralLedgerLineId);
            Assert.Equal(amount, lines.Where(l => l.AccountId == _cashId).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
            Assert.DoesNotContain(lines, l => db.Accounts.Single(a => a.Id == l.AccountId).AccountClassId is (int)AccountClasses.Revenue or (int)AccountClasses.Expense);
        });
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId);
        Assert.All(scope.ServiceProvider.GetRequiredService<IFinancialService>().IncomeStatement(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)), row => Assert.Equal(0, row.Amount));
    }

    [Fact]
    public async Task Partnership_UsesPartnerLabelsAndCurrentAccountName()
    {
        WithDb(db => { db.CompanySettings.Single().EntityType = EntityType.Partnership; db.Accounts.Single(a => a.AccountCode == "30800").AccountName = "Partner withdrawals"; });
        var response = await Send(HttpMethod.Get, "StatementOwnerActions");
        await AssertOk(response);
        var json = await AuditTestHost.Json(response);
        Assert.Equal("Partner drawing", json.GetProperty("drawing").GetProperty("label").GetString());
        Assert.Equal("Partner contribution", json.GetProperty("contribution").GetProperty("label").GetString());
        Assert.Equal("Partner withdrawals", json.GetProperty("drawing").GetProperty("accountName").GetString());
        await AssertOk(await Record(Line(-20)));
    }

    [Theory]
    [InlineData(EntityType.Corporation)]
    [InlineData(EntityType.Cooperative)]
    [InlineData(EntityType.NonProfit)]
    [InlineData(EntityType.Individual)]
    public async Task UnsupportedEntity_HidesActionsAndRefusesPosting(EntityType type)
    {
        WithDb(db => db.CompanySettings.Single().EntityType = type);
        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("drawing").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("contribution").ValueKind);
        var id = Line(-10);
        Assert.Equal(HttpStatusCode.BadRequest, (await Record(id)).StatusCode);
        AssertNothingPosted(id);
    }

    [Fact]
    public async Task FinancialsDisabled_StillRecords()
    {
        WithDb(db => db.Companies.Single(c => c.Id == _companyId).DisabledModulesJson = "[\"financials\"]");
        await AssertOk(await Record(Line(-10)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFunctionalPermission_RefusesWithoutPosting(bool manageOnly)
    {
        var id = Line(-10);
        var token = _user.Token;
        await _host.SetFunctionalRoles(_companyId, _user.Id);
        if (manageOnly)
        {
            var member = await _host.CreateUser("member@example.test");
            await _host.AddMember(_companyId, member.Id, CompanyMemberRole.Member);
            WithDb(db =>
            {
                var role = new SecurityRole { Name = "manage-only-472", DisplayName = "Manage only" };
                db.SecurityRoles.Add(role); db.SaveChanges();
                db.SecurityRolePermissions.Add(new SecurityRolePermission { SecurityRoleId = role.Id, SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == SystemPermissions.BankingAccountsManage).Id });
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = db.CompanyMembers.Single(m => m.UserId == member.Id).Id, SecurityRoleId = role.Id });
            });
            token = member.Token;
        }
        var response = await Send(HttpMethod.Post, $"StatementLines/{id}/RecordOwnerMovement", new { }, token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNothingPosted(id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bulk_RecordsAllOrNothing(bool mixed)
    {
        var ids = new[] { Line(-10), Line(-20), Line(mixed ? 30 : -30) };
        var response = await Send(HttpMethod.Post, "StatementLines/BulkRecordOwnerMovement", new { lineIds = ids });
        if (mixed) { Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); AssertNothingPosted(ids); }
        else
        {
            await AssertOk(response);
            Assert.Equal(3, (await AuditTestHost.Json(response)).GetProperty("recorded").GetInt32());
            WithDb(db => { Assert.Equal(3, db.BankTransactions.Count()); Assert.All(db.BankStatementLines, l => Assert.NotNull(l.MatchedGeneralLedgerLineId)); });
        }
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("before-start")]
    [InlineData("no-year")]
    public async Task PeriodGuard_RefusesWithoutPosting(string guard)
    {
        var id = Line(-10);
        WithDb(db =>
        {
            if (guard == "locked") db.CompanySettings.Single().BooksLockedThrough = Date;
            if (guard == "before-start") db.CompanySettings.Single().BooksStartDate = Date.AddDays(1);
            if (guard == "no-year")
                foreach (var year in db.FinancialYears.Where(y => y.StartDate <= Date && y.EndDate >= Date)) year.EndDate = Date.AddDays(-1);
        });
        var response = await Record(id);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await AuditTestHost.Json(response)).GetProperty("message").GetString()!;
        Assert.Contains(guard switch
        {
            "locked" => "Books are locked through",
            "before-start" => "before the books start date",
            _ => "No active financial year covers this period"
        }, message);
        AssertNothingPosted(id);
    }

    [Fact]
    public async Task ReconciledPeriod_RequiresConfirmationThenRecords()
    {
        var id = Line(-10);
        WithDb(db => db.BankReconciliations.Add(new BankReconciliation { CompanyId = _companyId, BankId = _bankId, StatementEndDate = Date, Status = BankReconciliationStatuses.Finished, CreatedAt = DateTime.UtcNow }));
        var response = await Record(id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RECONCILED_PERIOD_CONFIRM_REQUIRED", (await AuditTestHost.Json(response)).GetProperty("code").GetString());
        AssertNothingPosted(id);
        await AssertOk(await Record(id, suffix: "?confirmReconciledPeriod=true"));
    }

    [Fact]
    public async Task ExcludedAndMatchedLines_CannotRecordTwice()
    {
        var excluded = Line(-10);
        WithDb(db => db.BankStatementLines.Single(l => l.Id == excluded).ExcludedAt = DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.BadRequest, (await Record(excluded)).StatusCode);
        AssertNothingPosted(excluded);
        var id = Line(-20);
        await AssertOk(await Record(id));
        Assert.Equal(HttpStatusCode.BadRequest, (await Record(id)).StatusCode);
        WithDb(db => Assert.Single(db.BankTransactions));
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("missing")]
    [InlineData("parent")]
    [InlineData("wrong-class")]
    public async Task InvalidEquityAccount_HidesActionAndRefuses(string reason)
    {
        WithDb(db =>
        {
            var account = db.Accounts.Single(a => a.AccountCode == "30800");
            if (reason == "archived") account.ArchivedAt = DateTime.UtcNow;
            if (reason == "missing") account.AccountCode = "OLD30800";
            if (reason == "wrong-class") account.AccountClassId = (int)AccountClasses.Expense;
            if (reason == "parent") db.Accounts.Add(new Account { CompanyId = _companyId, AccountCode = "30801", AccountName = "Child", ParentAccountId = account.Id, AccountClassId = (int)AccountClasses.Equity });
        });
        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("drawing").ValueKind);
        var id = Line(-10);
        Assert.Equal(HttpStatusCode.BadRequest, (await Record(id)).StatusCode);
        AssertNothingPosted(id);
    }

    [Fact]
    public async Task RememberRule_SuggestsOnlyMatchingDirectionAndHidesFromExpenses()
    {
        await AssertOk(await Record(Line(-10, "Personal transfer 123"), true));
        var next = Line(-15, "Personal transfer 456");
        var opposite = Line(20, "Personal transfer 789");
        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, $"Accounts/{_bankId}/StatementLines?status=unmatched"));
        Assert.Equal("drawing", json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == next).GetProperty("suggestedOwnerAction").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == opposite).GetProperty("suggestedOwnerAction").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Get, "StatementDescriptionRule?description=Personal%20transfer")).StatusCode);
        WithDb(db => { var rule = Assert.Single(db.BankStatementDescriptionRules); Assert.Equal(1, db.Entry(rule).Property<int>("Action").CurrentValue); Assert.Null(rule.VendorId); Assert.Null(rule.ExpenseAccountId); });
        var expenseAccountId = 0;
        WithDb(db => expenseAccountId = db.Accounts.First(a => a.AccountClassId == (int)AccountClasses.Expense
            && !db.Accounts.Any(child => child.ParentAccountId == a.Id)).Id);
        await AssertOk(await Send(HttpMethod.Post, $"StatementLines/{next}/RecordExpense", new
        {
            expenseAccountId, newVendor = new { name = "Personal transfer vendor" }, rememberRule = true
        }));
        WithDb(db => Assert.Equal(0, db.Entry(Assert.Single(db.BankStatementDescriptionRules)).Property<int>("Action").CurrentValue));
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "StatementDescriptionRule?description=Personal%20transfer")).StatusCode);

    }

    [Fact]
    public async Task Void_UnmatchesAndCreatesReversal()
    {
        var id = Line(-10);
        await AssertOk(await Record(id));
        var transactionId = 0;
        WithDb(db => transactionId = db.BankTransactions.Single().Id);
        await AssertOk(await Send(HttpMethod.Post, $"Transactions/{transactionId}/Void", new { date = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reason = "Correction" }));
        WithDb(db =>
        {
            Assert.Null(db.BankStatementLines.Single(l => l.Id == id).MatchedGeneralLedgerLineId);
            var transaction = db.BankTransactions.Single();
            Assert.NotNull(transaction.VoidGeneralLedgerHeaderId);
            Assert.Equal(transaction.GeneralLedgerHeaderId, db.GeneralLedgerHeaders.Single(h => h.Id == transaction.VoidGeneralLedgerHeaderId).ReversedGeneralLedgerHeaderId);
        });
    }

    [Fact]
    public async Task YearEndClose_ClosesDrawingsIntoCapital()
    {
        var postingDate = new DateTime(2025, 6, 15);
        WithDb(db =>
        {
            db.CompanySettings.Single().BooksStartDate = new DateTime(2025, 1, 1);
            if (!db.FinancialYears.Any(y => y.FiscalYearCode == "2025"))
                db.FinancialYears.Add(new FinancialYear { CompanyId = _companyId, FiscalYearCode = "2025", FiscalYearName = "2025", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 12, 31), IsActive = true });
        });
        await AssertOk(await Record(Line(100, date: postingDate)));
        await AssertOk(await Record(Line(-40, date: postingDate)));
        await AssertOk(await _host.Send(HttpMethod.Post, "/api/Financials/YearEnd/2025/Close",
            new { acknowledgeWarnings = true }, _user.Token, Org));
        using var scope = _host.Factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, role: CompanyMemberRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        decimal Balance(string code)
        {
            var accountId = db.Accounts.Single(a => a.AccountCode == code).Id;
            return db.GeneralLedgerLines.Where(l => l.AccountId == accountId).ToList().Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);
        }
        Assert.Equal(0, Balance("30800"));
        Assert.Equal(-60, Balance("30100"));
    }

    [Fact]
    public async Task ChangingEntity_HidesActionsWithoutChangingExistingEntries()
    {
        var id = Line(-10);
        await AssertOk(await Record(id));
        WithDb(db => db.CompanySettings.Single().EntityType = EntityType.Corporation);
        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("drawing").ValueKind);
        WithDb(db => { Assert.Single(db.BankTransactions); Assert.NotNull(db.BankStatementLines.Single(l => l.Id == id).MatchedGeneralLedgerLineId); Assert.Equal(2, db.GeneralLedgerLines.Count()); });
    }
}
