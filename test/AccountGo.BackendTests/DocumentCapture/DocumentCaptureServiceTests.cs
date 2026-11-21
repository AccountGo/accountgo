using Api.Data;
using Api.Controllers;
using Api.DocumentCaptures;
using Api.PublicIds;
using Api.Service;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureServiceTests
{
    [Fact]
    public async Task Upload_ReplaysByIdempotencyKeyAndAcceptsCanonicalFieldsWithoutBusinessWrites()
    {
        using var harness = new Harness(enabled: true, maxAttemptsPerHour: 1, maxAttemptsPerDay: 1);
        var source = CreatePng();
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "client-key-1", CancellationToken.None);

        Assert.Equal("ready", uploaded.Status);
        Assert.StartsWith("cap_", uploaded.Id, StringComparison.Ordinal);
        Assert.True(uploaded.BytesAvailable);
        Assert.True(uploaded.CanAccept);
        Assert.False(uploaded.CanApply);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant(), uploaded.SourceHash);
        Assert.Equal("image-normalization-v2", uploaded.NormalizationVersion);
        Assert.Equal("fake", uploaded.ProviderName);

        var replay = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "client-key-1", CancellationToken.None);
        Assert.Equal(uploaded.Id, replay.Id);
        var contentReplay = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "client-key-2", CancellationToken.None);
        Assert.Equal(uploaded.Id, contentReplay.Id);
        Assert.Equal(1, await harness.Db.DocumentCaptureAttempts.CountAsync());

        const string acceptedFields = "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}";
        var internalId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        var accepted = await harness.Service.AcceptAsync(internalId, acceptedFields, CancellationToken.None);
        Assert.Equal("accepted", accepted.Status);
        Assert.Null(accepted.Fields);
        Assert.Equal("Fixture Vendor", accepted.AcceptedFields!.Value.GetProperty("vendorName").GetString());
        var acceptedReplay = await harness.Service.AcceptAsync(internalId,
            "{\"vendorName\":\"Fixture Vendor\",\"date\":\"2026-11-20\"}", CancellationToken.None);
        Assert.Equal(accepted.AcceptedFields.Value.GetRawText(), acceptedReplay.AcceptedFields!.Value.GetRawText());
        Assert.False(acceptedReplay.CanAccept);
        Assert.True(acceptedReplay.CanApply);
        Assert.Empty(await harness.Db.PurchaseInvoiceHeaders.ToListAsync());
        Assert.Empty(await harness.Db.SalesInvoiceHeaders.ToListAsync());
        Assert.Empty(await harness.Db.Banks.ToListAsync());
    }

    [Fact]
    public async Task Upload_WhenFeatureIsDisabledDoesNotPersistAnything()
    {
        using var harness = new Harness(enabled: false);

        var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "client-key-2", CancellationToken.None));

        Assert.Equal("DOCUMENT_CAPTURE_UNCONFIGURED", exception.ErrorCode);
        Assert.Empty(await harness.Db.DocumentCaptures.ToListAsync());
        Assert.Empty(await harness.Db.DocumentCaptureAttempts.ToListAsync());
    }

    [Fact]
    public async Task Link_RequiresAcceptedCaptureAndSavedSameCompanyTargetAndIsIdempotent()
    {
        using var harness = new Harness(enabled: true);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "link-key", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None);
        var target = new PurchaseInvoiceHeader
        {
            CompanyId = 1,
            PublicId = "bill_target1",
            Date = new DateTime(2026, 11, 20),
            VendorInvoiceNo = ""
        };
        harness.Db.PurchaseInvoiceHeaders.Add(target);
        await harness.Db.SaveChangesAsync();
        harness.PublicIds.Setup(ids => ids.ResolveAsync("bill_target1", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(target.Id));

        var first = await harness.Service.LinkAsync(captureId, "bill", "bill_target1", CancellationToken.None);
        var replay = await harness.Service.LinkAsync(captureId, "bill", "bill_target1", CancellationToken.None);

        Assert.Equal(uploaded.Id, first.Id);
        Assert.Equal("bill_target1", first.Link?.TargetId);
        Assert.Equal("accepted", replay.Status);
        Assert.Single(await harness.Db.DocumentCaptureLinks.ToListAsync());
        Assert.Single(await harness.Service.GetLinkedAsync("bill", "bill_target1", CancellationToken.None));
    }

    [Fact]
    public async Task Upload_ForceBypassesContentDedupeButStillReservesAnAttempt()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var first = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "force-key-1", CancellationToken.None);

        var forced = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "force-key-2", CancellationToken.None, force: true);

        Assert.NotEqual(first.Id, forced.Id);
        Assert.Equal(first.ContentHash, forced.ContentHash);
        Assert.Equal(2, await harness.Db.DocumentCaptureAttempts.CountAsync());
    }

    [Fact]
    public async Task Upload_ReusingIdempotencyKeyWithDifferentSourceConflictsWithoutAnotherAttempt()
    {
        using var harness = new Harness(enabled: true);
        await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "conflict-key", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(variant: 4), "conflict-key", CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_CONFLICT", exception.ErrorCode);
        Assert.Equal(1, await harness.Db.DocumentCaptureAttempts.CountAsync());
    }

    [Fact]
    public async Task Upload_StoresValidPdfByteForByte()
    {
        using var harness = new Harness(enabled: true);
        var builder = new PdfDocumentBuilder();
        builder.AddPage(100, 100);
        var source = builder.Build();

        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "application/pdf", source, "pdf-key", CancellationToken.None);

        Assert.Equal("application/pdf", uploaded.ContentType);
        Assert.Equal("pdf-structure-v1", uploaded.NormalizationVersion);
        Assert.Equal(1, uploaded.PageCount);
        var stored = await harness.Db.DocumentCaptureBlobs.Select(blob => blob.DocumentBytes).SingleAsync();
        Assert.Equal(source, stored);
        var downloaded = await harness.Service.GetBytesAsync(
            await harness.Db.DocumentCaptures.Select(capture => capture.Id).SingleAsync(), CancellationToken.None);
        Assert.Equal(source, downloaded.Bytes);
    }

    [Fact]
    public async Task GetImage_ReturnsSafePrivateNoStoreAttachment()
    {
        using var harness = new Harness(enabled: true);
        var builder = new PdfDocumentBuilder();
        builder.AddPage(100, 100);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "application/pdf", builder.Build(), "attachment-key", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Select(capture => capture.Id).SingleAsync();
        var controller = new DocumentCapturesController(harness.Service, harness.Tenant, harness.Entitlement,
            harness.CaptureOptions, harness.Extractor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<FileContentResult>(await controller.GetBytes(captureId, CancellationToken.None));

        Assert.StartsWith("cap_", uploaded.Id, StringComparison.Ordinal);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("document.pdf", result.FileDownloadName);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        Assert.Contains(typeof(DocumentCapturesController).GetMethod(nameof(DocumentCapturesController.GetBytes))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Cast<HttpGetAttribute>(),
            attribute => attribute.Template == "{id}/image");
    }

    [Fact]
    public async Task Upload_WhenPdfGateIsFullReturnsBusyWithoutPersistingCaptureOrAttempt()
    {
        using var harness = new Harness(enabled: true);
        var gate = DocumentCapturePdfValidator.ParseGates.GetOrAdd(
            DocumentCapturePdfLimits.ParseGateSlots,
            slots => new SemaphoreSlim(slots, slots));
        await gate.WaitAsync();
        try
        {
            var builder = new PdfDocumentBuilder();
            builder.AddPage(100, 100);
            var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
                harness.Service.UploadAsync("purchase-invoice", 1, "application/pdf", builder.Build(), "busy-pdf", CancellationToken.None));

            Assert.Equal(429, exception.StatusCode);
            Assert.Equal("DOCUMENT_DECODE_BUSY", exception.ErrorCode);
            Assert.Empty(await harness.Db.DocumentCaptures.ToListAsync());
            Assert.Empty(await harness.Db.DocumentCaptureAttempts.ToListAsync());
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task Upload_ProviderFailureReturnsPublicCaptureIdAndPersistsOnlyStableFailureMetadata()
    {
        using var harness = new Harness(enabled: true);
        var service = harness.CreateService(new FailingExtractor());

        var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "provider-failure", CancellationToken.None));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("DOCUMENT_UNREADABLE", exception.ErrorCode);
        Assert.StartsWith("cap_", exception.CaptureId, StringComparison.Ordinal);
        var capture = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync();
        Assert.Equal("failed", capture.Status);
        Assert.Equal("DOCUMENT_UNREADABLE", capture.ErrorCode);
        Assert.Null(capture.RawText);
        Assert.Null(capture.FieldsJson);
    }

    [Fact]
    public async Task StateTransitionsAuditOnlyCaptureMetadata()
    {
        using var harness = new Harness(enabled: true);
        var events = new List<(string EventType, string Metadata)>();
        var audit = new Mock<ISecurityAuditService>();
        audit.Setup(service => service.LogEventAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<object?>()))
            .Callback<string, string?, string?, int?, object?>((eventType, _, _, _, metadata) =>
                events.Add((eventType, System.Text.Json.JsonSerializer.Serialize(metadata))))
            .Returns(Task.CompletedTask);
        var service = harness.CreateService(new FakeDocumentFieldExtractor(harness.CaptureOptions), audit.Object);
        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "audit-key", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Select(capture => capture.Id).SingleAsync();
        await service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None);

        Assert.Equal(new[] { "document.capture.reserved", "document.capture.ready", "document.capture.accepted" },
            events.Select(item => item.EventType));
        Assert.All(events, item =>
        {
            Assert.Contains(uploaded.Id, item.Metadata, StringComparison.Ordinal);
            Assert.DoesNotContain("fields", item.Metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bytes", item.Metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("021000021", item.Metadata, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Upload_ProviderRunsOutsideTransactionAndStaleGenerationCannotFinalize()
    {
        using var harness = new Harness(enabled: true);
        var extractor = new BlockingExtractor(harness.Db);
        var service = harness.CreateService(extractor);
        var upload = service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "stale-generation", CancellationToken.None);
        await extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(extractor.ObservedNoTransaction);
        var captureId = await harness.Db.DocumentCaptures.Select(capture => capture.Id).SingleAsync();
        await harness.Db.DocumentCaptures.Where(capture => capture.Id == captureId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(capture => capture.ExtractionGeneration, 2)
                .SetProperty(capture => capture.Status, "extracting")
                .SetProperty(capture => capture.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(1)));
        harness.Db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt
        {
            CompanyId = 1,
            CaptureId = captureId,
            Generation = 2,
            ReservedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        extractor.Release();
        var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => upload);

        Assert.Equal("DOCUMENT_STATE_CONFLICT", exception.ErrorCode);
        var capture = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(row => row.Id == captureId);
        Assert.Equal(2, capture.ExtractionGeneration);
        Assert.Equal("extracting", capture.Status);
        Assert.Null(capture.FieldsJson);
    }

    private static byte[] CreatePng(int variant = 0)
    {
        using var bitmap = new SKBitmap(20, 12, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor((byte)(variant + 1), 50, 90));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly TestTenantContext _tenant;
        private readonly IEntitlementService _entitlement;
        private readonly IOptions<DocumentCaptureOptions> _configuredOptions;
        public readonly ApiDbContext Db;
        public readonly DocumentCaptureService Service;
        public readonly Mock<IPublicIdResolver> PublicIds = new();
        public IOptions<DocumentCaptureOptions> CaptureOptions => _configuredOptions;
        public TestTenantContext Tenant => _tenant;
        public IEntitlementService Entitlement => _entitlement;
        public IDocumentFieldExtractor Extractor { get; }

        public Harness(bool enabled, int maxAttemptsPerHour = 20, int maxAttemptsPerDay = 60)
        {
            _connection.Open();
            _tenant = new TestTenantContext(1);
            Db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, _tenant);
            Db.Database.EnsureCreated();
            Db.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
            Db.SaveChanges();
            _tenant.Permissions = new HashSet<string>(StringComparer.Ordinal)
            {
                SystemPermissions.PurchasesBillsCreate,
                SystemPermissions.PurchasesBillsView
            };

            var snapshot = new EntitlementSnapshot(true, "invoicing", "invoicing", false, [], [], [], null,
                new HashSet<string> { FeatureKeys.DocumentCapture }, "invoicing");
            var entitlementMock = new Mock<IEntitlementService>();
            entitlementMock.Setup(service => service.Get(1)).Returns(snapshot);
            entitlementMock.Setup(service => service.Evaluate(1, It.IsAny<EntitlementAction>()))
                .Returns(new EntitlementDecision(true, null, null));
            _entitlement = entitlementMock.Object;
            _configuredOptions = Options.Create(new DocumentCaptureOptions
            {
                Enabled = enabled,
                Extractor = "Fake",
                MaxAttemptsPerHour = maxAttemptsPerHour,
                MaxAttemptsPerDay = maxAttemptsPerDay,
                FakeFixtures = new Dictionary<string, string>
                {
                    ["purchase-invoice"] = "{\"vendorName\":\"Fixture Vendor\",\"date\":\"2026-11-20\",\"lines\":[]}"
                }
            });
            Extractor = new FakeDocumentFieldExtractor(_configuredOptions);
            Service = CreateService(Extractor);
        }

        public DocumentCaptureService CreateService(IDocumentFieldExtractor extractor, ISecurityAuditService? audit = null) =>
            new(Db, new DocumentCaptureAdmissionLock(Db), extractor, _entitlement, PublicIds.Object,
                _tenant, _configuredOptions, NullLogger<DocumentCaptureService>.Instance, audit);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class TestTenantContext(int companyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int? CompanyId => companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "creator-1";
        public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();
        public bool IsResolved => true;
        public int RequireCompanyId() => companyId;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed class BlockingExtractor(ApiDbContext db) : IDocumentFieldExtractor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConfigured => true;
        public string ProviderName => "test";
        public bool ObservedNoTransaction { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();

        public async Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
        {
            ObservedNoTransaction = db.Database.CurrentTransaction is null;
            Entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new DocumentExtractionResult(
                "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}",
                request.Kind, 1, Array.Empty<string>(), null, "test", "1");
        }
    }

    private sealed class FailingExtractor : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "test";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromException<DocumentExtractionResult>(new InvalidOperationException("provider body included routing 021000021"));
    }
}
