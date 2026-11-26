using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentCaptureServiceTests
{
    [Fact]
    public async Task UploadBodyReaderEnforcesLimitAgainstActualStreamLength()
    {
        var readBounded = typeof(DocumentCapturesController).GetMethod(
            "ReadBoundedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var limit = DocumentCaptureImageLimits.MaxInputBytes;

        foreach (var reportedLength in new[] { 1L, (long)limit - 1 })
        {
            var stream = new CountingOversizeStream(limit + 1);
            var file = new OversizedFormFile(stream, reportedLength);
            var task = (Task<byte[]>)readBounded.Invoke(null, [file, CancellationToken.None])!;
            var exception = await Assert.ThrowsAsync<DocumentCaptureServiceException>(async () => await task);
            Assert.Equal(413, exception.StatusCode);
            Assert.Equal("DOCUMENT_TOO_LARGE", exception.ErrorCode);
            Assert.InRange(stream.BytesRead, limit + 1, limit + 64 * 1024);
        }
    }

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
        Assert.Equal("Fixture Vendor", accepted.AcceptedFields!["vendorName"]!.ToObject<string>());
        var acceptedReplay = await harness.Service.AcceptAsync(internalId,
            "{\"vendorName\":\"Fixture Vendor\",\"date\":\"2026-11-20\"}", CancellationToken.None);
        Assert.Equal(accepted.AcceptedFields.ToString(Newtonsoft.Json.Formatting.None),
            acceptedReplay.AcceptedFields!.ToString(Newtonsoft.Json.Formatting.None));
        Assert.False(acceptedReplay.CanAccept);
        Assert.True(acceptedReplay.CanApply);
        Assert.Empty(await harness.Db.PurchaseInvoiceHeaders.ToListAsync());
        Assert.Empty(await harness.Db.SalesInvoiceHeaders.ToListAsync());
        Assert.Empty(await harness.Db.Banks.ToListAsync());
    }

    private sealed class AllowAiMappingPolicy : Api.DocumentCaptures.Mapping.ICaptureAiMappingPolicy
    {
        public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    [Fact]
    public async Task Upload_QueuesNoAiMappingUnlessEnabledAndAllowed_AndTheViewHasNoAiMappingBlock()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();

        // Enabled in configuration but the default policy allows nothing.
        harness.CaptureOptions.Value.AiMapping.Enabled = true;
        var denied = await harness.CreateService(harness.Extractor, aiMappingPolicy: new Api.DocumentCaptures.Mapping.DisabledCaptureAiMappingPolicy())
            .UploadAsync("purchase-invoice", 1, "image/png", source, "ai-denied", CancellationToken.None);
        Assert.Equal("ready", denied.Status);
        Assert.Null(denied.AiMapping);
        Assert.Empty(await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());

        // Allowed by policy but switched off in configuration.
        harness.CaptureOptions.Value.AiMapping.Enabled = false;
        var off = await harness.CreateService(harness.Extractor, aiMappingPolicy: new AllowAiMappingPolicy())
            .UploadAsync("sales-invoice", 1, "image/png", CreatePng(), "ai-off", CancellationToken.None);
        Assert.Null(off.AiMapping);
        Assert.Empty(await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Upload_QueuesAnAiMappingJobWithoutDelayingTheRuleResult_AndTheViewShowsPendingThenDoneWithRuleHints()
    {
        using var harness = new Harness(enabled: true);
        harness.CaptureOptions.Value.AiMapping.Enabled = true;
        var service = harness.CreateService(harness.Extractor, aiMappingPolicy: new AllowAiMappingPolicy());

        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "ai-on", CancellationToken.None);

        // The rule result is returned immediately, with the job still pending.
        Assert.Equal("ready", uploaded.Status);
        Assert.Equal("Fixture Vendor", uploaded.Fields!["vendorName"]!.ToObject<string>());
        Assert.Equal("pending", uploaded.AiMapping is null ? await PendingStatusAsync(harness, service, uploaded.Id) : uploaded.AiMapping.Status);
        var job = await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal("queued", job.Status);
        Assert.Equal("purchase-invoice", job.Kind);

        await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ExecuteUpdateAsync(set => set
            .SetProperty(j => j.Status, "done").SetProperty(j => j.RuleHintsJson, "{\"vendorName\":\"Fixture\"}"));
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        var done = await service.GetAsync(internalId, CancellationToken.None);
        Assert.Equal("done", done.AiMapping!.Status);
        Assert.Equal("Fixture", done.AiMapping.RuleHints["vendorName"]);

        await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ExecuteUpdateAsync(set => set.SetProperty(j => j.Status, "failed"));
        Assert.Equal("failed", (await service.GetAsync(internalId, CancellationToken.None)).AiMapping!.Status);
    }

    [Fact]
    public async Task Reject_DeletesTheAiMappingRowsThatHoldTheSameDocumentValues()
    {
        using var harness = new Harness(enabled: true);
        harness.CaptureOptions.Value.AiMapping.Enabled = true;
        var service = harness.CreateService(harness.Extractor, aiMappingPolicy: new AllowAiMappingPolicy());
        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "ai-reject", CancellationToken.None);
        Assert.Single(await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();

        await service.RejectAsync(internalId, CancellationToken.None);

        Assert.Empty(await harness.Db.DocumentCaptureAiMappings.IgnoreQueryFilters().ToListAsync());
    }

    private static async Task<string?> PendingStatusAsync(Harness harness, DocumentCaptureService service, string publicId)
    {
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == publicId).Select(c => c.Id).SingleAsync();
        return (await service.GetAsync(internalId, CancellationToken.None)).AiMapping?.Status;
    }

    [Theory]
    [InlineData("expense-receipt", "{\"vendorName\":\"Corner Cafe\",\"date\":\"2026-11-20\"}", "{}")]
    [InlineData("purchase-invoice", "{\"vendorName\":\"Acme Ltd\",\"date\":\"2026-11-20\"}", "{}")]
    [InlineData("sales-invoice", "{\"customerName\":\"Blue Harbor LLC\",\"date\":\"2026-11-20\"}", "{}")]
    [InlineData("bank", "{\"bankName\":\"First Test Bank\",\"name\":\"Acme Ops\"}", "{}")]
    [InlineData("assistant-attachment", "{\"fields\":[{\"label\":\"Reference\",\"value\":\"INV-42\"}]}", "{\"fields\":[]}")]
    public async Task Reject_DeletesBytesAndClearsExtractedContentForEveryKind_IsIdempotent_AndSkipsDeduplication(
        string kind, string extractedJson, string expectedEmptyJson)
    {
        using var harness = new Harness(enabled: true);
        var extractor = new WarningStaticExtractor(extractedJson);
        var service = harness.CreateService(extractor);
        var source = CreatePng();

        var uploaded = await service.UploadAsync(kind, 1, "image/png", source, $"reject-{kind}", CancellationToken.None);
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id)
            .Select(c => c.Id).SingleAsync();

        var rejected = await service.RejectAsync(internalId, CancellationToken.None);
        Assert.Equal("rejected", rejected.Status);
        Assert.False(rejected.BytesAvailable);
        Assert.Null(rejected.Warnings);
        Assert.Equal(expectedEmptyJson, rejected.Fields!.ToString(Newtonsoft.Json.Formatting.None));
        Assert.NotNull(rejected.RejectedAt);

        var stored = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == internalId);
        Assert.Equal("rejected", stored.Status);
        Assert.Equal(uploaded.Id, stored.PublicId);
        Assert.Equal(kind, stored.Kind);
        Assert.Equal(uploaded.SourceHash, stored.SourceHash);
        Assert.Equal(uploaded.ContentHash, stored.ContentHash);
        Assert.Equal(1, stored.ExtractionGeneration);
        Assert.NotNull(stored.RejectedBy);
        Assert.NotNull(stored.RejectedAt);
        Assert.NotNull(stored.BytesDeletedAt);
        Assert.NotNull(stored.FieldsWipedAt);
        Assert.Equal(expectedEmptyJson, stored.FieldsJson);
        Assert.Null(stored.RawText);
        Assert.Equal(0, stored.TextPageCount);
        Assert.Null(stored.WarningsJson);
        Assert.False(await harness.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == internalId));

        // Image endpoint throws existing bytes-unavailable error.
        var bytesEx = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            service.GetBytesAsync(internalId, CancellationToken.None));
        Assert.Equal(404, bytesEx.StatusCode);
        Assert.Equal("DOCUMENT_BYTES_UNAVAILABLE", bytesEx.ErrorCode);

        // Reject is idempotent.
        var rejectedAgain = await service.RejectAsync(internalId, CancellationToken.None);
        Assert.Equal("rejected", rejectedAgain.Status);
        Assert.False(rejectedAgain.BytesAvailable);

        // Deduplication never returns a rejected capture.
        var reuploaded = await service.UploadAsync(kind, 1, "image/png", source, $"reupload-{kind}", CancellationToken.None);
        Assert.NotEqual(uploaded.Id, reuploaded.Id);
        Assert.Equal("ready", reuploaded.Status);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(6)]
    public async Task AbandonedPurge_HonoursConfiguredRetentionHours_WipesContent_AndPreventsDuplicateReplay(int retentionHours)
    {
        var start = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(start);
        using var harness = new Harness(enabled: true, abandonedRetentionHours: retentionHours);
        var service = harness.CreateService(new WarningStaticExtractor("{\"vendorName\":\"Acme Ltd\",\"date\":\"2026-11-20\"}"), timeProvider: clock);
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db), clock);
        var source = CreatePng();

        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", source, "abandoned-key", CancellationToken.None);
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id)
            .Select(c => c.Id).SingleAsync();

        // 1 hour before expiry: still intact.
        clock.SetUtcNow(start.AddHours(retentionHours - 1));
        await retention.PurgeAsync(CancellationToken.None);
        Assert.True(await harness.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == internalId));
        var beforeExpiry = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == internalId);
        Assert.Null(beforeExpiry.BytesDeletedAt);
        Assert.Null(beforeExpiry.FieldsWipedAt);

        // At expiry: bytes deleted and extracted content wiped.
        clock.SetUtcNow(start.AddHours(retentionHours));
        await retention.PurgeAsync(CancellationToken.None);
        Assert.False(await harness.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == internalId));
        var afterExpiry = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == internalId);
        Assert.NotNull(afterExpiry.BytesDeletedAt);
        Assert.NotNull(afterExpiry.FieldsWipedAt);
        Assert.Equal("{}", afterExpiry.FieldsJson);
        Assert.Null(afterExpiry.RawText);
        Assert.Equal(0, afterExpiry.TextPageCount);
        Assert.Null(afterExpiry.WarningsJson);

        // Deduplication never returns a purged capture.
        var reuploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", source, "abandoned-reupload", CancellationToken.None);
        Assert.NotEqual(uploaded.Id, reuploaded.Id);
        Assert.Equal("ready", reuploaded.Status);
    }

    [Fact]
    public void OptionsValidate_EnforcesAbandonedRetentionHoursRange()
    {
        var dev = new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        dev.SetupGet(e => e.EnvironmentName).Returns("Development");

        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { AbandonedRetentionHours = 0 }, dev.Object));
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { AbandonedRetentionHours = 169 }, dev.Object));

        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { AbandonedRetentionHours = 1 }, dev.Object);
        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { AbandonedRetentionHours = 24 }, dev.Object);
        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { AbandonedRetentionHours = 168 }, dev.Object);
    }

    [Fact]
    [Trait("Rule", "AI-08")]
    public async Task Upload_StoresTheTextPagesReadAndRejectClearsThem()
    {
        using var harness = new Harness(enabled: true);
        var service = harness.CreateService(new WarningStaticExtractor("{\"vendorName\":\"Acme Ltd\"}", textPages: 6));

        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "text-pages", CancellationToken.None);
        var internalId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        Assert.Equal(6, (await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == internalId)).TextPageCount);

        await service.RejectAsync(internalId, CancellationToken.None);
        Assert.Equal(0, (await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == internalId)).TextPageCount);
    }

    [Fact]
    [Trait("Rule", "AI-08")]
    public async Task Upload_AnExtractorThatReportsNoPagesStillMetersOnePageForTheText()
    {
        using var harness = new Harness(enabled: true);
        var service = harness.CreateService(new WarningStaticExtractor("{\"vendorName\":\"Acme Ltd\"}"));

        var uploaded = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "text-pages-min", CancellationToken.None);

        Assert.Equal(1, (await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.PublicId == uploaded.Id)).TextPageCount);
    }

    private sealed class WarningStaticExtractor(string fieldsJson, int textPages = 0) : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "fixture";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentExtractionResult(fieldsJson, request.Kind, 1, ["TOTAL_UNLABELED"],
                "sensitive raw text", "fixture", "1", textPages));
    }

    [Fact]
    public async Task Upload_TerminalReplayDoesNotNeedAnImageDecodeSlot()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var first = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "terminal-replay", CancellationToken.None);
        var gate = DocumentCaptureImageNormalizer.DecodeGates.GetOrAdd(DocumentCaptureImageLimits.DecodeGateSlots,
            slots => new SemaphoreSlim(slots, slots));
        for (var i = 0; i < DocumentCaptureImageLimits.DecodeGateSlots; i++) await gate.WaitAsync();
        try
        {
            var replay = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "terminal-replay", CancellationToken.None);
            Assert.Equal(first.Id, replay.Id);
        }
        finally
        {
            for (var i = 0; i < DocumentCaptureImageLimits.DecodeGateSlots; i++) gate.Release();
        }
    }

    [Fact]
    public async Task Upload_OmittedIdempotencyKeyUsesHashDedupe()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var first = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, null, CancellationToken.None);
        var replay = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, null, CancellationToken.None);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(await harness.Db.DocumentCaptureAttempts.ToListAsync());
    }

    [Theory]
    [InlineData("original")]
    [InlineData("new-key")]
    public async Task Upload_LocalOcrReplayCandidateBecomesFailedBeforeAdmissionReturnsBusyWithoutWrites(string key)
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "original", CancellationToken.None);
        var realLock = new DocumentCaptureAdmissionLock(harness.Db);
        var admissionLock = new Mock<IDocumentCaptureAdmissionLock>();
        admissionLock.Setup(value => value.AcquireCompanyAsync(harness.Tenant.RequireCompanyId(), It.IsAny<CancellationToken>()))
            .Returns(async (int companyId, CancellationToken token) =>
            {
                await harness.Db.DocumentCaptures.Where(capture => capture.IdempotencyKey == "original")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(capture => capture.Status, "failed"), token);
                return await realLock.AcquireCompanyAsync(companyId, token);
            });
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(new DocumentCaptureOptions()));
        using var occupied = await extractor.AdmitAsync(1, CancellationToken.None);
        var service = harness.CreateService(extractor, admissionLock: admissionLock.Object);

        var error = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            service.UploadAsync("purchase-invoice", 1, "image/png", source, key, CancellationToken.None));

        Assert.Equal((429, "DOCUMENT_DECODE_BUSY"), (error.StatusCode, error.ErrorCode));
        var capture = Assert.Single(await harness.Db.DocumentCaptures.AsNoTracking().ToListAsync());
        Assert.Equal("failed", capture.Status);
        Assert.Equal(1, capture.ExtractionGeneration);
        Assert.Equal(1, await harness.Db.DocumentCaptureAttempts.CountAsync());
        Assert.Equal(1, await harness.Db.DocumentCaptureBlobs.CountAsync());
    }

    [Fact]
    public async Task BankReadsRequireSensitiveManagePermissionEvenWhenViewIsGranted()
    {
        using var harness = new Harness(enabled: true);
        harness.Tenant.Permissions = new HashSet<string> { SystemPermissions.BankingAccountsManage, SystemPermissions.BankingAccountsView };
        var uploaded = await harness.Service.UploadAsync("bank", 1, "image/png", CreatePng(), "bank-sensitive", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        harness.Tenant.Permissions = new HashSet<string> { SystemPermissions.BankingAccountsView };

        var metadata = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => harness.Service.GetAsync(captureId, CancellationToken.None));
        var bytes = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => harness.Service.GetBytesAsync(captureId, CancellationToken.None));
        var dedupe = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.UploadAsync("bank", 1, "image/png", CreatePng(), "bank-view-only-dedupe", CancellationToken.None));

        Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", metadata.ErrorCode);
        Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", bytes.ErrorCode);
        Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", dedupe.ErrorCode);
    }

    [Fact]
    public async Task BankReadsSucceedWithManagePermissionWithoutViewPermission()
    {
        using var harness = new Harness(enabled: true);
        harness.Tenant.Permissions = new HashSet<string> { SystemPermissions.BankingAccountsManage, SystemPermissions.BankingAccountsView };
        var uploaded = await harness.Service.UploadAsync("bank", 1, "image/png", CreatePng(), "bank-manage-only", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        harness.Tenant.Permissions = new HashSet<string> { SystemPermissions.BankingAccountsManage };

        var metadata = await harness.Service.GetAsync(captureId, CancellationToken.None);
        var bytes = await harness.Service.GetBytesAsync(captureId, CancellationToken.None);

        Assert.Equal(uploaded.Id, metadata.Id);
        Assert.NotEmpty(bytes.Bytes);
    }

    [Fact]
    public async Task ExistingCaptureCanBeAcceptedWithoutFeatureGrantButNewUploadCannot()
    {
        using var harness = new Harness(enabled: true);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "accept-after-downgrade", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        harness.EntitlementMock.Setup(service => service.Get(1)).Returns(new EntitlementSnapshot(true, "invoicing", "invoicing",
            false, [], [], [], null, new HashSet<string>(), "invoicing"));

        var accepted = await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Reviewed\"}", CancellationToken.None);
        var uploadDenied = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(3), "new-after-downgrade", CancellationToken.None));

        Assert.Equal("accepted", accepted.Status);
        Assert.Equal("ENTITLEMENT_DENIED", uploadDenied.ErrorCode);
    }

    [Fact]
    public async Task AssistantCaptureIsCreatorScopedAcrossReadsActionsKeyReplayAndHashDedupe()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var ownerUpload = await harness.Service.UploadAsync("assistant-attachment", 1, "image/png", source, "assistant-owner-key", CancellationToken.None);
        var ownerCaptureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == ownerUpload.Id)
            .Select(capture => capture.Id).SingleAsync();
        await harness.Service.AcceptAsync(ownerCaptureId, "{\"fields\":[{\"label\":\"Total\",\"value\":\"12\"}]}", CancellationToken.None);
        harness.Tenant.UserId = "creator-2";

        var get = await Assert.ThrowsAsync<PublicIdSerializationException>(() => harness.Service.GetAsync(ownerCaptureId, CancellationToken.None));
        var bytes = await Assert.ThrowsAsync<PublicIdSerializationException>(() => harness.Service.GetBytesAsync(ownerCaptureId, CancellationToken.None));
        var accept = await Assert.ThrowsAsync<PublicIdSerializationException>(() =>
            harness.Service.AcceptAsync(ownerCaptureId, "{\"fields\":[]}", CancellationToken.None));
        var reject = await Assert.ThrowsAsync<PublicIdSerializationException>(() => harness.Service.RejectAsync(ownerCaptureId, CancellationToken.None));
        var link = await Assert.ThrowsAsync<PublicIdSerializationException>(() =>
            harness.Service.LinkAsync(ownerCaptureId, "bill", "bill_missing", CancellationToken.None));
        var sameKeyAndPhoto = await harness.Service.UploadAsync("assistant-attachment", 1, "image/png", source, "assistant-owner-key", CancellationToken.None);
        var samePhoto = await harness.Service.UploadAsync("assistant-attachment", 1, "image/png", source, "assistant-other-key", CancellationToken.None);

        Assert.Equal(PublicIdErrorCodes.UnknownId, get.Code);
        Assert.Equal(PublicIdErrorCodes.UnknownId, bytes.Code);
        Assert.Equal(PublicIdErrorCodes.UnknownId, accept.Code);
        Assert.Equal(PublicIdErrorCodes.UnknownId, reject.Code);
        Assert.Equal(PublicIdErrorCodes.UnknownId, link.Code);
        Assert.NotEqual(ownerUpload.Id, sameKeyAndPhoto.Id);
        Assert.Equal(sameKeyAndPhoto.Id, samePhoto.Id);
        Assert.Equal(2, await harness.Db.DocumentCaptures.CountAsync());
        Assert.Equal("creator-2", (await harness.Db.DocumentCaptures.SingleAsync(capture => capture.PublicId == sameKeyAndPhoto.Id)).CreatedBy);
    }

    [Fact]
    public async Task ReadyCaptureWithoutBytesCannotBeAcceptedOrReturnedAsDedupe()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "ready-without-bytes", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        await harness.Db.DocumentCaptureBlobs.Where(blob => blob.CaptureId == captureId).ExecuteDeleteAsync();

        var view = await harness.Service.GetAsync(captureId, CancellationToken.None);
        var accept = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None));
        var replay = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "ready-without-bytes-retry", CancellationToken.None);

        Assert.False(view.BytesAvailable);
        Assert.False(view.CanAccept);
        Assert.False(view.CanApply);
        Assert.Equal("DOCUMENT_BYTES_UNAVAILABLE", accept.ErrorCode);
        Assert.NotEqual(uploaded.Id, replay.Id);
    }

    [Fact]
    public async Task AcceptedCaptureWithoutBytesCannotBeLinked()
    {
        using var harness = new Harness(enabled: true);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "accepted-without-bytes", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id)
            .Select(capture => capture.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\"}", CancellationToken.None);
        await harness.Db.DocumentCaptureBlobs.Where(blob => blob.CaptureId == captureId).ExecuteDeleteAsync();

        var link = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.LinkAsync(captureId, "bill", "bill_missing", CancellationToken.None));

        Assert.Equal("DOCUMENT_BYTES_UNAVAILABLE", link.ErrorCode);
    }

    [Fact]
    public async Task RejectChecksWritePermissionBeforeReturningAlreadyRejectedState()
    {
        using var harness = new Harness(enabled: true);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "reject-auth-order", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id).Select(capture => capture.Id).SingleAsync();
        await harness.Service.RejectAsync(captureId, CancellationToken.None);
        harness.Tenant.Permissions = new HashSet<string> { SystemPermissions.PurchasesBillsView };

        var denied = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => harness.Service.RejectAsync(captureId, CancellationToken.None));

        Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", denied.ErrorCode);
    }

    [Fact]
    public async Task Upload_DedupeSkipsCaptureWhoseLinkedTargetIsNoLongerReadable()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng(31);
        var original = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "linked-denied-original", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == original.Id).Select(capture => capture.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Linked\"}", CancellationToken.None);
        var target = new PurchaseInvoiceHeader { CompanyId = 1, PublicId = "bill_unreadable", Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "HIDDEN" };
        harness.Db.PurchaseInvoiceHeaders.Add(target);
        await harness.Db.SaveChangesAsync();
        harness.PublicIds.Setup(ids => ids.ResolveAsync("bill_unreadable", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(target.Id));
        await harness.Service.LinkAsync(captureId, "bill", "bill_unreadable", CancellationToken.None);
        await harness.Db.PurchaseInvoiceHeaders.Where(header => header.Id == target.Id).ExecuteDeleteAsync();

        var next = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "linked-denied-next", CancellationToken.None);

        Assert.NotEqual(original.Id, next.Id);
        Assert.Equal(2, await harness.Db.DocumentCaptures.CountAsync());
    }

    [Fact]
    public async Task RuntimeDisabledBlocksAcceptButAllowsRejectLinkAndAuthorizedReads()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng(21);
        var accepted = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "flag-link", CancellationToken.None);
        var acceptedCaptureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == accepted.Id).Select(capture => capture.Id).SingleAsync();
        await harness.Service.AcceptAsync(acceptedCaptureId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Accepted\"}", CancellationToken.None);
        var target = new PurchaseInvoiceHeader { CompanyId = 1, PublicId = "bill_flag", Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "FLAG" };
        harness.Db.PurchaseInvoiceHeaders.Add(target);
        await harness.Db.SaveChangesAsync();
        harness.PublicIds.Setup(ids => ids.ResolveAsync("bill_flag", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(target.Id));
        var rejected = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(22), "flag-reject", CancellationToken.None);
        var rejectedCaptureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == rejected.Id).Select(capture => capture.Id).SingleAsync();
        var pending = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(23), "flag-accept", CancellationToken.None);
        var pendingId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == pending.Id).Select(capture => capture.Id).SingleAsync();
        harness.CaptureOptions.Value.Enabled = false;

        Assert.Equal("accepted", (await harness.Service.LinkAsync(acceptedCaptureId, "bill", "bill_flag", CancellationToken.None)).Status);
        Assert.Equal("rejected", (await harness.Service.RejectAsync(rejectedCaptureId, CancellationToken.None)).Status);
        Assert.Equal(pending.Id, (await harness.Service.GetAsync(pendingId, CancellationToken.None)).Id);
        var disabled = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.AcceptAsync(pendingId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Pending\"}", CancellationToken.None));
        Assert.Equal("DOCUMENT_CAPTURE_DISABLED", disabled.ErrorCode);
    }

    [Fact]
    public void Capabilities_SeparateRuntimeFlagProviderAndAuthorizedKinds()
    {
        using var harness = new Harness(enabled: true);
        var providerOff = new DocumentCapturesController(harness.Service, harness.Tenant, harness.Entitlement,
            harness.CaptureOptions, new UnconfiguredDocumentFieldExtractor());
        var providerOffJson = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)providerOff.Capabilities()).Value);
        using var providerOffBody = System.Text.Json.JsonDocument.Parse(providerOffJson);
        Assert.True(providerOffBody.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(providerOffBody.RootElement.GetProperty("providerAvailable").GetBoolean());
        Assert.Contains("purchase-invoice", providerOffBody.RootElement.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()));

        harness.CaptureOptions.Value.Enabled = false;
        var flagOff = new DocumentCapturesController(harness.Service, harness.Tenant, harness.Entitlement,
            harness.CaptureOptions, harness.Extractor);
        var flagOffJson = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)flagOff.Capabilities()).Value);
        using var flagOffBody = System.Text.Json.JsonDocument.Parse(flagOffJson);
        Assert.False(flagOffBody.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(flagOffBody.RootElement.GetProperty("providerAvailable").GetBoolean());
        Assert.Contains("purchase-invoice", flagOffBody.RootElement.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()));
        Assert.True(flagOffBody.RootElement.GetProperty("maxImagePixels").GetInt64() > 0);
        Assert.DoesNotContain("image/webp", flagOffBody.RootElement.GetProperty("allowedMimeTypes").EnumerateArray()
            .Select(type => type.GetString()));
    }

    [Fact]
    public async Task Upload_DailyAttemptLimitRollsAtUtcMidnight()
    {
        using var harness = new Harness(enabled: true, maxAttemptsPerHour: 20, maxAttemptsPerDay: 1);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 21, 23, 59, 0, TimeSpan.Zero));
        var service = harness.CreateService(harness.Extractor, timeProvider: clock);
        await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(1), "before-midnight", CancellationToken.None);
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 22, 0, 1, 0, TimeSpan.Zero));

        var nextDay = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(2), "after-midnight", CancellationToken.None);

        Assert.Equal("ready", nextDay.Status);
        Assert.Equal(2, await harness.Db.DocumentCaptureAttempts.CountAsync());
    }

    [Fact]
    public async Task Upload_RetryAfterRetentionPurgedBytesRestoresBlobAndReservesOneAttempt()
    {
        using var harness = new Harness(enabled: true);
        var source = CreatePng();
        var failing = harness.CreateService(new FailingExtractor());
        await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            failing.UploadAsync("purchase-invoice", 1, "image/png", source, "retry-purged", CancellationToken.None));
        var capture = await harness.Db.DocumentCaptures.SingleAsync();
        capture.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await harness.Db.SaveChangesAsync();
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));
        await retention.PurgeAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Empty(await harness.Db.DocumentCaptureBlobs.ToListAsync());

        var retry = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", source, "retry-purged", CancellationToken.None);

        Assert.Equal("ready", retry.Status);
        Assert.Equal(2, (await harness.Db.DocumentCaptures.SingleAsync()).ExtractionGeneration);
        Assert.Single(await harness.Db.DocumentCaptureBlobs.ToListAsync());
        Assert.Equal(2, await harness.Db.DocumentCaptureAttempts.CountAsync());
        Assert.Null((await harness.Db.DocumentCaptures.SingleAsync()).BytesDeletedAt);
    }

    [Fact]
    public async Task Retention_RemovesAttemptReservationsAtFortyEightHours()
    {
        using var harness = new Harness(enabled: true);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "attempt-retention", CancellationToken.None);
        var attempt = await harness.Db.DocumentCaptureAttempts.SingleAsync();
        attempt.ReservedAt = new DateTime(2026, 11, 20, 11, 59, 59, DateTimeKind.Utc);
        var capture = await harness.Db.DocumentCaptures.SingleAsync();
        capture.AcceptedAt = DateTime.UtcNow;
        await harness.Db.SaveChangesAsync();
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));

        await retention.PurgeAsync(new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Empty(await harness.Db.DocumentCaptureAttempts.ToListAsync());
        Assert.Equal(uploaded.Id, capture.PublicId);
    }

    [Fact]
    public async Task Retention_ProcessesDeletedWorkspacesBeyondOneBatch()
    {
        using var harness = new Harness(enabled: true);
        var now = DateTime.UtcNow;
        harness.Db.SuppressTenantSecurityCheck = true;
        var companies = Enumerable.Range(0, DocumentCaptureRetentionService.BatchSize + 1)
            .Select(index => new Company
            {
                Name = $"Deleted {index}", CompanyCode = $"D{index:D3}", Slug = $"deleted-{index:D3}", DeletedAt = now
            }).ToArray();
        harness.Db.Companies.AddRange(companies);
        await harness.Db.SaveChangesAsync();
        foreach (var (company, index) in companies.Select((company, index) => (company, index)))
        {
            harness.Db.DocumentCaptures.Add(new Core.Domain.DocumentCapture
            {
                CompanyId = company.Id, PublicId = PublicIdGenerator.New("cap"), Kind = "purchase-invoice", SchemaVersion = 1,
                Status = "ready", SourceHash = $"source-{index}", ContentHash = $"content-{index}",
                NormalizationVersion = "test", ContentType = "application/pdf", ByteCount = 1, PageCount = 1,
                RequestFingerprint = $"fingerprint-{index}", ExtractionGeneration = 1, CreatedBy = "creator-1",
                CreatedAt = now, ExpiresAt = now.AddDays(7), Blob = new DocumentCaptureBlob { CompanyId = company.Id, DocumentBytes = [1] },
                Attempts = [new DocumentCaptureAttempt { CompanyId = company.Id, Generation = 1, ReservedAt = now }]
            });
        }
        await harness.Db.SaveChangesAsync();
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));

        var deleted = await retention.PurgeAsync(now, CancellationToken.None);

        Assert.Equal(DocumentCaptureRetentionService.BatchSize + 1, deleted);
        Assert.Empty(await harness.Db.DocumentCaptures.IgnoreQueryFilters().Where(capture => companies.Select(item => item.Id).Contains(capture.CompanyId)).ToListAsync());
        Assert.Empty(await harness.Db.DocumentCaptureAttempts.IgnoreQueryFilters().Where(attempt => companies.Select(item => item.Id).Contains(attempt.CompanyId)).ToListAsync());
    }

    [Fact]
    public async Task Retention_ProcessesAbandonedBytesBeyondOneBatch()
    {
        using var harness = new Harness(enabled: true);
        var now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        var captures = Enumerable.Range(0, DocumentCaptureRetentionService.BatchSize + 1).Select(index =>
            new Core.Domain.DocumentCapture
            {
                CompanyId = 1, PublicId = PublicIdGenerator.New("cap"), Kind = "purchase-invoice", SchemaVersion = 1,
                Status = "ready", SourceHash = $"abandoned-source-{index}", ContentHash = $"abandoned-content-{index}",
                NormalizationVersion = "test", ContentType = "application/pdf", ByteCount = 1, PageCount = 1,
                RequestFingerprint = $"abandoned-fingerprint-{index}", ExtractionGeneration = 1, CreatedBy = "creator-1",
                CreatedAt = now.AddDays(-8), ExpiresAt = now.AddDays(-1),
                Blob = new DocumentCaptureBlob { CompanyId = 1, DocumentBytes = [1] }
            }).ToArray();
        harness.Db.DocumentCaptures.AddRange(captures);
        await harness.Db.SaveChangesAsync();
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));

        await retention.PurgeAsync(now, CancellationToken.None);

        Assert.All(await harness.Db.DocumentCaptures.AsNoTracking().ToListAsync(), capture => Assert.NotNull(capture.BytesDeletedAt));
        Assert.Empty(await harness.Db.DocumentCaptureBlobs.ToListAsync());
    }

    [Fact]
    public async Task RetentionProcessesOldAttemptsAcrossMoreThanOneCompanyBatch()
    {
        using var harness = new Harness(enabled: true);
        var now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        harness.Db.SuppressTenantSecurityCheck = true;
        var companies = Enumerable.Range(0, DocumentCaptureRetentionService.BatchSize + 1)
            .Select(index => new Company
            {
                Name = $"Attempt company {index}", CompanyCode = $"A{index:D3}", Slug = $"attempt-{index:D3}"
            }).ToArray();
        harness.Db.Companies.AddRange(companies);
        await harness.Db.SaveChangesAsync();
        foreach (var (company, index) in companies.Select((company, index) => (company, index)))
        {
            var capture = new Core.Domain.DocumentCapture
            {
                CompanyId = company.Id, PublicId = PublicIdGenerator.New("cap"), Kind = "purchase-invoice", SchemaVersion = 1,
                Status = "accepted", SourceHash = $"attempt-source-{index}", ContentHash = $"attempt-content-{index}",
                NormalizationVersion = "test", ContentType = "application/pdf", ByteCount = 1, PageCount = 1,
                RequestFingerprint = $"attempt-fingerprint-{index}", ExtractionGeneration = 1, CreatedBy = "creator-1",
                CreatedAt = now.AddDays(-3), ExpiresAt = now.AddDays(4), AcceptedAt = now.AddDays(-2)
            };
            capture.Attempts.Add(new DocumentCaptureAttempt
            {
                CompanyId = company.Id, Generation = 1, ReservedAt = now.AddHours(-49)
            });
            harness.Db.DocumentCaptures.Add(capture);
        }
        await harness.Db.SaveChangesAsync();
        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));

        await retention.PurgeAsync(now, CancellationToken.None);

        Assert.Empty(await harness.Db.DocumentCaptureAttempts.IgnoreQueryFilters()
            .Where(attempt => companies.Select(company => company.Id).Contains(attempt.CompanyId)).ToListAsync());
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
    public async Task FeatureDenialUsesSharedEntitlementProblemBody()
    {
        using var harness = new Harness(enabled: true);
        var snapshot = new EntitlementSnapshot(true, "invoicing", "invoicing", false, [], [], [], null,
            new HashSet<string>(), "invoicing");
        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(service => service.Get(1)).Returns(snapshot);
        entitlement.Setup(service => service.Evaluate(1, It.IsAny<EntitlementAction>()))
            .Returns(new EntitlementDecision(false, "FEATURE_NOT_ENTITLED", "Upgrade required.",
                RequiredFeature: FeatureKeys.DocumentCapture, UpgradeAvailable: true, EffectivePlan: "invoicing"));
        entitlement.SetupGet(service => service.Options).Returns(new EntitlementsOptions { SupportEmail = "support@example.test" });
        var service = new DocumentCaptureService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db), harness.Extractor,
            entitlement.Object, harness.PublicIds.Object, harness.Tenant, harness.CaptureOptions,
            NullLogger<DocumentCaptureService>.Instance);
        var controller = new DocumentCapturesController(service, harness.Tenant, entitlement.Object, harness.CaptureOptions, harness.Extractor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var bytes = CreatePng();
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "File", "invoice.png") { Headers = new HeaderDictionary() };
        file.ContentType = "image/png";

        var result = Assert.IsType<ObjectResult>(await controller.Upload(new DocumentCaptureUploadRequest
        {
            Kind = "purchase-invoice", SchemaVersion = 1, File = file
        }, CancellationToken.None));
        var problem = Assert.IsType<ProblemDetails>(result.Value);

        Assert.Equal(403, result.StatusCode);
        Assert.Equal("FEATURE_NOT_ENTITLED", problem.Extensions["code"]);
        Assert.Equal("support@example.test", problem.Extensions["contact"]);
        Assert.True(problem.Extensions.ContainsKey("upgradeAvailable"));
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
    public async Task GetLinked_UsesBoundedBatchQueriesForManyCaptures()
    {
        var queryCounter = new QueryCountingInterceptor();
        using var harness = new Harness(enabled: true, interceptor: queryCounter);
        var target = new PurchaseInvoiceHeader { CompanyId = 1, PublicId = "bill_batch", Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "BATCH" };
        harness.Db.PurchaseInvoiceHeaders.Add(target);
        await harness.Db.SaveChangesAsync();
        harness.PublicIds.Setup(ids => ids.ResolveAsync("bill_batch", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(target.Id));
        for (var i = 0; i < 5; i++)
        {
            var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(i + 41), $"batch-{i}", CancellationToken.None);
            var captureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == uploaded.Id).Select(capture => capture.Id).SingleAsync();
            await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Batch\"}", CancellationToken.None);
            await harness.Service.LinkAsync(captureId, "bill", "bill_batch", CancellationToken.None);
        }
        queryCounter.Reset();

        var linked = await harness.Service.GetLinkedAsync("bill", "bill_batch", CancellationToken.None);

        Assert.Equal(5, linked.Count);
        Assert.InRange(queryCounter.SelectCount, 1, 5);
    }

    [Fact]
    public async Task Get_UsesBoundedBatchQueriesAcrossMultipleReadableLinks()
    {
        var queryCounter = new QueryCountingInterceptor();
        using var harness = new Harness(enabled: true, interceptor: queryCounter);
        var bills = Enumerable.Range(1, 3)
            .Select(i => new PurchaseInvoiceHeader
            {
                CompanyId = 1,
                PublicId = $"bill_get_{i}",
                Date = new DateTime(2026, 11, 20),
                VendorInvoiceNo = $"GET-{i}"
            })
            .ToArray();
        harness.Db.PurchaseInvoiceHeaders.AddRange(bills);

        var customer = new Core.Domain.Sales.Customer
        {
            CompanyId = 1,
            No = "CUST-GET",
            Party = new Party { CompanyId = 1, Name = "Get Customer", PartyType = PartyTypes.Customer }
        };
        harness.Db.Customers.Add(customer);
        await harness.Db.SaveChangesAsync();

        var invoices = Enumerable.Range(1, 3)
            .Select(i => new Core.Domain.Sales.SalesInvoiceHeader
            {
                CompanyId = 1,
                CustomerId = customer.Id,
                PublicId = $"inv_get_{i}",
                No = $"INV-GET-{i}",
                Date = new DateTime(2026, 11, 20),
                Status = SalesInvoiceStatus.Draft
            })
            .ToArray();
        harness.Db.SalesInvoiceHeaders.AddRange(invoices);
        await harness.Db.SaveChangesAsync();

        harness.PublicIds.Setup(ids => ids.ResolveAsync("bill_get_1", "bill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PublicIdLookupResult.Success(bills[0].Id));

        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(55), "get-bounded", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, "{\"date\":\"2026-11-20\",\"vendorName\":\"Bounded\"}", CancellationToken.None);
        await harness.Service.LinkAsync(captureId, "bill", "bill_get_1", CancellationToken.None);

        harness.Db.DocumentCaptureLinks.AddRange(
            new DocumentCaptureLink { CompanyId = 1, CaptureId = captureId, TargetType = "bill", TargetId = bills[1].Id, Role = DocumentCaptureLinkRoles.Attachment, LinkedBy = "creator-1", LinkedAt = DateTime.UtcNow.AddSeconds(1) },
            new DocumentCaptureLink { CompanyId = 1, CaptureId = captureId, TargetType = "bill", TargetId = bills[2].Id, Role = DocumentCaptureLinkRoles.Attachment, LinkedBy = "creator-1", LinkedAt = DateTime.UtcNow.AddSeconds(2) },
            new DocumentCaptureLink { CompanyId = 1, CaptureId = captureId, TargetType = "invoice", TargetId = invoices[0].Id, Role = DocumentCaptureLinkRoles.Attachment, LinkedBy = "creator-1", LinkedAt = DateTime.UtcNow.AddSeconds(3) },
            new DocumentCaptureLink { CompanyId = 1, CaptureId = captureId, TargetType = "invoice", TargetId = invoices[1].Id, Role = DocumentCaptureLinkRoles.Attachment, LinkedBy = "creator-1", LinkedAt = DateTime.UtcNow.AddSeconds(4) },
            new DocumentCaptureLink { CompanyId = 1, CaptureId = captureId, TargetType = "invoice", TargetId = invoices[2].Id, Role = DocumentCaptureLinkRoles.Attachment, LinkedBy = "creator-1", LinkedAt = DateTime.UtcNow.AddSeconds(5) });
        await harness.Db.SaveChangesAsync();
        harness.Db.ChangeTracker.Clear();
        queryCounter.Reset();

        var view = await harness.Service.GetAsync(captureId, CancellationToken.None);

        Assert.Equal("bill_get_1", view.Link?.TargetId);
        Assert.Equal(6, view.Links.Count);
        Assert.InRange(queryCounter.SelectCount, 1, 6);
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
    public async Task LogsAndAuditNeverContainProviderFinancialValuesOrFailureText()
    {
        using var harness = new Harness(enabled: true);
        var logger = new CapturingLogger();
        var events = new List<string>();
        var audit = new Mock<ISecurityAuditService>();
        audit.Setup(service => service.LogEventAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<object?>()))
            .Callback<string, string?, string?, int?, object?>((eventType, _, _, _, payload) => events.Add(
                eventType + " " + System.Text.Json.JsonSerializer.Serialize(payload)))
            .Returns(Task.CompletedTask);
        var secureTenant = new[]
        {
            SystemPermissions.BankingAccountsManage,
            SystemPermissions.PurchasesBillsView,
            SystemPermissions.PurchasesBillsCreate
        };
        harness.Tenant.Permissions = new HashSet<string>(secureTenant);
        var bankExtractor = new StaticExtractor("{\"bankName\":\"Fixture Bank\",\"routingNumber\":\"021000021\",\"accountNumber\":\"123456789012\",\"iban\":\"GB82WEST12345698765432\"}");
        var service = harness.CreateService(bankExtractor, audit.Object, logger: logger);
        await service.UploadAsync("bank", 1, "image/png", CreatePng(), "sensitive-bank", CancellationToken.None);
        var logsBeforeFailure = logger.Messages.Count;
        var auditBeforeFailure = events.Count;
        try
        {
            await harness.CreateService(new SensitiveFailingExtractor(), audit.Object, logger: logger)
                .UploadAsync("purchase-invoice", 1, "image/png", CreatePng(unchecked((int)0xFF123456)), "sensitive-failure", CancellationToken.None);
        }
        catch (DocumentCaptureServiceException) { }

        Assert.True(logger.Messages.Count > logsBeforeFailure, "The provider failure path did not produce a log entry.");
        Assert.True(events.Count > auditBeforeFailure, "The provider failure path did not produce an audit entry.");
        Assert.Contains(events, item => item.Contains("document.capture.failed", StringComparison.Ordinal));

        var noteService = harness.CreateService(new StaticExtractor("{\"date\":\"2026-11-20\"}"), audit.Object, logger: logger);
        var noteUpload = await noteService.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(1), "sensitive-note", CancellationToken.None);
        var noteCaptureId = await harness.Db.DocumentCaptures.Where(capture => capture.PublicId == noteUpload.Id)
            .Select(capture => capture.Id).SingleAsync();
        await noteService.AcceptAsync(noteCaptureId,
            "{\"date\":\"2026-11-20\",\"notes\":\"account 123456789012\"}", CancellationToken.None);

        var observed = string.Join("\n", logger.Messages.Concat(events));
        Assert.DoesNotContain("021000021", observed, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789012", observed, StringComparison.Ordinal);
        Assert.DoesNotContain("GB82WEST12345698765432", observed, StringComparison.Ordinal);
        Assert.DoesNotContain("provider failure body", observed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fixture Bank", observed, StringComparison.Ordinal);
        Assert.DoesNotContain("account 123456789012", observed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_ProviderRunsOutsideTransactionAndStaleGenerationCannotFinalize()
    {
        using var harness = new Harness(enabled: true);
        var extractor = new BlockingExtractor(harness.Db);
        var service = harness.CreateService(extractor);
        var upload = service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "stale-generation", CancellationToken.None);
        await extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

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

    [Fact]
    public async Task CancelledProviderThatIgnoresCancellationCannotOverwriteRetriedGeneration()
    {
        using var harness = new Harness(enabled: true);
        var extractor = new IgnoreCancellationOnFirstCallExtractor();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = harness.CreateService(extractor, timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var firstUpload = service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "ignored-cancellation", cancellation.Token);
        await extractor.FirstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstUpload);

        var capture = await harness.Db.DocumentCaptures.SingleAsync();
        Assert.Equal("extracting", capture.Status);
        Assert.Equal(1, await harness.Db.DocumentCaptureAttempts.CountAsync());
        var replay = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "ignored-cancellation", CancellationToken.None));
        Assert.Equal("DOCUMENT_EXTRACTION_IN_PROGRESS", replay.ErrorCode);
        Assert.Equal(1, await harness.Db.DocumentCaptureAttempts.CountAsync());

        Assert.NotNull(capture.LeaseExpiresAt);
        clock.SetUtcNow(new DateTimeOffset(capture.LeaseExpiresAt.Value, TimeSpan.Zero).AddSeconds(1));
        harness.Db.ChangeTracker.Clear();
        var retry = await service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(), "ignored-cancellation", CancellationToken.None);
        Assert.Equal("Generation 2", retry.Fields?.Value<string>("vendorName"));
        Assert.Equal(2, (await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(item => item.Id == capture.Id)).ExtractionGeneration);
        Assert.Equal(2, await harness.Db.DocumentCaptureAttempts.CountAsync());

        extractor.ReleaseLateResult();
        await extractor.FirstCallResult;
        var afterLateResult = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(item => item.Id == capture.Id);
        Assert.Equal(2, afterLateResult.ExtractionGeneration);
        Assert.Equal("ready", afterLateResult.Status);
        Assert.Contains("Generation 2", afterLateResult.FieldsJson, StringComparison.Ordinal);
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
        public Mock<IEntitlementService> EntitlementMock { get; private set; } = null!;
        public IDocumentFieldExtractor Extractor { get; }

        public Harness(bool enabled, int maxAttemptsPerHour = 20, int maxAttemptsPerDay = 60, DbCommandInterceptor? interceptor = null, int abandonedRetentionHours = 24)
        {
            _connection.Open();
            _tenant = new TestTenantContext(1);
            var contextOptions = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection);
            if (interceptor is not null)
                contextOptions.AddInterceptors(interceptor);
            Db = new ApiDbContext(contextOptions.Options, _tenant);
            Db.Database.EnsureCreated();
            Db.Companies.Add(new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" });
            Db.SaveChanges();
            _tenant.Permissions = new HashSet<string>(StringComparer.Ordinal)
            {
                SystemPermissions.PurchasesBillsCreate,
                SystemPermissions.PurchasesBillsView,
                SystemPermissions.SalesInvoicesCreate,
                SystemPermissions.SalesInvoicesView,
                SystemPermissions.BankingAccountsManage
            };

            var snapshot = new EntitlementSnapshot(true, "invoicing", "invoicing", false, [], [], [], null,
                new HashSet<string> { FeatureKeys.DocumentCapture }, "invoicing");
            var entitlementMock = new Mock<IEntitlementService>();
            EntitlementMock = entitlementMock;
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
                AbandonedRetentionHours = abandonedRetentionHours,
                FakeFixtures = new Dictionary<string, string>
                {
                    ["purchase-invoice"] = "{\"vendorName\":\"Fixture Vendor\",\"date\":\"2026-11-20\",\"lines\":[]}",
                    ["bank"] = "{\"bankName\":\"Fixture Bank\",\"accountNumber\":\"021000021\"}"
                }
            });
            Extractor = new FakeDocumentFieldExtractor(_configuredOptions);
            Service = CreateService(Extractor);
        }

        public DocumentCaptureService CreateService(IDocumentFieldExtractor extractor, ISecurityAuditService? audit = null, TimeProvider? timeProvider = null,
            Microsoft.Extensions.Logging.ILogger<DocumentCaptureService>? logger = null, IDocumentCaptureAdmissionLock? admissionLock = null,
            Api.DocumentCaptures.Mapping.ICaptureAiMappingPolicy? aiMappingPolicy = null) =>
            new(Db, admissionLock ?? new DocumentCaptureAdmissionLock(Db), extractor, _entitlement, PublicIds.Object,
                _tenant, _configuredOptions, logger ?? NullLogger<DocumentCaptureService>.Instance, audit, timeProvider, aiMappingPolicy);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class QueryCountingInterceptor : DbCommandInterceptor
    {
        private int _selectCount;
        public int SelectCount => Volatile.Read(ref _selectCount);
        public void Reset() => Interlocked.Exchange(ref _selectCount, 0);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _selectCount);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class TestTenantContext(int companyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int? CompanyId => companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId { get; set; } = "creator-1";
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

    private sealed class IgnoreCancellationOnFirstCallExtractor : IDocumentFieldExtractor
    {
        private int _calls;
        private readonly TaskCompletionSource<DocumentExtractionResult> _firstResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConfigured => true;
        public string ProviderName => "test";
        public TaskCompletionSource FirstCallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DocumentExtractionResult> FirstCallResult => _firstResult.Task;

        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstCallEntered.TrySetResult();
                return _firstResult.Task;
            }
            return Task.FromResult(new DocumentExtractionResult(
                "{\"date\":\"2026-11-20\",\"vendorName\":\"Generation 2\"}", request.Kind, 1,
                Array.Empty<string>(), null, "test", "2"));
        }

        public void ReleaseLateResult() => _firstResult.TrySetResult(new DocumentExtractionResult(
            "{\"date\":\"2026-11-20\",\"vendorName\":\"Stale generation 1\"}", "purchase-invoice", 1,
            Array.Empty<string>(), null, "test", "1"));
    }

    private sealed class FailingExtractor : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "test";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromException<DocumentExtractionResult>(new InvalidOperationException("provider body included routing 021000021"));
    }

    private sealed class StaticExtractor(string fieldsJson) : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "fixture";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentExtractionResult(fieldsJson, request.Kind, 1, Array.Empty<string>(),
                "raw 021000021 GB82WEST12345698765432", "fixture", "1"));
    }

    private sealed class SensitiveFailingExtractor : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "fixture";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromException<DocumentExtractionResult>(new InvalidOperationException(
                "provider failure body: routing 021000021 account 123456789012 IBAN GB82WEST12345698765432"));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<DocumentCaptureService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null) Messages.Add(exception.ToString());
        }
    }

    private sealed class CountingOversizeStream(long length) : Stream
    {
        private long _position;
        public long BytesRead => _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, length - _position);
            buffer[..count].Clear();
            _position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return _position;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class OversizedFormFile(Stream stream, long reportedLength) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => "form-data; name=\"file\"; filename=\"oversized.png\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => reportedLength;
        public string Name => "file";
        public string FileName => "oversized.png";
        public void CopyTo(Stream target) => stream.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => stream.CopyToAsync(target, cancellationToken);
        public Stream OpenReadStream() => stream;
    }
}
