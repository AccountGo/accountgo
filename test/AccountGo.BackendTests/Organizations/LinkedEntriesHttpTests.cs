#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Dto.Subscription;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Services.Billing;
using Xunit;

namespace AccountGo.BackendTests.Organizations;

[Collection("WorkspaceSetupHttp")]
public sealed class LinkedEntriesHttpTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linked-entries-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = "";
    private string _userId = "";

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_path}");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlanCatalog>();
                services.AddSingleton<IPlanCatalog, PersonalBooksPlanCatalog>();
            });
        });
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "linked@example.test", Email = "linked@example.test", EmailConfirmed = true };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        _userId = user.Id;
        var security = sp.GetRequiredService<Services.Security.ISecurityService>();
        security.EnsureDefaultPermissionsAndRoles();
        security.AddUser(user.Email!, user.Email!, "Lee", "Owner", new List<int>());
        _token = (await sp.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, string? slug = null,
        string? token = null, string? approvalId = null, string? approverId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? _token);
        if (slug != null) request.Headers.Add("X-AccountGo-Org", slug);
        if (approvalId != null) request.Headers.Add("X-Approval-Request-Id", approvalId);
        if (approverId != null) request.Headers.Add("X-Approved-By-User-Id", approverId);
        if (body != null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task EachLinkedEntryKindCreatesBalancedJournalsWithTheRequiredAccounts()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        using (var scope = _factory.Services.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<Core.Authorization.IWorkspaceApprovalPolicyResolver>();
            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.IndependentReviewerRequired, resolver.Resolve(workId, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost).Mode);
            Assert.Equal(Core.Authorization.WorkspaceApprovalMode.SingleActorAllowed, resolver.Resolve(personalId, Core.Authorization.AccessOperationCatalog.FinancialsJournalPost).Mode);
        }
        var accounts = await SeedAccounts(workId, personalId);
        var cases = new[]
        {
            (Kind: "Personal", PersonalAccount: accounts.PersonalExpenseId, Source: new[] { ("30800", DrOrCrSide.Dr), ("W9001", DrOrCrSide.Cr) }, Mirror: new[] { ("P9002", DrOrCrSide.Dr), ("40900", DrOrCrSide.Cr) }),
            (Kind: "MoveToPersonal", PersonalAccount: accounts.PersonalBankId, Source: new[] { ("30800", DrOrCrSide.Dr), ("W9001", DrOrCrSide.Cr) }, Mirror: new[] { ("P9001", DrOrCrSide.Dr), ("40900", DrOrCrSide.Cr) }),
            (Kind: "MoneyIn", PersonalAccount: accounts.PersonalBankId, Source: new[] { ("W9001", DrOrCrSide.Dr), ("30100", DrOrCrSide.Cr) }, Mirror: new[] { ("40900", DrOrCrSide.Dr), ("P9001", DrOrCrSide.Cr) })
        };
        foreach (var item in cases)
        {
            using var response = await Send(HttpMethod.Post, "/api/linked-entries", new
            {
                kind = item.Kind, date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = item.PersonalAccount
            }, workSlug);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var pairId = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var pair = await db.LinkedEntryPairs.SingleAsync(p => p.Id == pairId);
            var source = await ReadLines(db, pair.SourceCompanyId, pair.SourceJournalId!.Value);
            var mirror = await ReadLines(db, pair.MirrorCompanyId, pair.MirrorJournalId!.Value);
            Assert.Equal(source.Sum(x => x.Debit), source.Sum(x => x.Credit));
            Assert.Equal(mirror.Sum(x => x.Debit), mirror.Sum(x => x.Credit));
            Assert.Equal(25m, source.Sum(x => x.Debit));
            Assert.Equal(25m, mirror.Sum(x => x.Debit));
            Assert.Equal(item.Source.OrderBy(x => x), source.Select(x => (x.Code, x.Side)).OrderBy(x => x));
            Assert.Equal(item.Mirror.OrderBy(x => x), mirror.Select(x => (x.Code, x.Side)).OrderBy(x => x));
            var freelanceIncome = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.CompanyId == personalId && a.AccountCode == "40900");
            Assert.Equal("40000", await db.Accounts.IgnoreQueryFilters().Where(a => a.CompanyId == personalId && a.Id == freelanceIncome.ParentAccountId)
                .Select(a => a.AccountCode).SingleAsync());
        }
    }

    [Fact]
    public async Task FailureAfterSourceJournalSaveRollsBackBothJournalsAndPair()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER fail_mirror_journal BEFORE INSERT ON JournalEntryHeader WHEN NEW.CompanyId = {personalId} BEGIN SELECT RAISE(ABORT, 'injected mirror failure'); END;");
        var workHeadersBefore = await db.JournalEntryHeaders.IgnoreQueryFilters().CountAsync(h => h.CompanyId == workId);
        var mirrorHeadersBefore = await db.JournalEntryHeaders.IgnoreQueryFilters().CountAsync(h => h.CompanyId == personalId);

        using var response = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(workHeadersBefore, await db.JournalEntryHeaders.IgnoreQueryFilters().CountAsync(h => h.CompanyId == workId));
        Assert.Equal(mirrorHeadersBefore, await db.JournalEntryHeaders.IgnoreQueryFilters().CountAsync(h => h.CompanyId == personalId));
        Assert.False(await db.LinkedEntryPairs.AnyAsync(p => p.SourceCompanyId == workId));
    }

    [Fact]
    public async Task FxRequiresPersonalAmountAndStoresImpliedRate()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var personalSetting = await db.CompanySettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == personalId);
            personalSetting.Currency = "EUR";
            await db.SaveChangesAsync();
        }
        var request = new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        };
        using (var missing = await Send(HttpMethod.Post, "/api/linked-entries", request, workSlug))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Contains("linked_entry_mirror_amount_required", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using var response = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            request.kind, request.date, request.amount, request.workBankAccountId, request.personalAccountId, mirrorAmount = 31.25m
        }, workSlug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var checkScope = _factory.Services.CreateScope();
        var pair = await checkScope.ServiceProvider.GetRequiredService<ApiDbContext>().LinkedEntryPairs.SingleAsync(p => p.Id == id);
        Assert.Equal(25m, pair.SourceAmount);
        Assert.Equal(31.25m, pair.MirrorAmount);
        Assert.Equal(1.25m, pair.ExchangeRate);
        Assert.Equal("USD", pair.SourceCurrency);
        Assert.Equal("EUR", pair.MirrorCurrency);
    }

    [Fact]
    public async Task SplitPersonalSpendingKeepsWorkPortionAsExpenseAndMirrorsOnlyPersonalPortion()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        int workExpenseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var expense = new Account { CompanyId = workId, AccountClassId = (int)AccountClasses.Expense,
                AccountCode = "W9002", AccountName = "Work phone", DrOrCrSide = DrOrCrSide.Dr };
            db.Accounts.Add(expense);
            await db.SaveChangesAsync();
            workExpenseId = expense.Id;
        }
        using var response = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId,
            splitWorkExpenseAccountId = workExpenseId, splitWorkAmount = 10m
        }, workSlug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var verify = _factory.Services.CreateScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pair = await dbVerify.LinkedEntryPairs.SingleAsync(p => p.Id == id);
        Assert.Equal(15m, pair.SourceAmount);
        Assert.Equal(15m, pair.MirrorAmount);
        var workLines = await ReadLines(dbVerify, workId, pair.SourceJournalId!.Value);
        Assert.Contains(workLines, l => l.Code == "W9002" && l.Side == DrOrCrSide.Dr && l.Amount == 10m);
        Assert.Contains(workLines, l => l.Code == "30800" && l.Side == DrOrCrSide.Dr && l.Amount == 15m);
        var mirrorLines = await ReadLines(dbVerify, personalId, pair.MirrorJournalId!.Value);
        Assert.Equal(15m, mirrorLines.Sum(l => l.Debit));
        Assert.Contains(mirrorLines, l => l.Code == "P9002" && l.Side == DrOrCrSide.Dr && l.Amount == 15m);
    }

    [Fact]
    public async Task CompanyEntityAndMissingOrClosedYearsAreDeniedWithoutPosting()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var setting = await db.CompanySettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == workId);
        setting.EntityType = EntityType.Corporation;
        await db.SaveChangesAsync();
        var body = new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        };
        using (var companyDenied = await Send(HttpMethod.Post, "/api/linked-entries", body, workSlug))
        {
            Assert.Equal(HttpStatusCode.Forbidden, companyDenied.StatusCode);
            Assert.Contains("linked_entry_not_available", await companyDenied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        setting.EntityType = EntityType.SoleProprietor;
        await db.SaveChangesAsync();
        var year = await db.FinancialYears.IgnoreQueryFilters().FirstAsync(y => y.CompanyId == personalId && y.IsActive);
        year.IsActive = false;
        await db.SaveChangesAsync();
        using var closed = await Send(HttpMethod.Post, "/api/linked-entries", body, workSlug);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Contains("linked_entry_period_closed", await closed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await db.LinkedEntryPairs.AnyAsync(p => p.SourceCompanyId == workId));
    }

    [Fact]
    public async Task ReviewRequiredWorkspaceCannotReversePairAndLeavesBothSidesUntouched()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using var create = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var pairId = body.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pair = await db.LinkedEntryPairs.SingleAsync(p => p.Id == pairId);
        var sourceHeader = await db.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == workId && j.Id == pair.SourceJournalId);
        var mirrorHeader = await db.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == personalId && j.Id == pair.MirrorJournalId);
        var originalGlCount = await db.GeneralLedgerHeaders.IgnoreQueryFilters().CountAsync(g => g.CompanyId == workId || g.CompanyId == personalId);

        using var reverse = await Send(HttpMethod.Post, $"/api/linked-entries/{pairId}/reverse", new
        {
            date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reason = "Retry-safe review guard"
        }, workSlug);

        Assert.True(reverse.StatusCode == HttpStatusCode.OK, await reverse.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", (await db.LinkedEntryPairs.AsNoTracking().SingleAsync(p => p.Id == pairId)).State);
        Assert.False(await db.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(j => j.CompanyId == workId && j.Id == pair.SourceJournalId));
        Assert.False(await db.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(j => j.CompanyId == personalId && j.Id == pair.MirrorJournalId));
        Assert.Equal(originalGlCount, await db.GeneralLedgerHeaders.IgnoreQueryFilters().CountAsync(g => g.CompanyId == workId || g.CompanyId == personalId));
    }

    [Fact]
    public async Task ReviewApprovalPostsMirrorAndRevocationCancelsAndRemovesBothDrafts()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        var reviewer = await AddReviewer(workId, workSlug);
        var createBody = new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        };

        using var first = await Send(HttpMethod.Post, "/api/linked-entries", createBody, workSlug);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var postedPairId = firstJson.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var readScope = _factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var postedPair = await readDb.LinkedEntryPairs.AsNoTracking().SingleAsync(p => p.Id == postedPairId);
        using var approval = await Send(HttpMethod.Post, "/api/organizations/current/members/access-approvals", new
        {
            operationKey = Core.Authorization.AccessOperationCatalog.FinancialsJournalPost,
            resourceId = postedPair.SourceJournalId!.Value.ToString(CultureInfo.InvariantCulture)
        }, workSlug, reviewer.Token);
        Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
        using var approvalJson = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var approvalRequestId = approvalJson.RootElement.GetProperty("approvalRequestId").GetString();
        using (var approvalDbScope = _factory.Services.CreateScope())
        {
            var approvalDb = approvalDbScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var approvalRow = await approvalDb.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.RecordId == approvalRequestId);
            Assert.Equal("__AccessDelegationApproval", approvalRow.TableName);
            Assert.Equal("Approved", approvalRow.OriginalValue);
        }
        using var post = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", new { id = postedPair.SourceJournalId },
            workSlug, _token, approvalRequestId,
            approvalJson.RootElement.GetProperty("approverUserId").GetString());
        Assert.True(post.StatusCode == HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var saved = await db.LinkedEntryPairs.AsNoTracking().SingleAsync(p => p.Id == postedPairId);
            Assert.Equal("Posted", saved.State);
            Assert.NotNull(await db.JournalEntryHeaders.IgnoreQueryFilters().Where(j => j.CompanyId == workId && j.Id == saved.SourceJournalId).Select(j => j.GeneralLedgerHeaderId).SingleAsync());
            Assert.NotNull(await db.JournalEntryHeaders.IgnoreQueryFilters().Where(j => j.CompanyId == personalId && j.Id == saved.MirrorJournalId).Select(j => j.GeneralLedgerHeaderId).SingleAsync());
        }

        using var second = await Send(HttpMethod.Post, "/api/linked-entries", createBody, workSlug);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var cancelledPairId = secondJson.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        var cancelledPair = await readDb.LinkedEntryPairs.SingleAsync(p => p.Id == cancelledPairId);
        using var cancellationApproval = await Send(HttpMethod.Post, "/api/organizations/current/members/access-approvals", new
        {
            operationKey = Core.Authorization.AccessOperationCatalog.FinancialsJournalPost,
            resourceId = cancelledPair.SourceJournalId!.Value.ToString(CultureInfo.InvariantCulture)
        }, workSlug, reviewer.Token);
        Assert.Equal(HttpStatusCode.Created, cancellationApproval.StatusCode);
        using var cancellationJson = JsonDocument.Parse(await cancellationApproval.Content.ReadAsStringAsync());
        var requestId = cancellationJson.RootElement.GetProperty("approvalRequestId").GetString();
        using var revoke = await Send(HttpMethod.Delete, $"/api/organizations/current/members/access-approvals/{requestId}", slug: workSlug);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal("Cancelled", (await readDb.LinkedEntryPairs.AsNoTracking().SingleAsync(p => p.Id == cancelledPairId)).State);
        Assert.False(await readDb.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(j => j.CompanyId == workId && j.Id == cancelledPair.SourceJournalId));
        Assert.False(await readDb.JournalEntryHeaders.IgnoreQueryFilters().AnyAsync(j => j.CompanyId == personalId && j.Id == cancelledPair.MirrorJournalId));
        Assert.Equal(2, await readDb.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.TableName == "__LinkedEntryDraft" &&
            a.RecordId != null && (a.RecordId == cancelledPair.SourceJournalId.ToString() || a.RecordId == cancelledPair.MirrorJournalId.ToString())));
    }

    [Fact]
    public async Task MirrorCannotPostDirectlyAndCancelledSourceCannotPost()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using var create = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug);
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var pairId = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pair = await db.LinkedEntryPairs.SingleAsync(p => p.Id == pairId);
        var sourceJournalId = pair.SourceJournalId!.Value;
        using var mirror = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", new { id = pair.MirrorJournalId }, "linked-personal");
        Assert.True(mirror.StatusCode == HttpStatusCode.Conflict, await mirror.Content.ReadAsStringAsync());
        Assert.Contains("linked_entry_use_pair_reversal", await mirror.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var mirrorJournal = await db.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == personalId && j.Id == pair.MirrorJournalId);
        using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().ImpersonateUser(personalId, _userId, "linked-personal", CompanyMemberRole.Owner))
            Assert.Throws<Services.Financial.LinkedEntryOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>().UpdateJournalEntry(mirrorJournal, posted: true));
        scope.ServiceProvider.GetRequiredService<LinkedEntryService>().CancelPendingSource(workId, sourceJournalId);
        Assert.Equal("Cancelled", (await db.LinkedEntryPairs.AsNoTracking().SingleAsync(p => p.Id == pairId)).State);
        Assert.Equal("Cancelled", scope.ServiceProvider.GetRequiredService<LinkedEntryService>().GetSourcePairState(workId, sourceJournalId));
        using var source = await Send(HttpMethod.Post, "/api/Financials/PostJournalEntry", new { id = sourceJournalId }, workSlug);
        Assert.True(source.StatusCode == HttpStatusCode.Conflict, await source.Content.ReadAsStringAsync());
        Assert.Contains("linked_entry_not_pending", await source.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NightlyIntegrityCheckFlagsAnOrphanPair()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        using var create = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug);
        Assert.True(create.StatusCode == HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var pairId = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pair = await db.LinkedEntryPairs.SingleAsync(p => p.Id == pairId);
        pair.SourceJournalId = null;
        await db.SaveChangesAsync();
        var scanner = new LinkedEntryIntegrityScanner(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<LinkedEntryIntegrityScanner>>());
        Assert.True(await scanner.ScanOnceAsync() >= 1);
    }

    [Fact]
    public async Task NonOwnerIsDeniedAndDuplicateBankIdentityReturnsWarning()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        var reviewer = await AddReviewer(workId, workSlug);
        var rejected = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug, reviewer.Token);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var workBank = await db.Banks.IgnoreQueryFilters().SingleAsync(b => b.Id == accounts.WorkBankId);
            var personalBank = await db.Banks.IgnoreQueryFilters().SingleAsync(b => b.CompanyId == personalId);
            workBank.Number = "same-bank-number";
            personalBank.Number = "same-bank-number";
            await db.SaveChangesAsync();
        }
        using var response = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId
        }, workSlug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This account is also in your work books", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task<(string Id, string Token)> AddReviewer(int companyId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = $"reviewer-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
        user.Email = user.UserName;
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        var security = sp.GetRequiredService<Services.Security.ISecurityService>();
        security.AddUser(user.Email!, user.Email!, "Review", "Admin", new List<int>());
        var db = sp.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var owner = await db.CompanyMembers.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == companyId && m.UserId == _userId);
        var roleIds = await db.CompanyMemberSecurityRoles.IgnoreQueryFilters().Where(r => r.CompanyMemberId == owner.Id)
            .Select(r => r.SecurityRoleId).ToListAsync();
        var member = new CompanyMember { CompanyId = companyId, UserId = user.Id, Role = CompanyMemberRole.Admin, JoinedAt = DateTime.UtcNow };
        db.CompanyMembers.Add(member);
        await db.SaveChangesAsync();
        foreach (var roleId in roleIds) db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
        await db.SaveChangesAsync();
        return (user.Id, (await sp.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken);
    }

    [Fact]
    public async Task SingleActorPairReversalCreatesBothSidesAndUnmatchesStatementLine()
    {
        var (workId, personalId, workSlug) = await CreateLinkedWorkspaces();
        var accounts = await SeedAccounts(workId, personalId);
        int statementLineId;
        using (var setup = _factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var rule = await db.WorkspaceApprovalRules.IgnoreQueryFilters().SingleAsync(r => r.CompanyId == workId
                && r.OperationKey == Core.Authorization.AccessOperationCatalog.FinancialsJournalPost);
            rule.RuleKind = WorkspaceApprovalRuleKind.SingleActorAllowed;
            rule.Provenance = WorkspaceApprovalRuleProvenance.NewWorkspaceConfirmed;
            await db.SaveChangesAsync();
            var import = new BankStatementImport { CompanyId = workId, BankId = accounts.WorkBankId,
                Source = BankStatementSources.Csv, FileName = "linked.csv", ImportedAt = DateTime.UtcNow, ImportedBy = _userId };
            db.BankStatementImports.Add(import);
            await db.SaveChangesAsync();
            var line = new BankStatementLine { CompanyId = workId, BankId = accounts.WorkBankId,
                BankStatementImportId = import.Id, Source = BankStatementSources.Csv,
                ExternalId = Guid.NewGuid().ToString("N"), PostedDate = DateTime.UtcNow.Date,
                Amount = -25m, CreatedAt = DateTime.UtcNow };
            db.BankStatementLines.Add(line);
            await db.SaveChangesAsync();
            statementLineId = line.Id;
        }

        using var create = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "Personal", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 25m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalExpenseId,
            statementLineId
        }, workSlug);
        Assert.True(create.StatusCode == HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var pairId = json.RootElement.GetProperty("pair").GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var dbCheck = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var pair = await dbCheck.LinkedEntryPairs.SingleAsync(p => p.Id == pairId);
        Assert.Equal("Posted", pair.State);
        var source = await dbCheck.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == workId && j.Id == pair.SourceJournalId);
        var mirror = await dbCheck.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == personalId && j.Id == pair.MirrorJournalId);
        var matchedBefore = await dbCheck.BankStatementLines.IgnoreQueryFilters().SingleAsync(l => l.Id == statementLineId);
        Assert.NotNull(matchedBefore.MatchedGeneralLedgerLineId);

        var financial = scope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();
        var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
        await using var rejectedAttempt = await dbCheck.Database.BeginTransactionAsync();
        using (tenant.ImpersonateUser(workId, _userId, workSlug, CompanyMemberRole.Owner))
        {
            Assert.Throws<Services.Financial.LinkedEntryOperationException>(() => financial.UpdateJournalEntry(source, posted: false));
            var gl = financial.CreateReversingEntry(source.GeneralLedgerHeaderId!.Value, "Unscoped attempt", DateTime.UtcNow.Date);
            Assert.Throws<Services.Financial.LinkedEntryOperationException>(() => financial.SaveGeneralLedgerEntry(gl));
        }
        await rejectedAttempt.RollbackAsync();
        await dbCheck.Entry(matchedBefore).ReloadAsync();

        using var reverse = await Send(HttpMethod.Post, $"/api/linked-entries/{pairId}/reverse", new
        {
            date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reason = "Correction"
        }, workSlug);
        Assert.True(reverse.StatusCode == HttpStatusCode.OK, await reverse.Content.ReadAsStringAsync());
        await dbCheck.Entry(pair).ReloadAsync();
        Assert.Equal("Reversed", (await dbCheck.LinkedEntryPairs.SingleAsync(p => p.Id == pairId)).State);
        Assert.NotNull((await dbCheck.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == workId && j.Id == source.Id)).GeneralLedgerHeaderId);
        Assert.NotNull((await dbCheck.JournalEntryHeaders.IgnoreQueryFilters().SingleAsync(j => j.CompanyId == personalId && j.Id == mirror.Id)).GeneralLedgerHeaderId);
        Assert.Single(await dbCheck.GeneralLedgerHeaders.IgnoreQueryFilters().Where(g => g.CompanyId == workId && g.ReversedGeneralLedgerHeaderId == source.GeneralLedgerHeaderId).ToListAsync());
        Assert.Single(await dbCheck.GeneralLedgerHeaders.IgnoreQueryFilters().Where(g => g.CompanyId == personalId && g.ReversedGeneralLedgerHeaderId == mirror.GeneralLedgerHeaderId).ToListAsync());
        var unmatchedLine = await dbCheck.BankStatementLines.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == statementLineId);
        Assert.Null(unmatchedLine.MatchedGeneralLedgerLineId);

        using var second = await Send(HttpMethod.Post, $"/api/linked-entries/{pairId}/reverse", new
        {
            date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, workSlug);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("linked_entry_already_reversed", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var capital = await Send(HttpMethod.Post, "/api/linked-entries", new
        {
            kind = "MoneyIn", date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount = 12m, workBankAccountId = accounts.WorkBankId, personalAccountId = accounts.PersonalBankId
        }, workSlug);
        Assert.Equal(HttpStatusCode.OK, capital.StatusCode);
        using (var statementScope = _factory.Services.CreateScope())
        {
            var incomeTenant = statementScope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
            using var identity = incomeTenant.ImpersonateUser(workId, _userId, workSlug, CompanyMemberRole.Owner);
            var incomeFinancial = statementScope.ServiceProvider.GetRequiredService<Services.Financial.IFinancialService>();
            var income = incomeFinancial.IncomeStatement(new DateTime(DateTime.Today.Year, 1, 1), new DateTime(DateTime.Today.Year, 12, 31));
            Assert.DoesNotContain(income, row => row.AccountCode is "30100" or "30800");
        }
    }

    private async Task<List<(string Code, DrOrCrSide Side, decimal Amount, decimal Debit, decimal Credit)>> ReadLines(ApiDbContext db, int companyId, int journalId)
    {
        var lines = await db.JournalEntryLines.IgnoreQueryFilters().Where(l => l.JournalEntryHeaderId == journalId).ToListAsync();
        Assert.All(lines, line => Assert.Equal(companyId, line.CompanyId));
        var accounts = await db.Accounts.IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.Id, a => a.AccountCode);
        return lines.Select(l => (accounts[l.AccountId], l.DrCr, l.Amount,
            l.DrCr == DrOrCrSide.Dr ? l.Amount : 0m, l.DrCr == DrOrCrSide.Cr ? l.Amount : 0m)).ToList();
    }

    private async Task<(int WorkBankId, int PersonalExpenseId, int PersonalBankId)> SeedAccounts(int workId, int personalId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var workSlug = await db.Companies.IgnoreQueryFilters().Where(c => c.Id == workId).Select(c => c.Slug).SingleAsync();
        var personalSlug = await db.Companies.IgnoreQueryFilters().Where(c => c.Id == personalId).Select(c => c.Slug).SingleAsync();
        async Task<(int AccountId, int BankId)> BankFor(int companyId, string slug, string code, string bankName)
        {
            var account = new Account { CompanyId = companyId, AccountClassId = (int)AccountClasses.Assets, AccountCode = code, AccountName = bankName, IsCash = true, DrOrCrSide = DrOrCrSide.Dr };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            var bank = new Bank { CompanyId = companyId, Name = bankName, AccountId = account.Id, Type = BankTypes.CheckingAccount, IsActive = true, CurrencyCode = "USD" };
            db.Banks.Add(bank);
            await db.SaveChangesAsync();
            return (account.Id, bank.Id);
        }
        var workBank = await BankFor(workId, workSlug!, "W9001", "Work bank");
        var personalBank = await BankFor(personalId, personalSlug!, "P9001", "Personal bank");
        var expense = new Account { CompanyId = personalId, AccountClassId = (int)AccountClasses.Expense, AccountCode = "P9002", AccountName = "Household spending", DrOrCrSide = DrOrCrSide.Dr };
        db.Accounts.Add(expense);
        await db.SaveChangesAsync();
        return (workBank.BankId, expense.Id, personalBank.AccountId);
    }

    private async Task<(int WorkId, int PersonalId, string WorkSlug)> CreateLinkedWorkspaces()
    {
        const string workSlug = "linked-work";
        using var work = await Send(HttpMethod.Post, "/api/organizations", new { name = "Linked work", slug = workSlug, currency = "USD" });
        Assert.Equal(HttpStatusCode.Created, work.StatusCode);
        using var workJson = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        var workId = int.Parse(workJson.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
        using var personal = await Send(HttpMethod.Post, "/api/organizations/personal", new
        {
            name = "Linked personal", slug = "linked-personal", currency = "USD", linkWorkCompanyId = workId,
            booksStartDate = "2026-01-01", fiscalYearPattern = "Calendar"
        });
        Assert.Equal(HttpStatusCode.Created, personal.StatusCode);
        using var personalJson = JsonDocument.Parse(await personal.Content.ReadAsStringAsync());
        var personalId = int.Parse(personalJson.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
        await GrantFinancialPermissions(workId, personalId);
        return (workId, personalId, workSlug);
    }

    private async Task GrantFinancialPermissions(params int[] companyIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var permissionNames = new[] { SystemPermissions.FinancialsJournalPost, SystemPermissions.FinancialsJournalCreate,
            SystemPermissions.FinancialsJournalView, SystemPermissions.FinancialsAccountsView };
        var permissions = await db.SecurityPermissions.IgnoreQueryFilters().Where(p => permissionNames.Contains(p.Name)).ToListAsync();
        Assert.Equal(permissionNames.Length, permissions.Count);
        var role = new SecurityRole { Name = "linked-entry-test", DisplayName = "Linked entry test", System = true };
        db.SecurityRoles.Add(role);
        await db.SaveChangesAsync();
        foreach (var permission in permissions) db.SecurityRolePermissions.Add(new SecurityRolePermission { SecurityRoleId = role.Id, SecurityPermissionId = permission.Id });
        await db.SaveChangesAsync();
        foreach (var companyId in companyIds)
        {
            var member = await db.CompanyMembers.IgnoreQueryFilters().SingleAsync(m => m.CompanyId == companyId && m.UserId == _userId);
            db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = role.Id });
        }
        await db.SaveChangesAsync();
    }

    private sealed class PersonalBooksPlanCatalog : IPlanCatalog
    {
        private readonly CommunityPlanCatalog _community = new();
        public string DefaultPlanKey => _community.DefaultPlanKey;
        public string? PersonalBooksPlanKey => "household-test";
        public IReadOnlyList<PlanDefinitionDto> GetPlans() => _community.GetPlans();
        public bool TryResolvePlan(string? key, out PlanDefinitionDto plan)
        {
            if (key == PersonalBooksPlanKey)
            {
                plan = new() { Key = PersonalBooksPlanKey, Name = "Household", IncludedModules = new() { "financials", "purchases", "reports" } };
                return true;
            }
            return _community.TryResolvePlan(key, out plan);
        }
        public PlanDefinitionDto ResolveStoredPlan(string? key) { TryResolvePlan(key, out var plan); return plan; }
    }
}
