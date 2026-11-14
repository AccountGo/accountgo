// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
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
    public async Task PartnerMode_OffersAndRecordsOwnerMovementForTheChosenPartner()
    {
        var accountIds = new Dictionary<string, int>();
        var partnerId = 0;
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = EntityType.Partnership;
            setting.PartnerAccountsFrom = Date;
            var capital = new Account { CompanyId = _companyId, AccountClassId = 3, AccountCode = "30101", AccountName = "Capital – Avery", DrOrCrSide = DrOrCrSide.Cr };
            var drawings = new Account { CompanyId = _companyId, AccountClassId = 3, AccountCode = "30851", AccountName = "Drawings – Avery", DrOrCrSide = DrOrCrSide.Dr, IsContraAccount = true };
            db.Accounts.AddRange(capital, drawings);
            db.SaveChanges();
            accountIds["capital"] = capital.Id;
            accountIds["drawings"] = drawings.Id;
            var partner = new Partner { CompanyId = _companyId, Name = "Avery", CapitalAccountId = capital.Id, DrawingsAccountId = drawings.Id, CreatedAt = DateTime.UtcNow, CreatedBy = "test", RowVersion = Guid.NewGuid().ToByteArray() };
            db.Partners.Add(partner);
            db.SaveChanges();
            partnerId = partner.Id;
        });

        var actions = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.True(!actions.TryGetProperty("drawing", out var sharedDrawing) || sharedDrawing.ValueKind == JsonValueKind.Null);
        var offered = Assert.Single(actions.GetProperty("partners").EnumerateArray());
        Assert.Equal("Avery", offered.GetProperty("name").GetString());
        Assert.Equal("Partner drawing", offered.GetProperty("drawing").GetProperty("label").GetString());

        var id = Line(-42, "Avery transfer");
        var missingPartner = await Send(HttpMethod.Post, $"StatementLines/{id}/RecordOwnerMovement", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingPartner.StatusCode);
        Assert.Equal("Choose a partner.", (await AuditTestHost.Json(missingPartner)).GetProperty("message").GetString());
        var recorded = await Send(HttpMethod.Post, $"StatementLines/{id}/RecordOwnerMovement", new { partnerId, rememberRule = true });
        await AssertOk(recorded);
        var recordedJson = await AuditTestHost.Json(recorded);
        Assert.Equal(partnerId, recordedJson.GetProperty("matchedPartnerId").GetInt32());
        Assert.Equal("Avery", recordedJson.GetProperty("matchedPartnerName").GetString());
        WithDb(db =>
        {
            Assert.Equal(accountIds["drawings"], Assert.Single(db.BankTransactions).ContraAccountId);
            Assert.Equal(partnerId, Assert.Single(db.BankStatementDescriptionRules).PartnerId);
        });
        var suggestionLine = Line(-13, "Avery transfer");
        var statementPage = await AuditTestHost.Json(await Send(HttpMethod.Get, $"Accounts/{_bankId}/StatementLines?status=unmatched"));
        var suggestion = statementPage.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("id").GetInt32() == suggestionLine);
        Assert.Equal("drawing", suggestion.GetProperty("suggestedOwnerAction").GetString());
        Assert.Equal(partnerId, suggestion.GetProperty("suggestedPartnerId").GetInt32());
        Assert.Equal("Avery", suggestion.GetProperty("suggestedPartnerName").GetString());
        WithDb(db =>
        {
            db.Partners.Single(p => p.Id == partnerId).ArchivedAt = DateTime.UtcNow;
        });
        var undone = await Send(HttpMethod.Post, $"StatementLines/{id}/UndoOwnerMovement", new { });
        await AssertOk(undone);
        var undoJson = await AuditTestHost.Json(undone);
        Assert.Equal(partnerId, undoJson.GetProperty("matchedPartnerId").GetInt32());
        WithDb(db => Assert.NotNull(Assert.Single(db.BankTransactions).VoidGeneralLedgerHeaderId));

        var legacyLine = Line(-17, "Legacy shared drawing");
        var sharedDrawingAccountId = 0;
        WithDb(db => sharedDrawingAccountId = db.Accounts.Single(a => a.AccountCode == "30800").Id);
        await AssertOk(await Send(HttpMethod.Post, $"StatementLines/{legacyLine}/CreateTransaction", new { kind = 2, contraAccountId = sharedDrawingAccountId }));
        var legacyUndo = await Send(HttpMethod.Post, $"StatementLines/{legacyLine}/UndoOwnerMovement", new { });
        Assert.Equal(HttpStatusCode.BadRequest, legacyUndo.StatusCode);
        Assert.Equal("This line wasn't recorded as an owner drawing or contribution.", (await AuditTestHost.Json(legacyUndo)).GetProperty("message").GetString());
        WithDb(db => Assert.False(Assert.Single(db.BankTransactions.Where(tx => tx.ContraAccountId == sharedDrawingAccountId)).IsVoid));
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

    private Task<HttpResponseMessage> Undo(int id, string suffix = "") =>
        Send(HttpMethod.Post, $"StatementLines/{id}/UndoOwnerMovement{suffix}");

    private async Task<string?> MatchedAction(int id)
    {
        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, $"Accounts/{_bankId}/StatementLines?status=all"));
        return json.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == id)
            .GetProperty("matchedOwnerAction").GetString();
    }

    [Theory]
    [InlineData(-40, "drawing")]
    [InlineData(100, "contribution")]
    public async Task UndoOwnerMovement_WithoutFinancials_ReversesAndAllowsRecordingAgain(decimal amount, string action)
    {
        WithDb(db => db.Companies.Single(c => c.Id == _companyId).DisabledModulesJson = "[\"financials\"]");
        var id = Line(amount);
        await AssertOk(await Record(id));
        Assert.Equal(action, await MatchedAction(id));
        await AssertOk(await Undo(id));
        Assert.Null(await MatchedAction(id));
        WithDb(db =>
        {
            var transaction = Assert.Single(db.BankTransactions);
            Assert.True(transaction.IsVoid);
            var reversal = db.GeneralLedgerHeaders.Single(h => h.Id == transaction.VoidGeneralLedgerHeaderId);
            Assert.Equal(transaction.GeneralLedgerHeaderId, reversal.ReversedGeneralLedgerHeaderId);
            Assert.Null(db.BankStatementLines.Single(l => l.Id == id).MatchedGeneralLedgerLineId);
            Assert.Equal(0m, db.GeneralLedgerLines.Where(l => l.AccountId == _cashId).ToList()
                .Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
        });
        await AssertOk(await Record(id));
        WithDb(db => Assert.Single(db.BankTransactions.ToList(), t => !t.IsVoid));
    }

    [Theory]
    [InlineData("unmatched")]
    [InlineData("void")]
    [InlineData("non-owner")]
    [InlineData("unsupported-entity")]
    [InlineData("changed-account")]
    [InlineData("contra-side")]
    public async Task UndoOwnerMovement_RejectsNonqualifyingMatches(string kind)
    {
        var id = Line(-10);
        if (kind == "non-owner")
        {
            var contra = 0;
            WithDb(db => contra = db.Accounts.First(a => a.AccountClassId == (int)AccountClasses.Expense
                && !db.Accounts.Any(c => c.ParentAccountId == a.Id)).Id);
            await AssertOk(await Send(HttpMethod.Post, $"StatementLines/{id}/CreateTransaction", new { kind = (int)BankTransactionKinds.MoneyOut, contraAccountId = contra }));
        }
        else if (kind != "unmatched") await AssertOk(await Record(id));
        WithDb(db =>
        {
            if (kind == "void") db.BankTransactions.Single().VoidGeneralLedgerHeaderId = db.BankTransactions.Single().GeneralLedgerHeaderId;
            if (kind == "unsupported-entity") db.CompanySettings.Single().EntityType = EntityType.Corporation;
            if (kind == "changed-account") db.Accounts.Single(a => a.AccountCode == "30800").AccountCode = "OLD30800";
            if (kind == "contra-side") db.BankStatementLines.Single(l => l.Id == id).MatchedGeneralLedgerLineId =
                db.GeneralLedgerLines.Single(l => l.AccountId != _cashId).Id;
        });
        Assert.Null(await MatchedAction(id));
        var headers = 0;
        WithDb(db => headers = db.GeneralLedgerHeaders.Count());
        Assert.Equal(HttpStatusCode.BadRequest, (await Undo(id)).StatusCode);
        WithDb(db => Assert.Equal(headers, db.GeneralLedgerHeaders.Count()));
    }

    [Fact]
    public async Task UndoOwnerMovement_LockedOriginalDate_LeavesMatchAndEntryIntact()
    {
        var id = Line(-10);
        await AssertOk(await Record(id));
        WithDb(db => db.CompanySettings.Single().BooksLockedThrough = Date);
        Assert.Equal(HttpStatusCode.BadRequest, (await Undo(id)).StatusCode);
        WithDb(db => { Assert.False(db.BankTransactions.Single().IsVoid); Assert.Single(db.GeneralLedgerHeaders); Assert.NotNull(db.BankStatementLines.Single().MatchedGeneralLedgerLineId); });
    }

    [Fact]
    public async Task UndoOwnerMovement_ReconciledPeriod_RequiresConfirmation()
    {
        var id = Line(-10);
        await AssertOk(await Record(id));
        WithDb(db => db.BankReconciliations.Add(new BankReconciliation { CompanyId = _companyId, BankId = _bankId, StatementEndDate = DateTime.UtcNow.Date.AddDays(1), Status = BankReconciliationStatuses.Finished, CreatedAt = DateTime.UtcNow }));
        Assert.Equal(HttpStatusCode.Conflict, (await Undo(id)).StatusCode);
        WithDb(db => { Assert.False(db.BankTransactions.Single().IsVoid); Assert.Single(db.GeneralLedgerHeaders); });
        await AssertOk(await Undo(id, "?confirmReconciledPeriod=true"));
    }

    [Fact]
    public async Task UndoOwnerMovement_MissingLine_ReturnsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Undo(int.MaxValue)).StatusCode);
    }

    [Fact]
    public async Task UndoOwnerMovement_RequiresCreatePermission()
    {
        var id = Line(-10);
        await AssertOk(await Record(id));
        var member = await _host.CreateUser("undo-manage-only@example.test");
        await _host.AddMember(_companyId, member.Id, CompanyMemberRole.Member);
        WithDb(db =>
        {
            var role = new SecurityRole { Name = "undo-manage-only", DisplayName = "Manage only" };
            db.SecurityRoles.Add(role); db.SaveChanges();
            db.SecurityRolePermissions.Add(new SecurityRolePermission { SecurityRoleId = role.Id, SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == SystemPermissions.BankingAccountsManage).Id });
            db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = db.CompanyMembers.Single(m => m.UserId == member.Id).Id, SecurityRoleId = role.Id });
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"StatementLines/{id}/UndoOwnerMovement", null, member.Token)).StatusCode);
        WithDb(db => { Assert.False(db.BankTransactions.Single().IsVoid); Assert.NotNull(db.BankStatementLines.Single().MatchedGeneralLedgerLineId); });
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

    private (int PartnerId, int CapitalId, int DrawingsId) SetupPartner(
        string name = "Avery", string suffix = "1", DateTime? partnerArchivedAt = null, DateTime? drawingsArchivedAt = null)
    {
        var ids = (0, 0, 0);
        WithDb(db =>
        {
            var setting = db.CompanySettings.Single();
            setting.EntityType = EntityType.Partnership;
            setting.PartnerAccountsFrom ??= Date;
            var capital = new Account
            {
                CompanyId = _companyId, AccountClassId = 3, AccountCode = "3010" + suffix,
                AccountName = "Capital – " + name, DrOrCrSide = DrOrCrSide.Cr
            };
            var drawings = new Account
            {
                CompanyId = _companyId, AccountClassId = 3, AccountCode = "3085" + suffix,
                AccountName = "Drawings – " + name, DrOrCrSide = DrOrCrSide.Dr, IsContraAccount = true,
                ArchivedAt = drawingsArchivedAt
            };
            db.Accounts.AddRange(capital, drawings);
            db.SaveChanges();
            var partner = new Partner
            {
                CompanyId = _companyId, Name = name, CapitalAccountId = capital.Id, DrawingsAccountId = drawings.Id,
                ArchivedAt = partnerArchivedAt, CreatedAt = DateTime.UtcNow, CreatedBy = "test",
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            db.Partners.Add(partner);
            db.SaveChanges();
            ids = (partner.Id, capital.Id, drawings.Id);
        });
        return ids;
    }

    [Fact]
    public async Task PartnerMode_MatchedLineDirectionMustMatchAccountType()
    {
        var (_, _, drawingsId) = SetupPartner();
        var lineId = Line(30, "Repayment");
        await AssertOk(await Send(HttpMethod.Post, $"StatementLines/{lineId}/CreateTransaction",
            new { kind = 1, contraAccountId = drawingsId }));

        var page = await AuditTestHost.Json(await Send(HttpMethod.Get, $"Accounts/{_bankId}/StatementLines?status=matched"));
        var matched = page.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == lineId);
        Assert.True(!matched.TryGetProperty("matchedOwnerAction", out var action) || action.ValueKind == JsonValueKind.Null);
        Assert.True(!matched.TryGetProperty("matchedPartnerId", out var pid) || pid.ValueKind == JsonValueKind.Null);

        var undo = await Send(HttpMethod.Post, $"StatementLines/{lineId}/UndoOwnerMovement", new { });
        Assert.Equal(HttpStatusCode.BadRequest, undo.StatusCode);
        Assert.Equal("This line wasn't recorded as an owner drawing or contribution.",
            (await AuditTestHost.Json(undo)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task PartnerMode_BulkRecordOwnerMovement_PostsToChosenPartner()
    {
        var (partnerId, _, drawingsId) = SetupPartner();
        var line1 = Line(-20, "Avery 1");
        var line2 = Line(-35, "Avery 2");

        var response = await Send(HttpMethod.Post, "StatementLines/BulkRecordOwnerMovement",
            new { lineIds = new[] { line1, line2 }, partnerId });
        await AssertOk(response);

        WithDb(db =>
        {
            var txs = db.BankTransactions.ToList();
            Assert.Equal(2, txs.Count);
            Assert.All(txs, tx => Assert.Equal(drawingsId, tx.ContraAccountId));
        });
    }

    [Fact]
    public async Task PartnerMode_RecordOwnerMovement_ValidationErrors()
    {
        var line1 = Line(-20, "Sole prop check");
        var soleProp = await Send(HttpMethod.Post, $"StatementLines/{line1}/RecordOwnerMovement", new { partnerId = 1, rememberRule = true });
        Assert.Equal(HttpStatusCode.BadRequest, soleProp.StatusCode);
        Assert.Equal("Partners aren't set up for this workspace.", (await AuditTestHost.Json(soleProp)).GetProperty("message").GetString());
        AssertNothingPosted(line1);
        WithDb(db => Assert.Empty(db.BankStatementDescriptionRules));

        var (archivedPartnerId, _, _) = SetupPartner("Archived", "1", partnerArchivedAt: DateTime.UtcNow);
        var (archivedAccountPartnerId, _, _) = SetupPartner("NoDrawings", "2", drawingsArchivedAt: DateTime.UtcNow);

        var archivedRes = await Send(HttpMethod.Post, $"StatementLines/{line1}/RecordOwnerMovement", new { partnerId = archivedPartnerId, rememberRule = true });
        Assert.Equal(HttpStatusCode.BadRequest, archivedRes.StatusCode);
        Assert.Equal("Choose an active partner.", (await AuditTestHost.Json(archivedRes)).GetProperty("message").GetString());

        var unknownRes = await Send(HttpMethod.Post, $"StatementLines/{line1}/RecordOwnerMovement", new { partnerId = 999999, rememberRule = true });
        Assert.Equal(HttpStatusCode.BadRequest, unknownRes.StatusCode);
        Assert.Equal("Choose an active partner.", (await AuditTestHost.Json(unknownRes)).GetProperty("message").GetString());

        var archivedAcctRes = await Send(HttpMethod.Post, $"StatementLines/{line1}/RecordOwnerMovement", new { partnerId = archivedAccountPartnerId, rememberRule = true });
        Assert.Equal(HttpStatusCode.BadRequest, archivedAcctRes.StatusCode);
        Assert.Equal("Drawings – NoDrawings (30852) is missing, archived or can't be posted to.",
            (await AuditTestHost.Json(archivedAcctRes)).GetProperty("message").GetString());

        AssertNothingPosted(line1);
        WithDb(db => Assert.Empty(db.BankStatementDescriptionRules));
    }

    [Fact]
    public async Task PartnerMode_Rules_LegacyArchivedAndExpenseOverwrite()
    {
        var (activeId, _, _) = SetupPartner("Avery", "1");
        var (archivedId, _, _) = SetupPartner("Blake", "2", partnerArchivedAt: DateTime.UtcNow);

        WithDb(db =>
        {
            db.BankStatementDescriptionRules.AddRange(
                new BankStatementDescriptionRule
                {
                    CompanyId = _companyId, DescriptionKey = "legacy draw",
                    Action = (int)BankStatementRuleAction.OwnerDrawing, PartnerId = null,
                    CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow
                },
                new BankStatementDescriptionRule
                {
                    CompanyId = _companyId, DescriptionKey = "archived draw",
                    Action = (int)BankStatementRuleAction.OwnerDrawing, PartnerId = archivedId,
                    CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow
                },
                new BankStatementDescriptionRule
                {
                    CompanyId = _companyId, DescriptionKey = "avery expense",
                    Action = (int)BankStatementRuleAction.OwnerDrawing, PartnerId = activeId,
                    CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow
                });
        });

        var legacyLine = Line(-10, "Legacy draw");
        var archivedLine = Line(-15, "Archived draw");
        var expenseLine = Line(-25, "Avery expense");

        var page = await AuditTestHost.Json(await Send(HttpMethod.Get, $"Accounts/{_bankId}/StatementLines?status=unmatched"));
        var lines = page.GetProperty("lines").EnumerateArray().ToList();

        var legacyDto = lines.Single(l => l.GetProperty("id").GetInt32() == legacyLine);
        Assert.Equal("drawing", legacyDto.GetProperty("suggestedOwnerAction").GetString());
        Assert.True(!legacyDto.TryGetProperty("suggestedPartnerId", out var lpid) || lpid.ValueKind == JsonValueKind.Null);

        var archivedDto = lines.Single(l => l.GetProperty("id").GetInt32() == archivedLine);
        Assert.Equal("drawing", archivedDto.GetProperty("suggestedOwnerAction").GetString());
        Assert.True(!archivedDto.TryGetProperty("suggestedPartnerId", out var apid) || apid.ValueKind == JsonValueKind.Null);

        var expenseAccountId = 0;
        WithDb(db => expenseAccountId = db.Accounts.First(a => a.AccountClassId == (int)AccountClasses.Expense
            && !db.Accounts.Any(child => child.ParentAccountId == a.Id)).Id);
        await AssertOk(await Send(HttpMethod.Post, $"StatementLines/{expenseLine}/RecordExpense", new
        {
            expenseAccountId, newVendor = new { name = "Avery Vendor" }, rememberRule = true
        }));
        WithDb(db =>
        {
            var rule = db.BankStatementDescriptionRules.Single(r => r.DescriptionKey == "avery expense");
            Assert.Equal((int)BankStatementRuleAction.Expense, rule.Action);
            Assert.Null(rule.PartnerId);
        });
    }

    [Fact]
    public async Task PartnerMode_GetOwnerActions_FiltersArchivedAndHidesSharedActions()
    {
        var (activeId, _, _) = SetupPartner("Avery", "1");
        SetupPartner("Archived", "2", partnerArchivedAt: DateTime.UtcNow);
        var (noDrawingsId, _, _) = SetupPartner("Blake", "3", drawingsArchivedAt: DateTime.UtcNow);

        var json = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.True(!json.TryGetProperty("drawing", out var d) || d.ValueKind == JsonValueKind.Null);
        Assert.True(!json.TryGetProperty("contribution", out var c) || c.ValueKind == JsonValueKind.Null);
        var partners = json.GetProperty("partners").EnumerateArray().ToList();
        Assert.Equal(2, partners.Count);
        Assert.DoesNotContain(partners, p => p.GetProperty("name").GetString() == "Archived");

        var avery = partners.Single(p => p.GetProperty("partnerId").GetInt32() == activeId);
        Assert.Equal(JsonValueKind.Object, avery.GetProperty("drawing").ValueKind);
        var blake = partners.Single(p => p.GetProperty("partnerId").GetInt32() == noDrawingsId);
        Assert.Equal(JsonValueKind.Null, blake.GetProperty("drawing").ValueKind);
        Assert.Equal(JsonValueKind.Object, blake.GetProperty("contribution").ValueKind);
    }

    [Fact]
    public async Task SoleProprietor_OwnerActionsHaveNullPartners_AndRecordsTo30800()
    {
        var actions = await AuditTestHost.Json(await Send(HttpMethod.Get, "StatementOwnerActions"));
        Assert.True(!actions.TryGetProperty("partners", out var partners) || partners.ValueKind == JsonValueKind.Null);
        Assert.Equal("30800", actions.GetProperty("drawing").GetProperty("accountCode").GetString());

        var id = Line(-55, "Sole draw");
        await AssertOk(await Record(id));
        WithDb(db =>
        {
            var drawingsId = db.Accounts.Single(a => a.AccountCode == "30800").Id;
            Assert.Equal(drawingsId, Assert.Single(db.BankTransactions).ContraAccountId);
        });
    }
}
