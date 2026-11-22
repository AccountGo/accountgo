using Api.Data;
using Api.DocumentCaptures;
using Api.Controllers;
using Api.Data.Postgres;
using Api.PublicIds;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCapturePostgresConcurrencyTests
{
    private const int CompanyId = 1;

    [PostgresOnlyFact]
    public Task ConcurrentAdmissions_AtNineteenPerHour_OnlyTheTwentiethSucceeds()
        => AssertConcurrentLimitAsync(hourlySeedCount: 19, olderDailySeedCount: 0, expectedTotal: 20, contenders: 2);

    [PostgresOnlyFact]
    public Task ConcurrentAdmissions_AtFiftyNinePerDay_OnlyTheSixtiethSucceeds()
        => AssertConcurrentLimitAsync(hourlySeedCount: 19, olderDailySeedCount: 40, expectedTotal: 60, contenders: 2);

    [PostgresOnlyFact]
    public async Task ConcurrentRetries_OfOneFailedCapture_ReserveOnlyOneNewGeneration()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var contextOptions = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using (var setup = new PostgresApiDbContext(contextOptions, tenant))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
            await setup.SaveChangesAsync();
        }

        var options = Options.Create(new DocumentCaptureOptions
        {
            Enabled = true,
            Extractor = "Fake",
            FakeFixtures = new Dictionary<string, string>
            {
                ["purchase-invoice"] = "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}"
            }
        });
        var entitlement = CreateEntitlement();
        var source = CreatePng(7);
        string captureId;
        await using (var seedDb = new PostgresApiDbContext(contextOptions, tenant))
        {
            captureId = (await CreateService(seedDb, tenant, entitlement, options)
                .UploadAsync("purchase-invoice", 1, "image/png", source, "retry-key", CancellationToken.None)).Id;
        }
        await using (var expireDb = new PostgresApiDbContext(contextOptions, tenant))
        {
            var capture = await expireDb.DocumentCaptures.SingleAsync(row => row.PublicId == captureId);
            capture.Status = "failed";
            capture.LeaseExpiresAt = null;
            capture.ErrorCode = "DOCUMENT_PROVIDER_FAILED";
            await expireDb.SaveChangesAsync();
        }

        var extractor = new BlockingExtractor();
        var ready = new CountdownEvent(2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retries = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var requestDb = new PostgresApiDbContext(contextOptions, tenant);
            var service = CreateService(requestDb, tenant, entitlement, options, extractor);
            ready.Signal();
            await start.Task.ConfigureAwait(false);
            try
            {
                await service.UploadAsync("purchase-invoice", 1, "image/png", source, "retry-key", CancellationToken.None)
                    .ConfigureAwait(false);
                return "accepted";
            }
            catch (DocumentCaptureServiceException exception)
            {
                return exception.ErrorCode;
            }
        }).ToArray();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Concurrent retry requests did not reach the start gate.");
        start.SetResult();
        await extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competingResult = await Task.WhenAny(retries).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("DOCUMENT_EXTRACTION_IN_PROGRESS", await competingResult);
        Assert.Equal(1, extractor.CallCount);
        extractor.Release();
        var results = await Task.WhenAll(retries);

        Assert.Equal(1, results.Count(result => result == "accepted"));
        Assert.Equal(1, results.Count(result => result == "DOCUMENT_EXTRACTION_IN_PROGRESS"));
        await using var verify = new PostgresApiDbContext(contextOptions, tenant);
        Assert.Equal(2, await verify.DocumentCaptureAttempts.CountAsync());
        Assert.Equal(2, await verify.DocumentCaptures.Where(row => row.PublicId == captureId)
            .Select(row => row.ExtractionGeneration).SingleAsync());
    }

    [PostgresOnlyFact]
    public async Task PurgeWorkerSingletonLock_AllowsOnlyOneBatchRunner()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using var firstDb = new PostgresApiDbContext(options);
        await using var secondDb = new PostgresApiDbContext(options);
        var firstAdmission = new DocumentCaptureAdmissionLock(firstDb);
        var secondAdmission = new DocumentCaptureAdmissionLock(secondDb);

        var first = await firstAdmission.TryAcquirePurgeWorkerSingletonAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The first purge runner could not acquire its singleton lock.");
        var blocked = await secondAdmission.TryAcquirePurgeWorkerSingletonAsync(CancellationToken.None);
        Assert.Null(blocked);
        await first.DisposeAsync();
        var afterRelease = await secondAdmission.TryAcquirePurgeWorkerSingletonAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The purge runner could not acquire the released singleton lock.");
        await afterRelease.DisposeAsync();
    }

    [PostgresOnlyFact]
    public async Task HeldCompanyLock_MapsAcceptRejectAndLinkToRetryableBusyResponses()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var contextOptions = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await CreateCompanyAsync(contextOptions, tenant);
        var options = CaptureOptions();
        var entitlement = CreateEntitlement();
        await using var seedDb = new PostgresApiDbContext(contextOptions, tenant);
        var uploaded = await CreateService(seedDb, tenant, entitlement, options)
            .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(13), "busy-route", CancellationToken.None);
        var captureId = await seedDb.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();

        await using var blockerDb = new PostgresApiDbContext(contextOptions, tenant);
        await using var held = await new DocumentCaptureAdmissionLock(blockerDb).AcquireCompanyAsync(CompanyId, CancellationToken.None);
        await using var requestDb = new PostgresApiDbContext(contextOptions, tenant);
        var extractor = new FakeDocumentFieldExtractor(options);
        var service = CreateService(requestDb, tenant, entitlement, options, extractor);
        var controller = new DocumentCapturesController(service, tenant, entitlement, options, extractor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var accept = Assert.IsType<ObjectResult>(await controller.Accept(captureId,
            new DocumentCaptureAcceptRequest(JObject.Parse("{\"date\":\"2026-11-20\",\"vendorName\":\"Reviewed\"}")), CancellationToken.None));
        Assert.Equal(503, accept.StatusCode);
        Assert.Equal("1", controller.Response.Headers.RetryAfter);
        var reject = Assert.IsType<ObjectResult>(await controller.Reject(captureId, CancellationToken.None));
        Assert.Equal(503, reject.StatusCode);
        var link = Assert.IsType<ObjectResult>(await controller.Link(captureId,
            new DocumentCaptureLinkRequest("bill", "bill_unknown"), CancellationToken.None));
        Assert.Equal(503, link.StatusCode);
    }

    [PostgresOnlyFact]
    public async Task CompanyDelete_DuringExtraction_PreventsLateCompletionAndPurgesCapture()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await CreateCompanyAsync(options, tenant);
        var captureOptions = CaptureOptions();
        var blocking = new BlockingExtractor();
        var upload = Task.Run(async () =>
        {
            await using var db = new PostgresApiDbContext(options, tenant);
            return await CreateService(db, tenant, CreateEntitlement(), captureOptions, blocking)
                .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(1), "delete-extraction", CancellationToken.None);
        });
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await DeleteCompanyAndPurgeAsync(options, tenant);
        blocking.Release();
        var failure = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => upload);

        Assert.Equal("DOCUMENT_STATE_CONFLICT", failure.ErrorCode);
        await using var verify = new PostgresApiDbContext(options, tenant);
        Assert.Empty(await verify.DocumentCaptures.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.DocumentCaptureBlobs.IgnoreQueryFilters().ToListAsync());
    }

    [PostgresOnlyFact]
    public async Task CompanyDelete_RacingAccept_LeavesNoCaptureContent()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await CreateCompanyAsync(options, tenant);
        var captureOptions = CaptureOptions();
        string publicId;
        int captureId;
        await using (var db = new PostgresApiDbContext(options, tenant))
        {
            publicId = (await CreateService(db, tenant, CreateEntitlement(), captureOptions)
                .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(2), "delete-accept", CancellationToken.None)).Id;
            captureId = await db.DocumentCaptures.Where(item => item.PublicId == publicId).Select(item => item.Id).SingleAsync();
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accept = Task.Run(async () =>
        {
            await gate.Task.ConfigureAwait(false);
            await using var db = new PostgresApiDbContext(options, tenant);
            try
            {
                await CreateService(db, tenant, CreateEntitlement(), captureOptions)
                    .AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None);
                return "accepted";
            }
            catch (DocumentCaptureServiceException exception)
            {
                return exception.ErrorCode;
            }
        });
        var delete = Task.Run(async () =>
        {
            await gate.Task.ConfigureAwait(false);
            await DeleteCompanyAndPurgeAsync(options, tenant);
        });
        gate.SetResult();
        await Task.WhenAll(accept, delete);

        Assert.Contains(await accept, new[] { "accepted", "DOCUMENT_NOT_FOUND" });
        await using var verify = new PostgresApiDbContext(options, tenant);
        Assert.Empty(await verify.DocumentCaptures.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.DocumentCaptureBlobs.IgnoreQueryFilters().ToListAsync());
    }

    [PostgresOnlyFact]
    public async Task CompanyDelete_RacingEvidenceLink_LeavesNoCaptureOrLink()
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await CreateCompanyAsync(options, tenant);
        var captureOptions = CaptureOptions();
        string publicId;
        int captureId;
        await using (var db = new PostgresApiDbContext(options, tenant))
        {
            publicId = (await CreateService(db, tenant, CreateEntitlement(), captureOptions)
                .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(3), "delete-link", CancellationToken.None)).Id;
            captureId = await db.DocumentCaptures.Where(item => item.PublicId == publicId).Select(item => item.Id).SingleAsync();
            await CreateService(db, tenant, CreateEntitlement(), captureOptions)
                .AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None);
            var target = new PurchaseInvoiceHeader
            {
                CompanyId = CompanyId,
                PublicId = "bill_delete01",
                Date = new DateTime(2026, 11, 20),
                VendorInvoiceNo = string.Empty
            };
            db.PurchaseInvoiceHeaders.Add(target);
            await db.SaveChangesAsync();
        }

        var publicIds = new Mock<IPublicIdResolver>();
        publicIds.Setup(resolver => resolver.ResolveAsync("bill_delete01", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var link = Task.Run(async () =>
        {
            await gate.Task.ConfigureAwait(false);
            await using var db = new PostgresApiDbContext(options, tenant);
            try
            {
                await CreateService(db, tenant, CreateEntitlement(), captureOptions, publicIds: publicIds.Object)
                    .LinkAsync(captureId, "bill", "bill_delete01", CancellationToken.None);
                return "linked";
            }
            catch (DocumentCaptureServiceException exception)
            {
                return exception.ErrorCode;
            }
        });
        var delete = Task.Run(async () =>
        {
            await gate.Task.ConfigureAwait(false);
            await DeleteCompanyAndPurgeAsync(options, tenant);
        });
        gate.SetResult();
        await Task.WhenAll(link, delete);

        Assert.Contains(await link, new[] { "linked", "DOCUMENT_NOT_FOUND" });
        await using var verify = new PostgresApiDbContext(options, tenant);
        Assert.Empty(await verify.DocumentCaptures.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.DocumentCaptureLinks.IgnoreQueryFilters().ToListAsync());
    }

    private static async Task AssertConcurrentLimitAsync(int hourlySeedCount, int olderDailySeedCount, int expectedTotal, int contenders)
    {
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var tenant = new TestTenantContext(CompanyId);
        var contextOptions = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using (var setup = new PostgresApiDbContext(contextOptions, tenant))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
            await setup.SaveChangesAsync();
        }

        var options = Options.Create(new DocumentCaptureOptions
        {
            Enabled = true,
            Extractor = "Fake",
            FakeFixtures = new Dictionary<string, string>
            {
                ["purchase-invoice"] = "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}"
            }
        });
        var entitlement = CreateEntitlement();
        string seedId;
        await using (var firstDb = new PostgresApiDbContext(contextOptions, tenant))
        {
            seedId = (await CreateService(firstDb, tenant, entitlement, options)
                .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(0), "seed", CancellationToken.None)).Id;
        }
        await using (var seedContext = new PostgresApiDbContext(contextOptions, tenant))
        {
            var captureId = await seedContext.DocumentCaptures.Where(capture => capture.PublicId == seedId)
                .Select(capture => capture.Id).SingleAsync();
            var now = DateTime.UtcNow;
            for (var i = 0; i < hourlySeedCount - 1; i++)
                seedContext.DocumentCaptureAttempts.Add(NewAttempt(captureId, i + 2, now.AddMinutes(-10 - i)));
            for (var i = 0; i < olderDailySeedCount; i++)
                seedContext.DocumentCaptureAttempts.Add(NewAttempt(captureId, hourlySeedCount + i + 1, now.AddHours(-2).AddMinutes(-i)));
            await seedContext.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new CountdownEvent(contenders);
        var calls = Enumerable.Range(1, contenders).Select(index => Task.Run(async () =>
        {
            await using var requestDb = new PostgresApiDbContext(contextOptions, tenant);
            var service = CreateService(requestDb, tenant, entitlement, options);
            ready.Signal();
            await startGate.Task.ConfigureAwait(false);
            try
            {
                await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(index),
                    $"concurrent-{index}", CancellationToken.None).ConfigureAwait(false);
                return "accepted";
            }
            catch (DocumentCaptureServiceException exception)
            {
                return exception.ErrorCode;
            }
        })).ToArray();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Competing PostgreSQL requests did not reach the start gate.");
        startGate.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.Equal(1, results.Count(result => result == "accepted"));
        Assert.Equal(contenders - 1, results.Count(result => result == "DOCUMENT_RATE_LIMITED"));
        await using var verify = new PostgresApiDbContext(contextOptions, tenant);
        Assert.Equal(expectedTotal, await verify.DocumentCaptureAttempts.CountAsync());
        Assert.Equal(2, await verify.DocumentCaptures.CountAsync());
        Assert.Equal(2, await verify.DocumentCaptureBlobs.CountAsync());
    }

    private static DocumentCaptureAttempt NewAttempt(int captureId, int generation, DateTime reservedAt) => new()
    {
        CompanyId = CompanyId,
        CaptureId = captureId,
        Generation = generation,
        ReservedAt = reservedAt
    };

    private static DocumentCaptureService CreateService(
        ApiDbContext db,
        TestTenantContext tenant,
        IEntitlementService entitlement,
        IOptions<DocumentCaptureOptions> options,
        IDocumentFieldExtractor? extractor = null,
        IPublicIdResolver? publicIds = null)
    {
        return new DocumentCaptureService(db, new DocumentCaptureAdmissionLock(db),
            extractor ?? new FakeDocumentFieldExtractor(options), entitlement, publicIds ?? Mock.Of<IPublicIdResolver>(), tenant,
            options, NullLogger<DocumentCaptureService>.Instance);
    }

    private static IOptions<DocumentCaptureOptions> CaptureOptions() => Options.Create(new DocumentCaptureOptions
    {
        Enabled = true,
        Extractor = "Fake",
        FakeFixtures = new Dictionary<string, string>
        {
            ["purchase-invoice"] = "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}"
        }
    });

    private static async Task CreateCompanyAsync(DbContextOptions<PostgresApiDbContext> options, TestTenantContext tenant)
    {
        await using var db = new PostgresApiDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();
        db.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
        await db.SaveChangesAsync();
    }

    private static async Task DeleteCompanyAndPurgeAsync(DbContextOptions<PostgresApiDbContext> options, TestTenantContext tenant)
    {
        await using var db = new PostgresApiDbContext(options, tenant);
        await using (var transaction = await new DocumentCaptureAdmissionLock(db).AcquireCompanyAsync(CompanyId, CancellationToken.None))
        {
            var company = await db.Companies.IgnoreQueryFilters().SingleAsync(item => item.Id == CompanyId);
            company.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await new DocumentCaptureRetentionService(db, new DocumentCaptureAdmissionLock(db))
            .PurgeDeletedCompanyAsync(CompanyId, CancellationToken.None);
    }

    private sealed class BlockingExtractor : IDocumentFieldExtractor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public bool IsConfigured => true;
        public string ProviderName => "test";
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();

        public async Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new DocumentExtractionResult(
                "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}",
                request.Kind, 1, Array.Empty<string>(), null, "test", "1");
        }
    }

    private static IEntitlementService CreateEntitlement()
    {
        var snapshot = new EntitlementSnapshot(true, "invoicing", "invoicing", false, [], [], [], null,
            new HashSet<string> { FeatureKeys.DocumentCapture }, "invoicing");
        var mock = new Mock<IEntitlementService>();
        mock.Setup(service => service.Get(CompanyId)).Returns(snapshot);
        mock.Setup(service => service.Evaluate(CompanyId, It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(true, null, null));
        return mock.Object;
    }

    private static byte[] CreatePng(int variant)
    {
        using var bitmap = new SKBitmap(20, 12, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor((byte)(10 + variant), 50, 90));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class TestTenantContext(int companyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int? CompanyId => companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "creator-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            SystemPermissions.PurchasesBillsCreate,
            SystemPermissions.PurchasesBillsView
        };
        public bool IsResolved => true;
        public int RequireCompanyId() => companyId;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed class PostgresOnlyFactAttribute : FactAttribute
    {
        public PostgresOnlyFactAttribute()
        {
            if (!PostgresTestDatabase.IsConfigured)
                Skip = $"Set {PostgresTestDatabase.EnvironmentVariable} to run required PostgreSQL concurrency coverage.";
        }
    }
}
