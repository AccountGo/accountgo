// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data.Postgres;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

/// <summary>Required PostgreSQL coverage for the mapping job's two atomic steps: the claim and the conditional apply. Skipped without a server.</summary>
public sealed class CaptureAiMappingPostgresTests
{
    private const string RuleFields = """{"date":"2026-11-12","vendorName":"Northstar"}""";
    private const string MappedFields = """{"date":"2026-11-12","vendorInvoiceNo":"NS-20457","vendorName":"Northstar Supplies Ltd"}""";

    [PostgresOnlyFact]
    [Trait("Rule", "AI-06")]
    public async Task AcceptingTheCaptureBetweenTheReadAndTheConditionalApplyLeavesItsFieldsUntouchedAndSkipsTheJob()
        => await AssertConcurrentChangeSkipsAsync(async (otherDb, captureId) =>
            await otherDb.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == captureId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, "accepted").SetProperty(c => c.AcceptedAt, DateTime.UtcNow)
                    .SetProperty(c => c.AcceptedFieldsJson, """{"vendorName":"Typed by the user"}""")));

    [PostgresOnlyFact]
    [Trait("Rule", "AI-06")]
    public async Task ARegenerationBetweenTheReadAndTheConditionalApplyLeavesItsFieldsUntouchedAndSkipsTheJob()
        => await AssertConcurrentChangeSkipsAsync(async (otherDb, captureId) =>
            await otherDb.DocumentCaptures.IgnoreQueryFilters().Where(c => c.Id == captureId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExtractionGeneration, 2)));

    [PostgresOnlyFact]
    [Trait("Rule", "AI-06")]
    public async Task TwoProcessorsRacingForOneJobClaimItExactlyOnce()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new RaceTenant(1);
        var contextOptions = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        var captureId = await SeedAsync(contextOptions, tenant);

        var settings = Options.Create(new DocumentCaptureOptions { AiMapping = new DocumentCaptureAiMappingOptions { Enabled = true } });
        var mapper = new CountingMapper(MappedFields);
        var ready = new CountdownEvent(2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = new PostgresApiDbContext(contextOptions, tenant);
            var processor = new DocumentCaptureAiMappingProcessor(db, mapper, new AllowAll(), new CaptureAiMappingRateLimiter(), settings,
                NullLogger<DocumentCaptureAiMappingProcessor>.Instance);
            ready.Signal();
            await start.Task.ConfigureAwait(false);
            return await processor.ProcessNextAsync(CancellationToken.None).ConfigureAwait(false);
        }).ToArray();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(60)), "The racers did not reach the start gate.");
        start.SetResult();
        await Task.WhenAll(racers);

        // Only the winner's conditional claim matched, so the mapper ran once and the job records one attempt.
        Assert.Equal(1, mapper.Calls);
        await using var verify = new PostgresApiDbContext(contextOptions, tenant);
        var job = await verify.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.CaptureId == captureId);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("done", job.Status);
    }

    private static async Task AssertConcurrentChangeSkipsAsync(Func<PostgresApiDbContext, int, Task> changeMeanwhile)
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new RaceTenant(1);
        var contextOptions = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        var captureId = await SeedAsync(contextOptions, tenant);

        var settings = Options.Create(new DocumentCaptureOptions { AiMapping = new DocumentCaptureAiMappingOptions { Enabled = true } });
        // The mapper is "working" when a second connection changes the capture: after the processor read it, before it applies.
        var mapper = new CountingMapper(MappedFields, async () =>
        {
            await using var other = new PostgresApiDbContext(contextOptions, tenant);
            await changeMeanwhile(other, captureId);
        });
        await using (var db = new PostgresApiDbContext(contextOptions, tenant))
        {
            var processor = new DocumentCaptureAiMappingProcessor(db, mapper, new AllowAll(), new CaptureAiMappingRateLimiter(), settings,
                NullLogger<DocumentCaptureAiMappingProcessor>.Instance);
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        }

        Assert.Equal(1, mapper.Calls);
        await using var verify = new PostgresApiDbContext(contextOptions, tenant);
        var capture = await verify.DocumentCaptures.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == captureId);
        Assert.Equal(RuleFields, capture.FieldsJson);
        var job = await verify.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.CaptureId == captureId);
        Assert.Equal(("skipped", "changed"), (job.Status, job.Outcome));
    }

    private static async Task<int> SeedAsync(DbContextOptions<PostgresApiDbContext> contextOptions, RaceTenant tenant)
    {
        await using var db = new PostgresApiDbContext(contextOptions, tenant);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
        await db.SaveChangesAsync();
        var capture = new Core.Domain.DocumentCapture
        {
            CompanyId = 1, Kind = "purchase-invoice", SchemaVersion = 1, Status = "ready", SourceHash = new string('s', 64),
            ContentHash = new string('c', 64), NormalizationVersion = "image-normalization-v2", ContentType = "image/png",
            ByteCount = 1, PageCount = 1, RequestFingerprint = new string('f', 64), ExtractionGeneration = 1,
            FieldsJson = RuleFields, WarningsJson = "[]",
            RawText = "Northstar Supplies Ltd\nInvoice No: NS-20457\nDate: 2026-11-12", ProviderName = "fake", ProviderVersion = "1",
            CreatedBy = "tester", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(24),
        };
        db.DocumentCaptures.Add(capture);
        await db.SaveChangesAsync();
        var queued = await CaptureAiMappingQueue.TryEnqueueAsync(db,
            Options.Create(new DocumentCaptureOptions { AiMapping = new DocumentCaptureAiMappingOptions { Enabled = true } }), new AllowAll(),
            1, capture.Id, 1, "purchase-invoice", capture.ContentHash, DateTime.UtcNow, CancellationToken.None);
        Assert.True(queued);
        return capture.Id;
    }

    private sealed class PostgresOnlyFactAttribute : FactAttribute
    {
        public PostgresOnlyFactAttribute()
        {
            if (!PostgresTestDatabase.IsConfigured)
                Skip = $"Set {PostgresTestDatabase.EnvironmentVariable} to run required PostgreSQL concurrency coverage.";
        }
    }

    private sealed class AllowAll : ICaptureAiMappingPolicy
    {
        public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class CountingMapper(string fields, Func<Task>? whileMapping = null) : IVersionedCaptureFieldMapper
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Name => "counting";
        public string Version => "1";

        public async Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (whileMapping is not null)
                await whileMapping().ConfigureAwait(false);
            return new CaptureMappingResult(fields, Name, Version, Array.Empty<string>());
        }
    }

    private sealed class RaceTenant(int companyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int? CompanyId => companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "creator-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => true;
        public int RequireCompanyId() => companyId;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }
}
