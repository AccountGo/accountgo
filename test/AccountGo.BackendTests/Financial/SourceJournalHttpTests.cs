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
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Financial;

public sealed class SourceJournalHttpTests : IAsyncLifetime
{
    private readonly AuditTestHost _host = new();
    private AuditTestHost.UserSession _owner = null!;
    private AuditTestHost.UserSession _secondAdmin = null!;
    private int _companyId;
    private int _otherCompanyId;
    private int _cashAccountId;
    private int _revenueAccountId;
    private int _otherCashAccountId;
    private int _otherRevenueAccountId;
    private const string Org = "source-journal-http";
    private const string OtherOrg = "source-journal-http-other";
    private static readonly DateTime BooksStart = new(2025, 1, 1);

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _owner = await _host.CreateUser("sj-owner@example.test");
        _secondAdmin = await _host.CreateUser("sj-admin@example.test");
        _companyId = await _host.CreateWorkspace(_owner.Token, "Source Journal HTTP", Org);
        _otherCompanyId = await _host.CreateWorkspace(_owner.Token, "Other HTTP", OtherOrg);
        await _host.SetFunctionalRoles(_companyId, _owner.Id, "Accountant");
        await _host.SetFunctionalRoles(_otherCompanyId, _owner.Id, "Accountant");
        await _host.AddMember(_companyId, _secondAdmin.Id, CompanyMemberRole.Admin, new[] { "Accountant" });

        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        using (tenant.Impersonate(_companyId, Org, CompanyMemberRole.Owner))
        {
            var setting = db.CompanySettings.Single();
            setting.BooksStartDate = BooksStart;
            foreach (var year in new[] { 2025, 2026 })
            {
                if (!db.FinancialYears.Any(y => y.StartDate.Year == year))
                {
                    db.FinancialYears.Add(new FinancialYear
                    {
                        CompanyId = _companyId,
                        FiscalYearCode = year.ToString(CultureInfo.InvariantCulture),
                        FiscalYearName = year.ToString(CultureInfo.InvariantCulture),
                        StartDate = new DateTime(year, 1, 1),
                        EndDate = new DateTime(year, 12, 31),
                        IsActive = true
                    });
                }
            }
            _cashAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("1")).Id;
            _revenueAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("4")).Id;
            db.SaveChanges();
        }

        using (tenant.Impersonate(_otherCompanyId, OtherOrg, CompanyMemberRole.Owner))
        {
            var setting = db.CompanySettings.Single();
            setting.BooksStartDate = BooksStart;
            _otherCashAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("1")).Id;
            _otherRevenueAccountId = db.Accounts.Include(a => a.ChildAccounts).Where(a => a.ArchivedAt == null).AsEnumerable().First(a => a.CanPost() && a.AccountCode.StartsWith("4")).Id;
            db.SaveChanges();
        }
    }

    public Task DisposeAsync() => _host.DisposeAsync();

    private int CreateSourceDraft(string key, DateTime? date = null, string sourceSystem = "example-feed", int? companyId = null, string? orgSlug = null)
    {
        var targetCompanyId = companyId ?? _companyId;
        var targetOrg = orgSlug ?? Org;
        var cashId = targetCompanyId == _companyId ? _cashAccountId : _otherCashAccountId;
        var revId = targetCompanyId == _companyId ? _revenueAccountId : _otherRevenueAccountId;

        using var scope = _host.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.ImpersonateUser(targetCompanyId, _owner.Id, targetOrg, CompanyMemberRole.Owner);
        var svc = scope.ServiceProvider.GetRequiredService<SourceJournalService>();
        var res = svc.CreateDraft(new SourceJournalDraftRequest(
            CompanyId: targetCompanyId,
            SourceSystem: sourceSystem,
            SourceKey: key,
            PreparerId: "system:example-importer",
            Date: date ?? new DateTime(2026, 11, 10),
            Memo: $"Imported {key}",
            ReferenceNo: key,
            Lines: new[]
            {
                new SourceJournalDraftLine(cashId, DrOrCrSide.Dr, 100m, "Debit"),
                new SourceJournalDraftLine(revId, DrOrCrSide.Cr, 100m, "Credit")
            }));
        return res.JournalId;
    }

    private Task<HttpResponseMessage> Send(string path, HttpMethod? method = null, object? body = null) =>
        _host.Send(method ?? HttpMethod.Get, "/api/Financials/" + path, body, _owner.Token, Org);

    [Fact]
    public async Task SaveJournalEntry_OnImportedDraft_Returns409SourceJournalLocked()
    {
        var id = CreateSourceDraft("http6:lock:v1");

        var resp = await Send("SaveJournalEntry", HttpMethod.Post, new
        {
            id,
            journalDate = "2026-11-10",
            voucherType = (int)JournalVoucherTypes.Other,
            referenceNo = "TAMPER",
            memo = "Tampered memo",
            sourceSystem = "tampered",
            sourceKey = "tampered",
            journalEntryLines = new[]
            {
                new { id = 0, accountId = _cashAccountId, drCr = 1, amount = 200m, memo = "Dr" },
                new { id = 0, accountId = _revenueAccountId, drCr = 2, amount = 200m, memo = "Cr" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await AuditTestHost.Json(resp);
        Assert.Equal("source_journal_locked", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostJournalEntry_SystemPreparedDraft_WorksInNormalAndReviewRequiredWorkspaceWithoutApprovalHeaders()
    {
        // Normal (default SingleActorAllowed or solo) workspace
        var normalId = CreateSourceDraft("http7:normal:v1");
        var normalResp = await Send("PostJournalEntry", HttpMethod.Post, new { id = normalId, journalDate = "2026-11-10" });
        Assert.Equal(HttpStatusCode.OK, normalResp.StatusCode);

        // Switch workspace to IndependentReviewerRequired
        var getSettings = await _host.Send(HttpMethod.Get, "/api/organizations/current/operating-settings", null, _owner.Token, Org);
        Assert.Equal(HttpStatusCode.OK, getSettings.StatusCode);
        var settingsJson = await AuditTestHost.Json(getSettings);
        var rev = settingsJson.GetProperty("policyRevision").GetInt32();

        var setPolicy = await _host.Send(
            HttpMethod.Post,
            "/api/organizations/current/operating-settings/journal-approval-policy",
            new { expectedPolicyRevision = rev, journalWorkflow = "IndependentReviewerRequired" },
            _owner.Token,
            Org);
        Assert.Equal(HttpStatusCode.OK, setPolicy.StatusCode);

        // Human Owner posts a system:-prepared draft WITHOUT approval headers
        var reviewId = CreateSourceDraft("http7:review:v1");
        var reviewResp = await Send("PostJournalEntry", HttpMethod.Post, new { id = reviewId, journalDate = "2026-11-10" });
        Assert.True(reviewResp.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK in review-required workspace, got {(int)reviewResp.StatusCode}: {await reviewResp.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task PostJournalEntry_ImportedDraft_StillRefusedByBooksLockAndClosedPeriod()
    {
        var lockedDateId = CreateSourceDraft("http8:lockdate:v1", new DateTime(2026, 6, 15));
        var closedYearId = CreateSourceDraft("http8:closedyr:v1", new DateTime(2025, 6, 15));

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tenant.Impersonate(_companyId, Org, CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var setting = db.CompanySettings.Single();
            setting.BooksLockedThrough = new DateTime(2026, 6, 30);
            var fy2025 = db.FinancialYears.Single(y => y.StartDate.Year == 2025);
            fy2025.IsActive = false;
            fy2025.ClosedAt = DateTime.UtcNow;
            db.SaveChanges();
        }

        var lockedResp = await Send("PostJournalEntry", HttpMethod.Post, new { id = lockedDateId, journalDate = "2026-06-15" });
        Assert.Equal(HttpStatusCode.Forbidden, lockedResp.StatusCode);

        var closedResp = await Send("PostJournalEntry", HttpMethod.Post, new { id = closedYearId, journalDate = "2025-06-15" });
        Assert.NotEqual(HttpStatusCode.OK, closedResp.StatusCode);
    }

    [Fact]
    public async Task PostBatch_HandlesMixedIdsIndependently()
    {
        var valid1 = CreateSourceDraft("http9:valid1:v1", new DateTime(2026, 11, 10));
        var alreadyPosted = CreateSourceDraft("http9:posted:v1", new DateTime(2026, 11, 10));
        Assert.Equal(HttpStatusCode.OK, (await Send("PostJournalEntry", HttpMethod.Post, new { id = alreadyPosted, journalDate = "2026-11-10" })).StatusCode);

        var lockedPeriod = CreateSourceDraft("http9:locked:v1", new DateTime(2026, 5, 10));
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tenant.Impersonate(_companyId, Org, CompanyMemberRole.Owner);
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.CompanySettings.Single().BooksLockedThrough = new DateTime(2026, 5, 31);
            db.SaveChanges();
        }

        var otherCompanyDraft = CreateSourceDraft("http9:other:v1", new DateTime(2026, 11, 10), companyId: _otherCompanyId, orgSlug: OtherOrg);
        var nonExistent = 999999;

        var batchResp = await Send("JournalEntries/PostBatch", HttpMethod.Post, new
        {
            ids = new[] { valid1, alreadyPosted, otherCompanyDraft, lockedPeriod, nonExistent }
        });

        Assert.Equal(HttpStatusCode.OK, batchResp.StatusCode);
        var arr = await AuditTestHost.Json(batchResp);
        var items = arr.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetInt32());

        Assert.True(items[valid1].GetProperty("posted").GetBoolean());
        Assert.False(items[alreadyPosted].GetProperty("posted").GetBoolean());
        Assert.False(items[otherCompanyDraft].GetProperty("posted").GetBoolean());
        Assert.False(items[lockedPeriod].GetProperty("posted").GetBoolean());
        Assert.False(items[nonExistent].GetProperty("posted").GetBoolean());
    }

    [Fact]
    public async Task DiscardImported_HttpEndpoint_FreesKeyAndRefusesPostedOrManual()
    {
        var draftId = CreateSourceDraft("http10:discard:v1");

        var discardResp = await Send($"JournalEntries/{draftId}/DiscardImported", HttpMethod.Post);
        Assert.Equal(HttpStatusCode.OK, discardResp.StatusCode);

        // Recreating with the same key succeeds
        var recreatedId = CreateSourceDraft("http10:discard:v1");
        Assert.NotEqual(draftId, recreatedId);

        // Post it, then attempt discard -> 409 source_journal_posted
        Assert.Equal(HttpStatusCode.OK, (await Send("PostJournalEntry", HttpMethod.Post, new { id = recreatedId, journalDate = "2026-11-10" })).StatusCode);
        var discardPosted = await Send($"JournalEntries/{recreatedId}/DiscardImported", HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Conflict, discardPosted.StatusCode);
        Assert.Equal("source_journal_posted", (await AuditTestHost.Json(discardPosted)).GetProperty("code").GetString());

        // Manual draft -> 409 not_a_source_journal
        var saveManual = await Send("SaveJournalEntry", HttpMethod.Post, new
        {
            id = 0,
            journalDate = "2026-11-10",
            voucherType = (int)JournalVoucherTypes.Other,
            referenceNo = "MAN-1",
            memo = "Manual",
            sourceSystem = "should-be-ignored",
            sourceKey = "should-be-ignored",
            journalEntryLines = new[]
            {
                new { id = 0, accountId = _cashAccountId, drCr = 1, amount = 50m, memo = "Dr" },
                new { id = 0, accountId = _revenueAccountId, drCr = 2, amount = 50m, memo = "Cr" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, saveManual.StatusCode);
        var manualId = (await AuditTestHost.Json(saveManual)).GetProperty("id").GetInt32();

        var discardManual = await Send($"JournalEntries/{manualId}/DiscardImported", HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Conflict, discardManual.StatusCode);
        Assert.Equal("not_a_source_journal", (await AuditTestHost.Json(discardManual)).GetProperty("code").GetString());

        // Filter tests: source=any, source=none, source=example-feed
        var anyList = (await AuditTestHost.Json(await Send("JournalEntries?source=any"))).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        var noneList = (await AuditTestHost.Json(await Send("JournalEntries?source=none"))).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        var feedList = (await AuditTestHost.Json(await Send("JournalEntries?source=example-feed"))).EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();

        Assert.Contains(recreatedId, anyList);
        Assert.DoesNotContain(manualId, anyList);
        Assert.Contains(manualId, noneList);
        Assert.DoesNotContain(recreatedId, noneList);
        Assert.Contains(recreatedId, feedList);
    }
}
