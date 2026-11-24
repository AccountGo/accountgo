// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.Data;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class CaptureAiMappingTests : IDisposable
{
    private const string RuleFields = """{"date":"2026-11-12","vendorName":"Northstar"}""";
    private const string RawText = "Northstar Supplies Ltd\nInvoice No: NS-20457\nDate: 2026-11-12";
    private const string MappedFields = """{"date":"2026-11-12","vendorInvoiceNo":"NS-20457","vendorName":"Northstar Supplies Ltd"}""";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableTenant _tenant = new(1);
    private readonly ApiDbContext _db;
    private readonly DocumentCaptureOptions _captureOptions = new() { AiMapping = new DocumentCaptureAiMappingOptions { Enabled = true } };
    private readonly RecordingAudit _audit = new();
    private readonly AllowPolicy _policy = new();
    private readonly CaptureAiMappingRateLimiter _limiter = new();

    public CaptureAiMappingTests()
    {
        _connection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
        _db.Database.EnsureCreated();
        _db.Companies.Add(new Company { Name = "One Co", CompanyCode = "ONE", Slug = "one-co" });
        _db.Companies.Add(new Company { Name = "Two Co", CompanyCode = "TWO", Slug = "two-co" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<int> AddReadyCaptureAsync(int companyId = 1, string kind = "purchase-invoice", string contentHash = "a",
        string fieldsJson = RuleFields, string rawText = RawText, int generation = 1)
    {
        _tenant.Set(companyId);
        var capture = new Core.Domain.DocumentCapture
        {
            CompanyId = companyId, Kind = kind, SchemaVersion = 1, Status = "ready", SourceHash = new string('s', 64),
            ContentHash = contentHash.PadRight(64, 'c'), NormalizationVersion = "image-normalization-v2", ContentType = "image/png",
            ByteCount = 1, PageCount = 1, RequestFingerprint = new string('f', 64), ExtractionGeneration = generation,
            FieldsJson = fieldsJson, WarningsJson = "[]", RawText = rawText, ProviderName = "fake", ProviderVersion = "1",
            CreatedBy = "tester", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(24),
        };
        _db.DocumentCaptures.Add(capture);
        await _db.SaveChangesAsync();
        _tenant.Set(1);
        return capture.Id;
    }

    private Task<bool> EnqueueAsync(int captureId, int companyId = 1, string kind = "purchase-invoice", string contentHash = "a",
        int generation = 1, ICaptureAiMappingPolicy? policy = null)
    {
        _tenant.Set(companyId);
        return CaptureAiMappingQueue.TryEnqueueAsync(_db, Options.Create(_captureOptions), policy ?? _policy, companyId, captureId, generation, kind,
            contentHash.PadRight(64, 'c'), DateTime.UtcNow, CancellationToken.None);
    }

    private DocumentCaptureAiMappingProcessor Processor(ICaptureFieldMapper mapper, TimeProvider? clock = null) =>
        new(_db, mapper, _policy, _limiter, Options.Create(_captureOptions), NullLogger<DocumentCaptureAiMappingProcessor>.Instance, _audit, clock);

    private Task<Core.Domain.DocumentCapture> CaptureAsync(int id) => _db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    private Task<DocumentCaptureAiMapping> JobAsync(int captureId) =>
        _db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.CaptureId == captureId);

    // ---- opt-in: off by default -----------------------------------------------------------------------------------

    [Fact]
    public async Task TheDefaultPolicyAllowsNothingSoNoJobIsQueued()
    {
        var capture = await AddReadyCaptureAsync();

        Assert.False(await new DisabledCaptureAiMappingPolicy().IsAllowedAsync(1, "purchase-invoice", CancellationToken.None));
        Assert.False(await EnqueueAsync(capture, policy: new DisabledCaptureAiMappingPolicy()));
        Assert.Empty(await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AiMappingDisabledInConfigurationQueuesNothingEvenWhenThePolicyAllows()
    {
        _captureOptions.AiMapping.Enabled = false;
        var capture = await AddReadyCaptureAsync();

        Assert.False(await EnqueueAsync(capture));
        Assert.Empty(await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
        Assert.False(await Processor(new ScriptedMapper(MappedFields)).ProcessNextAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    public async Task BankAndAssistantCapturesAreNeverQueued(string kind)
    {
        var capture = await AddReadyCaptureAsync(kind: kind);

        Assert.False(await EnqueueAsync(capture, kind: kind));
        Assert.Empty(await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task EnqueueingTheSameExtractionTwiceKeepsOneJob()
    {
        var capture = await AddReadyCaptureAsync();

        Assert.True(await EnqueueAsync(capture));
        Assert.False(await EnqueueAsync(capture));
        Assert.Single(await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
        Assert.True(await EnqueueAsync(capture, generation: 2));
        Assert.Equal(2, await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().CountAsync());
    }

    // ---- end to end -----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task AFakeMapperJobImprovesTheFieldsListsThemAsAiAndAuditsMetadataOnly()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);

        Assert.True(await Processor(new FakeCaptureFieldMapper()).ProcessNextAsync(CancellationToken.None));

        var stored = await CaptureAsync(capture);
        using var fields = JsonDocument.Parse(stored.FieldsJson!);
        Assert.Equal("Northstar Supplies Ltd", fields.RootElement.GetProperty("vendorName").GetString());
        Assert.Equal("NS-20457", fields.RootElement.GetProperty("vendorInvoiceNo").GetString());
        Assert.Equal("ai", fields.RootElement.GetProperty("fieldSources").GetProperty("vendorName").GetString());
        Assert.Contains("AI_DISAGREES_WITH_RULES", stored.WarningsJson);
        Assert.Equal("ready", stored.Status);

        var job = await JobAsync(capture);
        Assert.Equal("done", job.Status);
        Assert.Equal("applied", job.Outcome);
        Assert.Equal("fake", job.MapperName);
        Assert.Equal(1, job.Attempts);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.LeaseUntil);
        Assert.Contains("Northstar", job.RuleHintsJson);
        Assert.Contains("vendorName", job.RuleHintsJson);

        var audit = Assert.Single(_audit.Events);
        Assert.Equal("document.capture.ai-mapped", audit.EventType);
        var metadata = JsonSerializer.Serialize(audit.Metadata);
        Assert.Contains("applied", metadata);
        Assert.DoesNotContain("Northstar Supplies", metadata);
        Assert.DoesNotContain("NS-20457", metadata);
    }

    [Fact]
    public async Task AMapperThatChangesNothingFinishesAsUnchangedWithoutTouchingTheCapture()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);

        await Processor(new RuleOnlyFieldMapper()).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(RuleFields, (await CaptureAsync(capture)).FieldsJson);
        Assert.Equal("unchanged", (await JobAsync(capture)).Outcome);
        Assert.Equal("done", (await JobAsync(capture)).Status);
    }

    [Fact]
    public async Task AnInventedValueIsDroppedByTheGuardsBeforeItIsStored()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);

        await Processor(new ScriptedMapper("""{"date":"2026-11-12","vendorInvoiceNo":"ZZ-99999","vendorName":"Northstar"}""")).ProcessNextAsync(CancellationToken.None);

        var stored = await CaptureAsync(capture);
        Assert.DoesNotContain("ZZ-99999", stored.FieldsJson);
        Assert.Contains("AI_VALUE_NOT_IN_SOURCE", stored.WarningsJson);
    }

    // ---- AI-06: conditional apply ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task ACaptureAcceptedBeforeTheJobRunsIsSkippedAndNeverOverwritten()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == capture)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, "accepted").SetProperty(c => c.AcceptedAt, DateTime.UtcNow)
                .SetProperty(c => c.AcceptedFieldsJson, """{"vendorName":"Typed by user"}"""));
        var mapper = new ScriptedMapper(MappedFields);

        await Processor(mapper).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(0, mapper.Calls);
        Assert.Equal(RuleFields, (await CaptureAsync(capture)).FieldsJson);
        Assert.Equal("skipped", (await JobAsync(capture)).Status);
    }

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task ACaptureRegeneratedBeforeTheJobRunsIsSkipped()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == capture)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExtractionGeneration, 2));

        await Processor(new ScriptedMapper(MappedFields)).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(RuleFields, (await CaptureAsync(capture)).FieldsJson);
        Assert.Equal("skipped", (await JobAsync(capture)).Status);
    }

    [Theory]
    [InlineData("extracting")]
    [InlineData("failed")]
    [InlineData("rejected")]
    public async Task ACaptureThatIsNoLongerReadyIsSkipped(string status)
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == capture).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, status));

        await Processor(new ScriptedMapper(MappedFields)).ProcessNextAsync(CancellationToken.None);

        Assert.Equal("skipped", (await JobAsync(capture)).Status);
    }

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task ACaptureAcceptedWhileTheMapperIsRunningIsNotOverwritten()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        var mapper = new ScriptedMapper(async () =>
        {
            // The user accepts while the mapper is still working.
            await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == capture)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, "accepted").SetProperty(c => c.AcceptedAt, DateTime.UtcNow));
            return MappedFields;
        });

        await Processor(mapper).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(1, mapper.Calls);
        Assert.Equal(RuleFields, (await CaptureAsync(capture)).FieldsJson);
        var job = await JobAsync(capture);
        Assert.Equal("skipped", job.Status);
        Assert.Equal("changed", job.Outcome);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task ARegenerationWhileTheMapperIsRunningIsNotOverwritten()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        var mapper = new ScriptedMapper(async () =>
        {
            await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == capture)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExtractionGeneration, 2).SetProperty(c => c.FieldsJson, """{"vendorName":"Second read"}"""));
            return MappedFields;
        });

        await Processor(mapper).ProcessNextAsync(CancellationToken.None);

        Assert.Equal("""{"vendorName":"Second read"}""", (await CaptureAsync(capture)).FieldsJson);
        Assert.Equal("skipped", (await JobAsync(capture)).Status);
    }

    [Fact]
    public async Task ConsentWithdrawnBeforeTheJobRunsSkipsItWithoutCallingTheMapper()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        _policy.Allowed = false;
        var mapper = new ScriptedMapper(MappedFields);

        await Processor(mapper).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(0, mapper.Calls);
        var job = await JobAsync(capture);
        Assert.Equal(("skipped", "policy"), (job.Status, job.Outcome));
    }

    // ---- AI-07: the cache never crosses workspaces ----------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-07")]
    public async Task AnIdenticalDocumentInTheSameWorkspaceReusesTheMappingWithoutCallingTheMapper()
    {
        var first = await AddReadyCaptureAsync(contentHash: "same");
        var second = await AddReadyCaptureAsync(contentHash: "same");
        await EnqueueAsync(first, contentHash: "same");
        await EnqueueAsync(second, contentHash: "same");
        var mapper = new ScriptedMapper(MappedFields) { Version = "v1" };
        var processor = Processor(mapper);

        await processor.ProcessNextAsync(CancellationToken.None);
        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(1, mapper.Calls);
        Assert.Equal("applied", (await JobAsync(first)).Outcome);
        Assert.Equal("cached", (await JobAsync(second)).Outcome);
        Assert.Equal((await CaptureAsync(first)).FieldsJson, (await CaptureAsync(second)).FieldsJson);
    }

    [Fact]
    [Trait("Rule", "AI-07")]
    public async Task TheCacheIsNeverSharedAcrossWorkspaces()
    {
        var mine = await AddReadyCaptureAsync(companyId: 1, contentHash: "same");
        var theirs = await AddReadyCaptureAsync(companyId: 2, contentHash: "same");
        await EnqueueAsync(mine, companyId: 1, contentHash: "same");
        await EnqueueAsync(theirs, companyId: 2, contentHash: "same");
        var mapper = new ScriptedMapper(MappedFields) { Version = "v1" };
        var processor = Processor(mapper);

        await processor.ProcessNextAsync(CancellationToken.None);
        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(2, mapper.Calls);
        Assert.Equal("applied", (await JobAsync(mine)).Outcome);
        Assert.Equal("applied", (await JobAsync(theirs)).Outcome);
    }

    [Fact]
    [Trait("Rule", "AI-07")]
    public async Task ADifferentMapperVersionOrRulesVersionOrKindDoesNotHitTheCache()
    {
        var first = await AddReadyCaptureAsync(contentHash: "same");
        var second = await AddReadyCaptureAsync(contentHash: "same");
        await EnqueueAsync(first, contentHash: "same");
        await EnqueueAsync(second, contentHash: "same");
        var mapperV1 = new ScriptedMapper(MappedFields) { Version = "v1" };
        var mapperV2 = new ScriptedMapper(MappedFields) { Version = "v2" };

        await Processor(mapperV1).ProcessNextAsync(CancellationToken.None);
        await Processor(mapperV2).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(1, mapperV2.Calls);
        Assert.Equal("applied", (await JobAsync(second)).Outcome);
    }

    // ---- retention: mapped values never outlive the capture's content --------------------------------------------

    private async Task AddDoneJobAsync(int captureId, int generation, string contentHash = "a")
    {
        _tenant.Set(1);
        _db.DocumentCaptureAiMappings.Add(new DocumentCaptureAiMapping
        {
            CompanyId = 1, CaptureId = captureId, ExtractionGeneration = generation, Status = "done", Attempts = 1,
            Kind = "purchase-invoice", ContentHash = contentHash.PadRight(64, 'c'), RulesVersion = "rules-3",
            MapperName = "scripted", MapperVersion = "1", Outcome = "applied", CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            MappedFieldsJson = MappedFields, RuleHintsJson = """{"vendorName":"Northstar"}""",
        });
        await _db.SaveChangesAsync();
    }

    private DocumentCaptureRetentionService Retention() => new(_db, new DocumentCaptureAdmissionLock(_db));

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task AbandonedPurgeRemovesTheMappingRowsOfEveryGenerationButNotThoseOfAcceptedCaptures()
    {
        var abandoned = await AddReadyCaptureAsync(contentHash: "one");
        var accepted = await AddReadyCaptureAsync(contentHash: "two");
        await AddDoneJobAsync(abandoned, 1, "one");
        await AddDoneJobAsync(abandoned, 2, "one");
        await AddDoneJobAsync(accepted, 1, "two");
        var now = DateTime.UtcNow;
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == abandoned)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExpiresAt, now.AddDays(-1)));
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == accepted)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, "accepted").SetProperty(c => c.AcceptedAt, now.AddDays(-30))
                .SetProperty(c => c.ExpiresAt, now.AddDays(-30)).SetProperty(c => c.FieldsJson, (string?)null).SetProperty(c => c.AcceptedFieldsJson, "{\"date\":\"2026-11-12\"}"));

        await Retention().PurgeAsync(now, CancellationToken.None);

        var remaining = await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.DoesNotContain(remaining, job => job.CaptureId == abandoned);
        Assert.Single(remaining, job => job.CaptureId == accepted);
        Assert.NotNull((await CaptureAsync(abandoned)).FieldsWipedAt);
    }

    [Fact]
    [Trait("Rule", "AI-06")]
    public async Task RejectedResiduePurgeRemovesTheMappingRowsToo()
    {
        var rejected = await AddReadyCaptureAsync();
        await AddDoneJobAsync(rejected, 1);
        var now = DateTime.UtcNow;
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == rejected)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, "rejected").SetProperty(c => c.RejectedAt, now.AddMinutes(-5))
                .SetProperty(c => c.RejectedBy, "creator-1").SetProperty(c => c.ExpiresAt, now.AddDays(5)));

        await Retention().PurgeAsync(now, CancellationToken.None);

        Assert.Empty(await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().Where(job => job.CaptureId == rejected).ToListAsync());
        Assert.NotNull((await CaptureAsync(rejected)).FieldsWipedAt);
    }

    [Fact]
    [Trait("Rule", "AI-07")]
    public async Task AWipedCapturesMappingIsNeverReusedByTheCache()
    {
        var wiped = await AddReadyCaptureAsync(contentHash: "same");
        await AddDoneJobAsync(wiped, 1, "same");
        // A mapping row that somehow survived the wipe (for example from before the wipe also removed it).
        await _db.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == wiped)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.FieldsWipedAt, DateTime.UtcNow).SetProperty(c => c.FieldsJson, "{}"));
        var later = await AddReadyCaptureAsync(contentHash: "same");
        await EnqueueAsync(later, contentHash: "same");
        var mapper = new ScriptedMapper(MappedFields) { Version = "1" };

        await Processor(mapper).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(1, mapper.Calls);
        Assert.Equal("applied", (await JobAsync(later)).Outcome);
    }

    // ---- leases, attempts and the rate cap ------------------------------------------------------------------------

    [Fact]
    public async Task AJobWithALiveLeaseIsNotTakenAndOneWithAnExpiredLeaseIsReclaimed()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, "running")
            .SetProperty(j => j.Attempts, 1).SetProperty(j => j.LeaseUntil, clock.GetUtcNow().UtcDateTime.AddSeconds(20)));
        var mapper = new ScriptedMapper(MappedFields);

        Assert.False(await Processor(mapper, clock).ProcessNextAsync(CancellationToken.None));
        Assert.Equal(0, mapper.Calls);

        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(await Processor(mapper, clock).ProcessNextAsync(CancellationToken.None));
        Assert.Equal(1, mapper.Calls);
        var job = await JobAsync(capture);
        Assert.Equal("done", job.Status);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task AJobWhoseLeaseExpiredAfterTheLastAttemptIsFailedNotRetried()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        await _db.DocumentCaptureAiMappings.IgnoreQueryFilters().ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, "running")
            .SetProperty(j => j.Attempts, DocumentCaptureAiMappingProcessor.MaxAttempts).SetProperty(j => j.LeaseUntil, DateTime.UtcNow.AddSeconds(-5)));
        var mapper = new ScriptedMapper(MappedFields);

        Assert.False(await Processor(mapper).ProcessNextAsync(CancellationToken.None));

        Assert.Equal(0, mapper.Calls);
        var job = await JobAsync(capture);
        Assert.Equal(("failed", "attempts"), (job.Status, job.Outcome));
    }

    [Fact]
    public async Task AFailingMapperIsRetriedUpToTheAttemptCapThenTheRulesStand()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var mapper = new ScriptedMapper((Func<string>)(() => throw new InvalidOperationException("secret NS-20457")));
        var processor = Processor(mapper, clock);

        for (var attempt = 1; attempt <= DocumentCaptureAiMappingProcessor.MaxAttempts; attempt++)
        {
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
            var job = await JobAsync(capture);
            Assert.Equal(attempt, job.Attempts);
            Assert.Equal(attempt < DocumentCaptureAiMappingProcessor.MaxAttempts ? "queued" : "failed", job.Status);
            clock.Advance(TimeSpan.FromMinutes(1)); // past any backoff
        }

        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(DocumentCaptureAiMappingProcessor.MaxAttempts, mapper.Calls);
        Assert.Equal(RuleFields, (await CaptureAsync(capture)).FieldsJson);
        Assert.DoesNotContain("secret", (await JobAsync(capture)).Outcome);
    }

    [Fact]
    public async Task ARequeuedJobWaitsOutItsBackoffBeforeItIsClaimedAgain()
    {
        var capture = await AddReadyCaptureAsync();
        await EnqueueAsync(capture);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var mapper = new ScriptedMapper((Func<string>)(() => throw new InvalidOperationException("outage")));
        var processor = Processor(mapper, clock);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(1, mapper.Calls);

        // An outage doesn't burn the remaining attempts within seconds.
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(1, mapper.Calls);
        Assert.Equal("queued", (await JobAsync(capture)).Status);

        // Ten seconds per attempt used: claimable again just after the first backoff.
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(2, mapper.Calls);

        // The second backoff is twice as long.
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(3, mapper.Calls);
    }

    // ---- no idle polling: the worker is woken by a signal ---------------------------------------------------------

    [Fact]
    public async Task AnEnqueueWakesTheWorkerWithoutWaitingForTheFallbackCheck()
    {
        var signal = new CaptureAiMappingSignal();
        var capture = await AddReadyCaptureAsync();

        // Nothing queued: the wait runs to the (here very short) fallback and reports it was not signalled.
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));

        _tenant.Set(1);
        Assert.True(await CaptureAiMappingQueue.TryEnqueueAsync(_db, Options.Create(_captureOptions), _policy, 1, capture, 1, "purchase-invoice",
            "a".PadRight(64, 'c'), DateTime.UtcNow, CancellationToken.None, signal));

        // A long fallback would never elapse in this test: the queued job signalled at once.
        Assert.True(await signal.WaitAsync(TimeSpan.FromMinutes(10), CancellationToken.None));
        Assert.True(await Processor(new ScriptedMapper(MappedFields)).ProcessNextAsync(CancellationToken.None));
        Assert.Equal("done", (await JobAsync(capture)).Status);
    }

    [Fact]
    public async Task SeveralEnqueuesCollapseIntoOneWakeUpAndADuplicateEnqueueDoesNotSignal()
    {
        var signal = new CaptureAiMappingSignal();
        var first = await AddReadyCaptureAsync(contentHash: "one");
        var second = await AddReadyCaptureAsync(contentHash: "two");
        _tenant.Set(1);
        await CaptureAiMappingQueue.TryEnqueueAsync(_db, Options.Create(_captureOptions), _policy, 1, first, 1, "purchase-invoice", "one".PadRight(64, 'c'),
            DateTime.UtcNow, CancellationToken.None, signal);
        await CaptureAiMappingQueue.TryEnqueueAsync(_db, Options.Create(_captureOptions), _policy, 1, second, 1, "purchase-invoice", "two".PadRight(64, 'c'),
            DateTime.UtcNow, CancellationToken.None, signal);

        Assert.True(await signal.WaitAsync(TimeSpan.FromMinutes(10), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));

        // Enqueueing the same extraction again adds no job and wakes nobody.
        await CaptureAiMappingQueue.TryEnqueueAsync(_db, Options.Create(_captureOptions), _policy, 1, first, 1, "purchase-invoice", "one".PadRight(64, 'c'),
            DateTime.UtcNow, CancellationToken.None, signal);
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));
    }

    [Fact]
    public void TheIdleWorkerChecksTheDatabaseOnlyEveryFifteenSecondsAtMost()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), DocumentCaptureAiMappingWorker.FallbackInterval);
    }

    [Fact]
    public async Task JobsOverThePerMinuteCapStayQueued()
    {
        _captureOptions.AiMapping.MaxMappingsPerMinute = 1;
        var first = await AddReadyCaptureAsync(contentHash: "one");
        var second = await AddReadyCaptureAsync(contentHash: "two");
        await EnqueueAsync(first, contentHash: "one");
        await EnqueueAsync(second, contentHash: "two");
        var mapper = new ScriptedMapper(MappedFields);
        var processor = Processor(mapper);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal(1, mapper.Calls);
        Assert.Equal("done", (await JobAsync(first)).Status);
        Assert.Equal(("queued", 0), ((await JobAsync(second)).Status, (await JobAsync(second)).Attempts));
    }

    // ---- configuration --------------------------------------------------------------------------------------------

    private static void Validate(Action<DocumentCaptureAiMappingOptions> change, string environment = "Production")
    {
        var options = new DocumentCaptureOptions();
        change(options.AiMapping);
        var env = new Moq.Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        DocumentCaptureOptions.Validate(options, env.Object);
    }

    [Fact]
    public void AiMappingIsOffByDefaultAndTheDefaultsAreValid()
    {
        var defaults = new DocumentCaptureAiMappingOptions();
        Assert.False(defaults.Enabled);
        Assert.Equal("accountgo-capture-mapper", defaults.AgentSlug);
        Assert.Equal(6, defaults.TimeoutSeconds);
        Assert.Equal(200, defaults.MaxLines);
        Assert.Equal(30, defaults.MaxMappingsPerMinute);
        Validate(_ => { });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void TheTimeoutCannotExceedTheSixSecondCutOff(int seconds) =>
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.TimeoutSeconds = seconds));

    [Fact]
    public void LimitsOutsideTheSupportedRangeAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.MaxLines = 0));
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.MaxMappingsPerMinute = 601));
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.AgentSlug = " "));
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.Mapper = "other"));
    }

    [Fact]
    public void TheFakeMapperIsRejectedOutsideDevelopmentAndTests()
    {
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.Mapper = "fake", "Production"));
        Assert.Throws<InvalidOperationException>(() => Validate(o => o.Mapper = "fake", "Staging"));
        Validate(o => o.Mapper = "fake", "Development");
        Validate(o => o.Mapper = "fake", "Testing");
    }

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class MutableTenant(int companyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private int _companyId = companyId;
        public int? CompanyId => _companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "creator-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => true;
        public int RequireCompanyId() => _companyId;
        public void Set(int value) => _companyId = value;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed class AllowPolicy : ICaptureAiMappingPolicy
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(Allowed);
    }

    private sealed class ScriptedMapper : IVersionedCaptureFieldMapper
    {
        private readonly Func<Task<string>> _fields;
        public ScriptedMapper(string fields) => _fields = () => Task.FromResult(fields);
        public ScriptedMapper(Func<string> fields) => _fields = () => Task.FromResult(fields());
        public ScriptedMapper(Func<Task<string>> fields) => _fields = fields;
        public int Calls { get; private set; }
        public string Name => "scripted";
        public string Version { get; set; } = "1";

        public async Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return new CaptureMappingResult(await _fields(), Name, Version, Array.Empty<string>());
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
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
