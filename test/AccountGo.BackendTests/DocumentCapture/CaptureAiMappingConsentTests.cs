// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.Controllers;
using Api.Data;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class CaptureAiMappingConsentTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");
    private readonly TestTenant _tenant = new();
    private readonly ApiDbContext _db;
    private readonly ApplicationIdentityDbContext _identity;
    private readonly RecordingAudit _audit = new();
    private readonly FixedAvailability _availability = new(CaptureAiMappingAvailability.Offered);
    private readonly DocumentCaptureOptions _options = new() { Enabled = true, AiMapping = { Enabled = true } };

    public CaptureAiMappingConsentTests()
    {
        _connection.Open();
        _identityConnection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
        _db.Database.EnsureCreated();
        _db.Companies.Add(new Company { Name = "One Co", CompanyCode = "ONE", Slug = "one-co" });
        _db.Companies.Add(new Company { Name = "Two Co", CompanyCode = "TWO", Slug = "two-co" });
        _db.SaveChanges();
        _identity = new ApplicationIdentityDbContext(new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(_identityConnection).Options);
        _identity.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _identity.Dispose();
        _db.Dispose();
        _identityConnection.Dispose();
        _connection.Dispose();
    }

    private EfCaptureAiMappingConsentStore Store(TimeProvider? clock = null) => new(_db, clock);

    private CaptureAiMappingSettingsController Controller(CompanyMemberRole role = CompanyMemberRole.Owner, string userId = "owner-1")
    {
        _tenant.Role = role;
        _tenant.UserId = userId;
        return new CaptureAiMappingSettingsController(_tenant, _availability, Store(), Options.Create(_options), _identity, _audit);
    }

    private static JsonElement Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    private static int StatusOf(IActionResult result) => Assert.IsType<ObjectResult>(result).StatusCode!.Value;

    // ---- the store ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GrantingRecordsTheCurrentNoticeVersionAndIsIdempotent()
    {
        Assert.True(await Store().GrantAsync(1, "owner-1", CancellationToken.None));
        Assert.False(await Store().GrantAsync(1, "owner-1", CancellationToken.None));

        var consent = await Store().GetAsync(1, CancellationToken.None);
        Assert.NotNull(consent);
        Assert.Equal(CaptureAiMappingConsentNotice.CurrentVersion, consent.ConsentVersion);
        Assert.Equal("owner-1", consent.ConsentedByUserId);
        Assert.True(consent.IsCurrent);
        Assert.Single(await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task RevokingKeepsTheRowAsEvidenceAndIsIdempotent()
    {
        Assert.False(await Store().RevokeAsync(1, "owner-1", CancellationToken.None));
        await Store().GrantAsync(1, "owner-1", CancellationToken.None);

        Assert.True(await Store().RevokeAsync(1, "owner-2", CancellationToken.None));
        Assert.False(await Store().RevokeAsync(1, "owner-2", CancellationToken.None));

        var consent = await Store().GetAsync(1, CancellationToken.None);
        Assert.NotNull(consent);
        Assert.False(consent.IsCurrent);
        Assert.NotNull(consent.RevokedAt);
        var row = await _db.CaptureAiMappingConsents.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal("owner-2", row.RevokedByUserId);
    }

    [Fact]
    [Trait("Rule", "AI-08")]
    public async Task GrantingAgainAfterARevokeReplacesTheConsent()
    {
        await Store().GrantAsync(1, "owner-1", CancellationToken.None);
        await Store().RevokeAsync(1, "owner-1", CancellationToken.None);

        Assert.True(await Store().GrantAsync(1, "owner-2", CancellationToken.None));

        var consent = await Store().GetAsync(1, CancellationToken.None);
        Assert.True(consent!.IsCurrent);
        Assert.Equal("owner-2", consent.ConsentedByUserId);
        Assert.Single(await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    [Trait("Rule", "AI-08")]
    public async Task ConsentToAnOlderNoticeIsNotCurrentAndNeedsReconsent()
    {
        await Store().GrantAsync(1, "owner-1", CancellationToken.None);
        await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ExecuteUpdateAsync(set => set.SetProperty(c => c.ConsentVersion, "2020-01-01"));

        var consent = await Store().GetAsync(1, CancellationToken.None);
        Assert.False(consent!.IsCurrent);
        Assert.True(consent.NeedsReconsent);

        Assert.True(await Store().GrantAsync(1, "owner-1", CancellationToken.None));
        Assert.True((await Store().GetAsync(1, CancellationToken.None))!.IsCurrent);
    }

    [Fact]
    public async Task ConsentIsReadPerWorkspaceWithoutATenant()
    {
        await Store().GrantAsync(1, "owner-1", CancellationToken.None);
        _tenant.CompanyIdValue = null;

        Assert.True((await Store().GetAsync(1, CancellationToken.None))!.IsCurrent);
        Assert.Null(await Store().GetAsync(2, CancellationToken.None));
    }

    // ---- the API --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOwnerGrantsAndRevokesAndEachChangeIsAuditedWithMetadataOnly()
    {
        var granted = Body(await Controller().Put(new(true), CancellationToken.None));
        Assert.True(granted.GetProperty("enabled").GetBoolean());
        Assert.Equal(CaptureAiMappingConsentNotice.CurrentVersion, granted.GetProperty("consentVersion").GetString());
        Assert.True(granted.GetProperty("canManage").GetBoolean());

        var revoked = Body(await Controller().Put(new(false), CancellationToken.None));
        Assert.False(revoked.GetProperty("enabled").GetBoolean());

        Assert.Equal(["document.capture.ai-consent.granted", "document.capture.ai-consent.revoked"], _audit.Events.Select(e => e.EventType));
        Assert.All(_audit.Events, e => Assert.DoesNotContain("owner-1@example.com", JsonSerializer.Serialize(e.Metadata)));
    }

    [Fact]
    public async Task RepeatingTheSameChoiceChangesNothingAndWritesNoAuditEvent()
    {
        await Controller().Put(new(true), CancellationToken.None);
        await Controller().Put(new(true), CancellationToken.None);
        await Controller().Put(new(false), CancellationToken.None);
        await Controller().Put(new(false), CancellationToken.None);

        Assert.Equal(2, _audit.Events.Count);
    }

    [Theory]
    [Trait("Rule", "AI-08")]
    [InlineData(CompanyMemberRole.Admin)]
    [InlineData(CompanyMemberRole.Member)]
    public async Task OnlyTheOwnerCanChangeIt(CompanyMemberRole role)
    {
        Assert.Equal(403, StatusOf(await Controller(role).Put(new(true), CancellationToken.None)));
        Assert.Empty(await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task MembersSeeTheStateReadOnlyAndOnlyOwnersAndAdminsSeeWhoConsented()
    {
        _identity.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1@example.com", Email = "owner-1@example.com" });
        await _identity.SaveChangesAsync();
        await Controller().Put(new(true), CancellationToken.None);

        var member = Body(await Controller(CompanyMemberRole.Member, "member-1").Get(CancellationToken.None));
        Assert.True(member.GetProperty("enabled").GetBoolean());
        Assert.False(member.GetProperty("canManage").GetBoolean());
        Assert.Equal(JsonValueKind.Null, member.GetProperty("consentedBy").ValueKind);

        var admin = Body(await Controller(CompanyMemberRole.Admin, "admin-1").Get(CancellationToken.None));
        Assert.Equal("owner-1@example.com", admin.GetProperty("consentedBy").GetString());
    }

    [Theory]
    [InlineData("not_offered")]
    [InlineData("plan")]
    [InlineData("turned_off_by_support")]
    [InlineData("cap_not_set")]
    public async Task AnUnavailableWorkspaceGetsAClosedSetReasonAndCannotTurnItOn(string reason)
    {
        _availability.Current = CaptureAiMappingAvailability.Unavailable(reason);

        var view = Body(await Controller().Get(CancellationToken.None));
        Assert.False(view.GetProperty("available").GetBoolean());
        Assert.Equal(reason, view.GetProperty("unavailableReason").GetString());

        Assert.Equal(409, StatusOf(await Controller().Put(new(true), CancellationToken.None)));
        Assert.Empty(await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TurningItOffIsAlwaysAllowedEvenWhenTheHostNoLongerOffersIt()
    {
        await Controller().Put(new(true), CancellationToken.None);
        _availability.Current = CaptureAiMappingAvailability.Unavailable("plan");

        var view = Body(await Controller().Put(new(false), CancellationToken.None));

        Assert.False(view.GetProperty("enabled").GetBoolean());
        Assert.Equal("document.capture.ai-consent.revoked", _audit.Events[^1].EventType);
    }

    [Fact]
    public async Task ConsentToAnOlderNoticeReadsAsNotEnabledWithReconsentNeeded()
    {
        await Controller().Put(new(true), CancellationToken.None);
        await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ExecuteUpdateAsync(set => set.SetProperty(c => c.ConsentVersion, "2020-01-01"));

        var view = Body(await Controller().Get(CancellationToken.None));

        Assert.False(view.GetProperty("enabled").GetBoolean());
        Assert.True(view.GetProperty("needsReconsent").GetBoolean());
    }

    [Fact]
    public async Task ADisabledDocumentCaptureModuleReportsNotOffered()
    {
        _options.Enabled = false;

        var view = Body(await Controller().Get(CancellationToken.None));

        Assert.False(view.GetProperty("available").GetBoolean());
        Assert.Equal("not_offered", view.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task WithAiMappingOffInConfigurationNothingIsOfferedEvenWhenTheHostOffersIt()
    {
        _options.AiMapping.Enabled = false;

        var view = Body(await Controller().Get(CancellationToken.None));
        Assert.False(view.GetProperty("available").GetBoolean());
        Assert.Equal("not_offered", view.GetProperty("unavailableReason").GetString());

        Assert.Equal(409, StatusOf(await Controller().Put(new(true), CancellationToken.None)));
        Assert.Empty(await _db.CaptureAiMappingConsents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AGrantFailureThatIsNotTheUniqueIndexRaceSurfacesInsteadOfLookingLikeAnAlreadyGrantedConsent()
    {
        // A table that cannot take the insert: the save fails, no consent row exists, so the failure must not be swallowed.
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"CaptureAiMappingConsents\"");
        await _db.Database.ExecuteSqlRawAsync("CREATE TABLE \"CaptureAiMappingConsents\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"CompanyId\" INTEGER NOT NULL, \"ConsentVersion\" TEXT NOT NULL, \"ConsentedAt\" TEXT NOT NULL, \"ConsentedByUserId\" TEXT NOT NULL, \"RevokedAt\" TEXT NULL, \"RevokedByUserId\" TEXT NULL, CHECK (\"CompanyId\" < 0))");

        await Assert.ThrowsAsync<DbUpdateException>(() => Store().GrantAsync(1, "owner-1", CancellationToken.None));
    }

    [Fact]
    public async Task GrantAndRevokeKeepTheTenantFilterSoTheyNeverTouchAnotherWorkspace()
    {
        await Store().GrantAsync(1, "owner-1", CancellationToken.None);
        _tenant.CompanyIdValue = 2;

        Assert.False(await Store().RevokeAsync(1, "owner-2", CancellationToken.None));
        Assert.True((await Store().GetAsync(1, CancellationToken.None))!.IsCurrent);
    }

    [Fact]
    public async Task ABodyWithoutAChoiceIsRejected() =>
        Assert.Equal(400, StatusOf(await Controller().Put(new(null), CancellationToken.None)));

    // ---- defaults -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheDefaultsOfferNothingAndMeterNothing()
    {
        var services = new ServiceCollection();
        services.AddCaptureAiMapping(new DocumentCaptureAiMappingOptions());
        using var provider = services.BuildServiceProvider();

        var availability = await provider.GetRequiredService<ICaptureAiMappingAvailability>().GetAsync(1, CancellationToken.None);
        Assert.False(availability.Available);
        Assert.Equal("not_offered", availability.UnavailableReason);
        Assert.False(await provider.GetRequiredService<ICaptureAiMappingPolicy>().IsAllowedAsync(1, "purchase-invoice", CancellationToken.None));
        Assert.True(await provider.GetRequiredService<ICaptureAiMappingMeter>().ReserveAsync(1, 3, CancellationToken.None));
    }

    [Fact]
    public async Task AHostRegistrationBeforeTheDefaultsIsKept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICaptureAiMappingAvailability>(new FixedAvailability(CaptureAiMappingAvailability.Offered));
        services.AddCaptureAiMapping(new DocumentCaptureAiMappingOptions());
        using var provider = services.BuildServiceProvider();

        Assert.True((await provider.GetRequiredService<ICaptureAiMappingAvailability>().GetAsync(1, CancellationToken.None)).Available);
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("plan", false, "plan")]
    [InlineData("cap_not_set", false, "cap_not_set")]
    [InlineData("nonsense", true, null)]
    public async Task TheFakeMapperOffersTheSettingOrAReasonForLocalChecks(string? reason, bool available, string? expected)
    {
        var services = new ServiceCollection();
        services.AddCaptureAiMapping(new DocumentCaptureAiMappingOptions { Enabled = true, Mapper = "fake", FakeUnavailableReason = reason });
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ICaptureAiMappingAvailability>().GetAsync(1, CancellationToken.None);

        Assert.Equal(available, result.Available);
        Assert.Equal(expected, result.UnavailableReason);
    }

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class FixedAvailability(CaptureAiMappingAvailability current) : ICaptureAiMappingAvailability
    {
        public CaptureAiMappingAvailability Current { get; set; } = current;
        public Task<CaptureAiMappingAvailability> GetAsync(int companyId, CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    private sealed class TestTenant : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int? CompanyIdValue { get; set; } = 1;
        public int? CompanyId => CompanyIdValue;
        public string? CompanySlug => "one-co";
        public CompanyMemberRole? Role { get; set; } = CompanyMemberRole.Owner;
        public string? UserId { get; set; } = "owner-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => CompanyIdValue is not null;
        public int RequireCompanyId() => CompanyIdValue ?? 1;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed record AuditEvent(string EventType, string? Outcome, int? CompanyId, object? Metadata);

    private sealed class RecordingAudit : ISecurityAuditService
    {
        public List<AuditEvent> Events { get; } = [];
        public Task LogEventAsync(string eventType, string? userId, int? companyId, string? ipAddress, string? userAgent, object? metadata = null)
        {
            Events.Add(new AuditEvent(eventType, null, companyId, metadata));
            return Task.CompletedTask;
        }
        public Task LogEventAsync(string eventType, string? outcome, string? userId, int? companyId, object? metadata = null)
        {
            Events.Add(new AuditEvent(eventType, outcome, companyId, metadata));
            return Task.CompletedTask;
        }
    }
}
