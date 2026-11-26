// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using AccountGo.BackendTests.DocumentCapture;
using Api.Data;
using Api.DocumentCaptures;
using Api.PublicIds;
using Api.Service;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.Documents;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentLibraryServiceTests
{
    private const int MiB = 1024 * 1024;

    [Fact]
    public async Task Upload_StoresFiledDocumentWithoutExtraction_AndAuditsAndBumpsRevision()
    {
        await using var h = await Harness.CreateAsync();
        var pdf = CreatePdf(3);

        var view = await h.Library.UploadAsync(pdf, "application/pdf", "C:\\fakepath\\Lease 2026.pdf",
            new DocumentMetadataInput(null, "Contract", new DateTime(2026, 1, 15), "Office lease"), null, null, "up-1", false, default);

        Assert.StartsWith("cap_", view.Id, StringComparison.Ordinal);
        Assert.Equal("document", view.Kind);
        Assert.Equal("stored", view.Status);
        Assert.Equal("Lease 2026", view.Title);
        Assert.Equal("Lease 2026.pdf", view.FileName);
        Assert.Equal("contract", view.Category);
        Assert.Equal(3, view.PageCount);
        Assert.Equal(view.StoredSha256, view.UploadedSha256);
        Assert.False(view.StoredCopyOptimised);
        Assert.Empty(view.Links);

        var row = await h.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.PublicId == view.Id);
        Assert.NotNull(row.FiledAt);
        Assert.Equal(0, row.ExtractionGeneration);
        Assert.False(await h.Db.DocumentCaptureAttempts.AnyAsync());
        Assert.Equal(1L, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
        h.Audit.Verify(a => a.LogEventAsync("document.uploaded", "success", "actor-1", h.CompanyId, It.IsAny<object?>()), Times.Once);

        var (bytes, contentType, fileName) = await h.Library.GetBytesAsync(row.Id, default);
        Assert.Equal(pdf, bytes);
        Assert.Equal("application/pdf", contentType);
        Assert.Equal("Lease 2026.pdf", fileName);
    }

    [Fact]
    public async Task Upload_ImageIsStoredAsOptimisedCopyWithBothHashes()
    {
        await using var h = await Harness.CreateAsync();
        var png = CreatePng(40, 30);

        var view = await h.Library.UploadAsync(png, "image/png", "receipt.png", Meta(), null, null, null, false, default);

        Assert.Equal(Sha(png), view.UploadedSha256);
        var row = await h.Db.DocumentCaptures.AsNoTracking().Include(c => c.Blob).SingleAsync(c => c.PublicId == view.Id);
        Assert.Equal(Sha(row.Blob!.DocumentBytes), view.StoredSha256);
    }

    [Fact]
    public async Task Upload_IntoLibraryNeedsManage_ButAttachingNeedsOnlyTheRecordPermission()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        h.Tenant.SetPermissions(SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate);

        var denied = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal(403, denied.StatusCode);

        var attached = await h.Library.UploadAsync(CreatePdf(1), "application/pdf", "receipt.pdf", Meta(), "bill", bill.PublicId, null, false, default);
        var link = Assert.Single(attached.Links);
        Assert.Equal(("bill", bill.PublicId, "attachment"), (link.TargetType, link.TargetId, link.Role));

        // Read through the bill without documents.view; the library list stays closed.
        var linked = await h.Library.ListLinkedAsync("bill", bill.PublicId, 1, 50, default);
        Assert.Equal(attached.Id, Assert.Single(linked.Items).Id);
        var captureId = await h.IdOf(attached.Id);
        Assert.Equal(attached.Id, (await h.Library.GetAsync(captureId, default)).Id);
        var listDenied = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default));
        Assert.Equal(403, listDenied.StatusCode);

        // Without the bill's view permission the file is not found.
        h.Tenant.SetPermissions(SystemPermissions.SalesInvoicesView);
        var hidden = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Library.GetAsync(captureId, default));
        Assert.Equal("DOCUMENT_NOT_FOUND", hidden.ErrorCode);
    }

    [Fact]
    public async Task Limits_RecordUploadsUseRecordLimit_LibraryUploadsUseLibraryLimit()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var large = CreateNoisePng(2_400, 1_500);
        Assert.InRange(large.Length, 9 * MiB, 15 * MiB);

        var record = await Assert.ThrowsAsync<DocumentLimitException>(() =>
            h.Library.UploadAsync(large, "image/png", "big.png", Meta(), "bill", bill.PublicId, null, false, default));
        Assert.Equal(413, record.StatusCode);
        Assert.Equal(new DocumentUploadLimit(8 * MiB, 10), record.Limit);

        var library = await h.Library.UploadAsync(large, "image/png", "big.png", Meta(), null, null, null, false, default);
        Assert.True(library.StoredCopyOptimised);

        var pages = await Assert.ThrowsAsync<DocumentLimitException>(() =>
            h.Library.UploadAsync(CreatePdf(11), "application/pdf", "long.pdf", Meta(), "bill", bill.PublicId, null, false, default));
        Assert.Equal("DOCUMENT_TOO_MANY_PAGES", pages.ErrorCode);
        Assert.NotNull(await h.Library.UploadAsync(CreatePdf(11), "application/pdf", "long.pdf", Meta(), null, null, null, false, default));
    }

    [Fact]
    public async Task Limits_EngineThatGrantsNothingStoresNothing()
    {
        await using var h = await Harness.CreateAsync(DocumentStoragePolicy.NotGranted);
        var ex = await Assert.ThrowsAsync<DocumentStorageLimitException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(0, ex.LimitBytes);
    }

    [Fact]
    public async Task Quota_CountsAdditionalBytesOnly_TrashStillCounts_RestoreIsAlwaysAllowed()
    {
        await using var h = await Harness.CreateAsync();
        var first = CreatePdf(1, label: "one");
        h.Policy = DocumentStoragePolicy.Community with { StorageLimitBytes = first.Length };

        var stored = await h.Library.UploadAsync(first, "application/pdf", "one.pdf", Meta(), null, null, null, false, default);
        var over = await Assert.ThrowsAsync<DocumentStorageLimitException>(() =>
            h.Library.UploadAsync(CreatePdf(1, label: "two"), "application/pdf", "two.pdf", Meta(), null, null, null, false, default));
        Assert.Equal(first.Length, over.UsedBytes);

        var id = await h.IdOf(stored.Id);
        await h.Library.TrashAsync(id, default);
        await Assert.ThrowsAsync<DocumentStorageLimitException>(() =>
            h.Library.UploadAsync(CreatePdf(1, label: "two"), "application/pdf", "two.pdf", Meta(), null, null, null, false, default));

        h.Policy = DocumentStoragePolicy.Community with { StorageLimitBytes = 1 };
        var restored = await h.Library.RestoreAsync(id, default);
        Assert.False(restored.InTrash);

        await h.Library.TrashAsync(id, default);
        await h.Library.DeletePermanentlyAsync(id, default);
        h.Policy = DocumentStoragePolicy.Community with { StorageLimitBytes = first.Length };
        Assert.NotNull(await h.Library.UploadAsync(CreatePdf(1, label: "two"), "application/pdf", "two.pdf", Meta(), null, null, null, false, default));
    }

    [Fact]
    public async Task Idempotency_ReplayReturnsSameDocumentAndLink_DifferentInputConflicts()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var pdf = CreatePdf(1);

        var first = await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta("Lease"), "bill", bill.PublicId, "key-1", false, default);
        var replay = await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta("Lease"), "bill", bill.PublicId, "key-1", false, default);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(replay.Links);
        Assert.Equal(1, await h.Db.DocumentCaptureLinks.CountAsync());

        var conflict = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta("Other title"), "bill", bill.PublicId, "key-1", false, default));
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.ErrorCode);
    }

    [Fact]
    public async Task Duplicates_ReadableMatchIsReported_UnreadableMatchIsNot_ForceStoresACopy()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var pdf = CreatePdf(1);
        var original = await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta(), "bill", bill.PublicId, null, false, default);

        var duplicate = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal(("DOCUMENT_DUPLICATE", original.Id), (duplicate.ErrorCode, duplicate.CaptureId));

        var copy = await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta(), null, null, null, true, default);
        Assert.NotEqual(original.Id, copy.Id);

        // A caller who can't read the existing file is not told it exists.
        var invoice = h.SeedInvoice("I-1");
        h.Tenant.SetPermissions(SystemPermissions.SalesInvoicesView, SystemPermissions.SalesInvoicesCreate);
        var other = CreatePdf(2);
        h.Tenant.SetPermissions(SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate);
        await h.Library.UploadAsync(other, "application/pdf", "b.pdf", Meta(), "bill", bill.PublicId, null, false, default);
        h.Tenant.SetPermissions(SystemPermissions.SalesInvoicesView, SystemPermissions.SalesInvoicesCreate);
        var unseen = await h.Library.UploadAsync(other, "application/pdf", "b.pdf", Meta(), "invoice", invoice.PublicId, null, false, default);
        Assert.Single(unseen.Links);
    }

    [Fact]
    public async Task PostedRecordsLockTheirAttachments_DraftsDoNot()
    {
        await using var h = await Harness.CreateAsync();
        var draft = h.SeedBill("B-DRAFT");
        var posted = h.SeedBill("B-POSTED");
        var doc = await h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default);
        var id = await h.IdOf(doc.Id);
        await h.Library.LinkAsync(id, "bill", draft.PublicId, default);
        await h.Library.LinkAsync(id, "bill", posted.PublicId, default);
        await h.Db.PurchaseInvoiceHeaders.Where(b => b.Id == posted.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PurchaseInvoiceStatus.Open));

        var view = await h.Library.GetAsync(id, default);
        Assert.True(view.HasLockedLink);
        Assert.True(view.Links.Single(l => l.TargetId == posted.PublicId).Locked);

        var unlinkPosted = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UnlinkAsync(id, "bill", posted.PublicId, default));
        Assert.Equal("DOCUMENT_LOCKED_BY_POSTED_RECORD", unlinkPosted.ErrorCode);
        var trash = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Library.TrashAsync(id, default));
        Assert.Equal("DOCUMENT_LOCKED_BY_POSTED_RECORD", trash.ErrorCode);

        await h.Library.UnlinkAsync(id, "bill", draft.PublicId, default);
        Assert.Single((await h.Library.GetAsync(id, default)).Links);
        Assert.NotNull(await h.Library.ArchiveAsync(id, default));

        // A hidden locked record still locks the file.
        h.Tenant.SetPermissions(SystemPermissions.DocumentsView, SystemPermissions.DocumentsManage);
        var hiddenView = await h.Library.GetAsync(id, default);
        Assert.Empty(hiddenView.Links);
        Assert.True(hiddenView.HasLockedLink);
        await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Library.TrashAsync(id, default));
    }

    [Fact]
    public async Task ScanSourcesCannotBeTrashedFromTheLibrary_ButAreListedAndAnnotatable()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var scanId = await h.SeedFiledScanAsync(bill);

        var listed = await h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default);
        var item = Assert.Single(listed.Items);
        Assert.Equal("purchase-invoice", item.Kind);
        Assert.True(item.HasSourceLink);
        Assert.Equal("Scanned bill", item.Title);

        var updated = await h.Library.UpdateMetadataAsync(scanId, new DocumentMetadataInput("Paper supplier bill", "invoice", null, null), default);
        Assert.Equal("Paper supplier bill", updated.Title);
        var trash = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Library.TrashAsync(scanId, default));
        Assert.Equal("DOCUMENT_SOURCE_LINK_FIXED", trash.ErrorCode);

        // Scans need the scan's own read rule in the library, not just documents.view.
        h.Tenant.SetPermissions(SystemPermissions.DocumentsView, SystemPermissions.DocumentsManage);
        Assert.Empty((await h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default)).Items);
    }

    [Fact]
    public async Task TrashRestoreAndPermanentDelete_FollowTheirRules_AndEachBumpsTheRevision()
    {
        await using var h = await Harness.CreateAsync();
        var doc = await h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default);
        var id = await h.IdOf(doc.Id);
        var notInTrash = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Library.DeletePermanentlyAsync(id, default));
        Assert.Equal("DOCUMENT_NOT_IN_TRASH", notInTrash.ErrorCode);

        var revision = await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId);
        Assert.True((await h.Library.TrashAsync(id, default)).InTrash);
        Assert.Equal(revision + 1, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
        Assert.Single((await h.Library.ListAsync(new DocumentListQuery(null, null, false, true, null, 1, 50), default)).Items);
        Assert.Empty((await h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default)).Items);
        Assert.False((await h.Library.RestoreAsync(id, default)).InTrash);
        Assert.Equal(revision + 2, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
        await h.Library.TrashAsync(id, default);
        await h.Library.DeletePermanentlyAsync(id, default);
        Assert.Equal(revision + 4, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
        Assert.False(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == id));
        Assert.False(await h.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == id));
        Assert.False(await h.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == id));
        h.Audit.Verify(a => a.LogEventAsync("document.deleted", "success", "actor-1", h.CompanyId, It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task List_FiltersByCategoryLinkedSearchAndArchive()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        await h.Library.UploadAsync(CreatePdf(1, label: "a"), "application/pdf", "lease.pdf", Meta(category: "contract", notes: "Office lease"), null, null, null, false, default);
        await h.Library.UploadAsync(CreatePdf(1, label: "b"), "application/pdf", "receipt.pdf", Meta(category: "receipt"), "bill", bill.PublicId, null, false, default);
        var archived = await h.Library.UploadAsync(CreatePdf(1, label: "c"), "application/pdf", "old.pdf", Meta(), null, null, null, false, default);
        await h.Library.ArchiveAsync(await h.IdOf(archived.Id), default);

        Task<DocumentPage> List(string? category = null, bool? linked = null, bool archivedOnly = false, string? q = null) =>
            h.Library.ListAsync(new DocumentListQuery(category, linked, archivedOnly, false, q, 1, 50), default);

        Assert.Equal(2, (await List()).Total);
        Assert.Equal("lease", Assert.Single((await List(category: "CONTRACT")).Items).Title);
        Assert.Equal("receipt", Assert.Single((await List(linked: true)).Items).Title);
        Assert.Equal("lease", Assert.Single((await List(linked: false)).Items).Title);
        Assert.Equal("lease", Assert.Single((await List(q: "OFFICE")).Items).Title);
        Assert.Equal("old", Assert.Single((await List(archivedOnly: true)).Items).Title);
        var paged = await h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 2, 1), default);
        Assert.Equal((2, 2, 1), (paged.Total, paged.Page, paged.PageSize));
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task Metadata_IsValidated()
    {
        await using var h = await Harness.CreateAsync();
        var bad = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(category: "secret"), null, null, null, false, default));
        Assert.Equal("DOCUMENT_CATEGORY_INVALID", bad.ErrorCode);
        var notes = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(notes: new string('x', 1_001)), null, null, null, false, default));
        Assert.Equal("DOCUMENT_NOTES_TOO_LONG", notes.ErrorCode);
        var type = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync("hello"u8.ToArray(), "text/plain", "a.txt", Meta(), null, null, null, false, default));
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", type.ErrorCode);
        var mismatch = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "image/png", "a.png", Meta(), null, null, null, false, default));
        Assert.Equal("DOCUMENT_MEDIA_MISMATCH", mismatch.ErrorCode);
        var view = await h.Library.UploadAsync(CreatePdf(1), "application/pdf", "../../etc/\u0007evil.pdf", Meta(), null, null, null, false, default);
        Assert.Equal("evil.pdf", view.FileName);
    }

    [Fact]
    public async Task Disabled_RefusesEveryLibraryCall()
    {
        await using var h = await Harness.CreateAsync(enabled: false);
        var ex = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal("DOCUMENTS_DISABLED", ex.ErrorCode);
        var list = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default));
        Assert.Equal("DOCUMENTS_DISABLED", list.ErrorCode);
    }

    [Fact]
    public async Task DeletedTargetHook_RemovesLinksKeepsFilesAndBumpsRevision()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var doc = await h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), "bill", bill.PublicId, null, false, default);
        var revision = await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId);

        Assert.Equal(1, await DocumentLibraryService.RemoveLinksForDeletedTargetAsync(h.Db, h.CompanyId, "bill", bill.Id, default));
        Assert.Equal(revision + 1, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
        Assert.Empty((await h.Library.GetAsync(await h.IdOf(doc.Id), default)).Links);
        Assert.Equal(0, await DocumentLibraryService.RemoveLinksForDeletedTargetAsync(h.Db, h.CompanyId, "bill", bill.Id, default));
        Assert.Equal(revision + 1, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
    }

    [Fact]
    public async Task TrashRetention_PurgesOnlyExpiredUnlockedTrash()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var now = DateTime.UtcNow;
        async Task<int> Trashed(string label, bool old, string? linkBill = null)
        {
            var view = await h.Library.UploadAsync(CreatePdf(1, label: label), "application/pdf", $"{label}.pdf", Meta(), null, null, null, false, default);
            var id = await h.IdOf(view.Id);
            if (linkBill is not null)
                await h.Library.LinkAsync(id, "bill", linkBill, default);
            await h.Library.TrashAsync(id, default);
            if (old)
                await h.Db.DocumentCaptures.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, now.AddDays(-31)));
            return id;
        }
        var expired = await Trashed("expired", old: true);
        var recent = await Trashed("recent", old: false);
        var lockedLater = await Trashed("locked", old: true, linkBill: bill.PublicId);
        var restored = await Trashed("restored", old: true);
        await h.Db.DocumentCaptures.Where(c => c.Id == restored).ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, (DateTime?)null));
        await h.Db.PurchaseInvoiceHeaders.Where(b => b.Id == bill.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PurchaseInvoiceStatus.Open));
        var revision = await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId);

        var retentionAudit = new Mock<ISecurityAuditService>();
        var retention = new DocumentCaptureRetentionService(h.Db, new DocumentCaptureAdmissionLock(h.Db),
            documentsOptions: Options.Create(new DocumentsOptions { TrashRetentionDays = 30 }), securityAudit: retentionAudit.Object);
        var expiredPublicId = await h.Db.DocumentCaptures.Where(c => c.Id == expired).Select(c => c.PublicId).SingleAsync();
        Assert.Equal(1, await retention.PurgeAsync(now, default));

        // One audit event, for the purge that committed; the refused (locked) and kept items write none.
        var purgedEvent = Assert.Single(retentionAudit.Invocations, i => i.Method.Name == nameof(ISecurityAuditService.LogEventAsync));
        Assert.Equal("document.purged", purgedEvent.Arguments[0]);
        Assert.Null(purgedEvent.Arguments[2]);
        Assert.Contains(expiredPublicId, System.Text.Json.JsonSerializer.Serialize(purgedEvent.Arguments[4]));
        Assert.Equal(0, await retention.PurgeAsync(now, default));
        Assert.Single(retentionAudit.Invocations, i => i.Method.Name == nameof(ISecurityAuditService.LogEventAsync));

        Assert.False(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == expired));
        Assert.False(await h.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == expired));
        Assert.True(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == recent));
        Assert.True(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == lockedLater));
        Assert.True(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == restored));
        Assert.Equal(revision + 1, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
    }

    [Fact]
    public async Task ScanWhoseDraftWasDeleted_StaysVisibleAndRemovable()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var scanId = await h.SeedFiledScanAsync(bill);
        await DocumentLibraryService.RemoveLinksForDeletedTargetAsync(h.Db, h.CompanyId, "bill", bill.Id, default);

        var listed = Assert.Single((await h.Library.ListAsync(new DocumentListQuery(null, null, false, false, null, 1, 50), default)).Items);
        Assert.False(listed.HasSourceLink);
        Assert.True((await h.Library.TrashAsync(scanId, default)).InTrash);
    }

    [Fact]
    public async Task IdempotencyKeyOfAnUnreadableFile_IsIgnored_NotReportedAsAConflict()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var invoice = h.SeedInvoice("I-1");
        h.Tenant.SetPermissions(SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate);
        var billDoc = await h.Library.UploadAsync(CreatePdf(1, label: "bill"), "application/pdf", "a.pdf", Meta(), "bill", bill.PublicId, "shared-key", false, default);

        // Another role, same key, different content: a fresh upload, nothing revealed about the existing file.
        h.Tenant.SetPermissions(SystemPermissions.SalesInvoicesView, SystemPermissions.SalesInvoicesCreate);
        var invoiceDoc = await h.Library.UploadAsync(CreatePdf(1, label: "invoice"), "application/pdf", "b.pdf", Meta(), "invoice", invoice.PublicId, "shared-key", false, default);
        Assert.NotEqual(billDoc.Id, invoiceDoc.Id);
        Assert.Equal(2, await h.Db.DocumentCaptures.CountAsync(c => c.Kind == "document"));
        Assert.Equal("shared-key", (await h.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.PublicId == billDoc.Id)).IdempotencyKey);

        // The owner of the key still gets the conflict for different input.
        h.Tenant.SetPermissions(SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate);
        var conflict = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1, label: "other"), "application/pdf", "a.pdf", Meta(), "bill", bill.PublicId, "shared-key", false, default));
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.ErrorCode);
    }

    [Fact]
    public async Task UploadRateCountsEveryAttempt_DeletedAndInvalidIncluded_UntilTheWindowPasses()
    {
        await using var h = await Harness.CreateAsync(maxUploadsPerHour: 3);
        var view = await h.Library.UploadAsync(CreatePdf(1, label: "n0"), "application/pdf", "a.pdf", Meta(), null, null, null, false, default);
        var id = await h.IdOf(view.Id);
        await h.Library.TrashAsync(id, default);
        await h.Library.DeletePermanentlyAsync(id, default);
        await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync("%PDF-1.4 broken"u8.ToArray(), "application/pdf", "bad.pdf", Meta(), null, null, null, false, default));
        await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(new byte[] { 1, 2, 3 }, "image/png", "bad.png", Meta(), null, null, null, false, default));

        var limited = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1, label: "n4"), "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal((429, "DOCUMENT_RATE_LIMITED"), (limited.StatusCode, limited.ErrorCode));
        Assert.Equal(3, await h.Db.DocumentUploadAdmissions.CountAsync());
        Assert.False(await h.Db.DocumentCaptures.AnyAsync());

        // An idempotent replay doesn't consume a slot; once the hour passes uploads are allowed again.
        await h.Db.DocumentUploadAdmissions.ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedAt, DateTime.UtcNow.AddHours(-2)));
        var pdf = CreatePdf(1, label: "n5");
        var keyed = await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta(), null, null, "k-5", false, default);
        Assert.Equal(keyed.Id, (await h.Library.UploadAsync(pdf, "application/pdf", "a.pdf", Meta(), null, null, "k-5", false, default)).Id);
        Assert.Equal(1, await h.Db.DocumentUploadAdmissions.CountAsync(a => a.ReservedAt > DateTime.UtcNow.AddHours(-1)));

        // The retention worker clears admissions older than a day.
        await h.Db.DocumentUploadAdmissions.ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedAt, DateTime.UtcNow.AddDays(-2)));
        await new DocumentCaptureRetentionService(h.Db, new DocumentCaptureAdmissionLock(h.Db)).PurgeAsync(DateTime.UtcNow, default);
        Assert.False(await h.Db.DocumentUploadAdmissions.AnyAsync());
    }

    [Fact]
    public async Task PermanentDeleteOfAnUnlinkedScan_LeavesNoExtractedContentOrMappings()
    {
        await using var h = await Harness.CreateAsync();
        var bill = h.SeedBill("B-1");
        var scanId = await h.SeedFiledScanAsync(bill);
        await h.Db.DocumentCaptures.Where(c => c.Id == scanId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.FieldsJson, "{\"vendorName\":\"Invented Paper Co\"}")
            .SetProperty(c => c.AcceptedFieldsJson, "{\"vendorName\":\"Invented Paper Co\"}")
            .SetProperty(c => c.RawText, "INVENTED PAPER CO total 12.00")
            .SetProperty(c => c.WarningsJson, "[\"w\"]"));
        h.Db.DocumentCaptureAiMappings.Add(new DocumentCaptureAiMapping
        {
            CompanyId = h.CompanyId, CaptureId = scanId, ExtractionGeneration = 1, Status = "done", Kind = "purchase-invoice",
            ContentHash = new string('a', 64), RulesVersion = "r1", MappedFieldsJson = "{\"vendorName\":\"Invented Paper Co\"}",
            CreatedAt = DateTime.UtcNow
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await DocumentLibraryService.RemoveLinksForDeletedTargetAsync(h.Db, h.CompanyId, "bill", bill.Id, default);
        await h.Library.TrashAsync(scanId, default);
        await h.Library.DeletePermanentlyAsync(scanId, default);

        Assert.False(await h.Db.DocumentCaptures.AnyAsync(c => c.Id == scanId));
        Assert.False(await h.Db.DocumentCaptureBlobs.AnyAsync(b => b.CaptureId == scanId));
        Assert.False(await h.Db.DocumentCaptureAiMappings.AnyAsync(m => m.CaptureId == scanId));
        Assert.False(await h.Db.DocumentCaptureLinks.AnyAsync(l => l.CaptureId == scanId));
    }

    [Fact]
    public async Task LibraryUploadNeedsViewAsWellAsManage_AndNothingIsStoredOnDenial()
    {
        await using var h = await Harness.CreateAsync();
        h.Tenant.SetPermissions(SystemPermissions.DocumentsManage);
        var denied = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            h.Library.UploadAsync(CreatePdf(1), "application/pdf", "a.pdf", Meta(), null, null, null, false, default));
        Assert.Equal(403, denied.StatusCode);
        Assert.False(await h.Db.DocumentCaptures.AnyAsync());
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(h.Db, h.CompanyId));
    }

    // ---------- helpers ----------

    private static DocumentMetadataInput Meta(string? title = null, string? category = null, string? notes = null) =>
        new(title, category, null, notes);

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] CreatePdf(int pages, string label = "x")
    {
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = label;
        for (var i = 0; i < pages; i++)
            builder.AddPage(100, 100);
        return builder.Build();
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] CreateNoisePng(int width, int height)
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _dbPath;
        public ApiDbContext Db { get; }
        public TestTenant Tenant { get; }
        public DocumentLibraryService Library { get; }
        public Mock<ISecurityAuditService> Audit { get; }
        public int CompanyId { get; }
        public DocumentStoragePolicy Policy { get; set; }
        private readonly int _itemId;
        private readonly int _measurementId;

        private Harness(string dbPath, ApiDbContext db, TestTenant tenant, Mock<ISecurityAuditService> audit, int companyId,
            int itemId, int measurementId, DocumentStoragePolicy policy, bool enabled, int maxUploadsPerHour)
        {
            _dbPath = dbPath;
            Db = db;
            Tenant = tenant;
            Audit = audit;
            CompanyId = companyId;
            _itemId = itemId;
            _measurementId = measurementId;
            Policy = policy;

            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.Get(It.IsAny<int>())).Returns(new EntitlementSnapshot(true, "community", "community", false,
                [], [], [], null, new HashSet<string>(StringComparer.Ordinal) { FeatureKeys.DocumentCapture }, "community"));
            entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>())).Returns(new EntitlementDecision(true, null, null));
            entitlements.Setup(e => e.GetDocumentStoragePolicy(It.IsAny<int>())).Returns(() => Policy);

            var publicIds = new Mock<IPublicIdResolver>();
            publicIds.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? publicId, string prefix, CancellationToken _) =>
                {
                    if (string.IsNullOrWhiteSpace(publicId) || !publicId.StartsWith(prefix + "_", StringComparison.Ordinal))
                        return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed, prefix);
                    int? found = prefix switch
                    {
                        "bill" => db.PurchaseInvoiceHeaders.AsNoTracking().Where(b => b.PublicId == publicId).Select(b => (int?)b.Id).SingleOrDefault(),
                        "inv" => db.SalesInvoiceHeaders.AsNoTracking().Where(b => b.PublicId == publicId).Select(b => (int?)b.Id).SingleOrDefault(),
                        "cus" => db.Customers.AsNoTracking().Where(b => b.PublicId == publicId).Select(b => (int?)b.Id).SingleOrDefault(),
                        "ven" => db.Vendors.AsNoTracking().Where(b => b.PublicId == publicId).Select(b => (int?)b.Id).SingleOrDefault(),
                        _ => null
                    };
                    return found is > 0 ? PublicIdLookupResult.Success(found.Value) : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, prefix);
                });

            Library = new DocumentLibraryService(db, new DocumentCaptureAdmissionLock(db), entitlements.Object, publicIds.Object, tenant,
                Options.Create(new DocumentsOptions { Enabled = enabled, MaxUploadsPerHour = maxUploadsPerHour }),
                Options.Create(new DocumentCaptureOptions()),
                audit.Object);
        }

        public static async Task<Harness> CreateAsync(DocumentStoragePolicy? policy = null, bool enabled = true, int maxUploadsPerHour = 120)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"accountgo_docs_{Guid.NewGuid():N}.db");
            var tenant = new TestTenant();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options;
            var db = new ApiDbContext(options, tenant) { SuppressTenantSecurityCheck = true };
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
            var company = new Company { Name = "Docs Co", ShortName = "DOCS", CompanyCode = "DOC", Slug = "docs-co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            tenant.CompanyIdValue = company.Id;
            var measurement = new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" };
            var category = new ItemCategory { CompanyId = company.Id, Name = "General", ItemType = ItemTypes.Service };
            db.Measurements.Add(measurement);
            db.ItemCategories.Add(category);
            await db.SaveChangesAsync();
            var item = new Item
            {
                CompanyId = company.Id, Code = "SVC-1", Description = "Service", ItemCategoryId = category.Id,
                SmallestMeasurementId = measurement.Id, Price = 1m, Cost = 1m
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            db.SuppressTenantSecurityCheck = false;
            var audit = new Mock<ISecurityAuditService>();
            return new Harness(dbPath, db, tenant, audit, company.Id, item.Id, measurement.Id,
                policy ?? DocumentStoragePolicy.Community, enabled, maxUploadsPerHour);
        }

        public Task<int> IdOf(string publicId) =>
            Db.DocumentCaptures.AsNoTracking().Where(c => c.PublicId == publicId).Select(c => c.Id).SingleAsync();

        public PurchaseInvoiceHeader SeedBill(string no)
        {
            var bill = new PurchaseInvoiceHeader
            {
                CompanyId = CompanyId, No = no, VendorInvoiceNo = no, Date = new DateTime(2026, 11, 20), Status = PurchaseInvoiceStatus.Draft,
                PurchaseInvoiceLines = [new PurchaseInvoiceLine { CompanyId = CompanyId, ItemId = _itemId, Quantity = 1m, Cost = 10m, Amount = 10m, Description = "Line" }]
            };
            Db.PurchaseInvoiceHeaders.Add(bill);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return bill;
        }

        public SalesInvoiceHeader SeedInvoice(string no)
        {
            var customer = new Customer
            {
                CompanyId = CompanyId, No = $"C-{no}",
                Party = new Party { CompanyId = CompanyId, Name = $"Customer {no}", PartyType = PartyTypes.Customer }
            };
            Db.Customers.Add(customer);
            Db.SaveChanges();
            var invoice = new SalesInvoiceHeader
            {
                CompanyId = CompanyId, CustomerId = customer.Id, No = no, Date = new DateTime(2026, 11, 20), Status = SalesInvoiceStatus.Draft,
                SalesInvoiceLines = [new SalesInvoiceLine { CompanyId = CompanyId, ItemId = _itemId, MeasurementId = _measurementId, Quantity = 1m, Amount = 10m, Description = "Line" }]
            };
            Db.SalesInvoiceHeaders.Add(invoice);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return invoice;
        }

        public async Task<int> SeedFiledScanAsync(PurchaseInvoiceHeader bill)
        {
            var now = DateTime.UtcNow;
            var capture = new Core.Domain.DocumentCapture
            {
                CompanyId = CompanyId, Kind = "purchase-invoice", SchemaVersion = 1, Status = "accepted",
                SourceHash = new string('a', 64), ContentHash = new string('a', 64), NormalizationVersion = "pdf-structure-v1",
                ContentType = "application/pdf", ByteCount = 3, PageCount = 1, IdempotencyKey = $"seed-{Guid.NewGuid():N}",
                RequestFingerprint = new string('b', 64), ExtractionGeneration = 1, CreatedBy = "actor-1", CreatedAt = now,
                ExpiresAt = now, AcceptedAt = now, AcceptedBy = "actor-1", AcceptedFieldsJson = "{}", FiledAt = now, FiledBy = "actor-1",
                Blob = new DocumentCaptureBlob { CompanyId = CompanyId, DocumentBytes = [1, 2, 3] }
            };
            capture.Links.Add(new DocumentCaptureLink
            {
                CompanyId = CompanyId, TargetType = "bill", TargetId = bill.Id, Role = DocumentCaptureLinkRoles.Source,
                LinkedBy = "actor-1", LinkedAt = now
            });
            Db.DocumentCaptures.Add(capture);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return capture.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private sealed class TestTenant : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private HashSet<string> _permissions = new(StringComparer.Ordinal)
        {
            SystemPermissions.DocumentsView, SystemPermissions.DocumentsManage,
            SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate, SystemPermissions.PurchasesVendorsView,
            SystemPermissions.PurchasesVendorsManage, SystemPermissions.SalesInvoicesView, SystemPermissions.SalesInvoicesCreate,
            SystemPermissions.SalesCustomersView, SystemPermissions.SalesCustomersManage
        };
        public int CompanyIdValue { get; set; } = 1;
        public int? CompanyId => CompanyIdValue;
        public string? CompanySlug => "docs-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Member;
        public string? UserId => "actor-1";
        public IReadOnlySet<string> Permissions => _permissions;
        public bool IsResolved => true;
        public int RequireCompanyId() => CompanyIdValue;
        public void SetPermissions(params string[] permissions) => _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }
}
