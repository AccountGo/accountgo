// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
using Dto.DocumentCapture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using Xunit;
using CaptureEntity = Core.Domain.DocumentCapture;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentsLibraryFoundationSliceTests
{
    private static readonly CaptureReviewTargetPolicy BillPolicy = new(
        AllowedKinds: new HashSet<string>(StringComparer.Ordinal) { "expense-receipt", "purchase-invoice" },
        TargetType: "bill",
        InvalidKindDetail: "Invalid kind for bill.",
        StateConflictDetail: "Capture must be accepted.",
        DiscrepancyDetail: "Totals differ.");

    [Fact]
    public async Task MultiLink_SourceAndAttachmentLinks_BehaveAsBeforeForSourceAndFilterLinksByCallerPermissions()
    {
        await using var harness = await SliceHarness.CreateAsync();

        var billA = harness.SeedBill("BILL-A", 120.00m);
        var billB = harness.SeedBill("BILL-B", 999.00m);
        var invoiceC = harness.SeedSalesInvoice("INV-C", 500.00m);

        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(1), "sa1-multilink", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();

        const string acceptedFields = """{"date":"2026-11-20","vendorName":"Northwind Paper","sourceTotals":{"subtotal":"100.00","taxTotal":"20.00","grandTotal":"120.00"}}""";
        await harness.Service.AcceptAsync(captureId, acceptedFields, CancellationToken.None);

        // Link capture as 'source' to bill A via LinkAsync -> files capture and bumps revision from 0 to 1
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));
        var linkedView = await harness.Service.LinkAsync(captureId, "bill", billA.PublicId, CancellationToken.None);
        Assert.Equal(1L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        var persistedCapture = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == captureId);
        Assert.NotNull(persistedCapture.FiledAt);
        Assert.Equal("actor-1", persistedCapture.FiledBy);

        // Direct DB insert: add 'attachment' links to bill B and invoice C
        harness.Db.DocumentCaptureLinks.AddRange(
            new DocumentCaptureLink
            {
                CompanyId = harness.CompanyId,
                CaptureId = captureId,
                TargetType = "bill",
                TargetId = billB.Id,
                Role = DocumentCaptureLinkRoles.Attachment,
                LinkedBy = "actor-1",
                LinkedAt = DateTime.UtcNow.AddMinutes(1)
            },
            new DocumentCaptureLink
            {
                CompanyId = harness.CompanyId,
                CaptureId = captureId,
                TargetType = "invoice",
                TargetId = invoiceC.Id,
                Role = DocumentCaptureLinkRoles.Attachment,
                LinkedBy = "actor-1",
                LinkedAt = DateTime.UtcNow.AddMinutes(2)
            });
        await harness.Db.SaveChangesAsync();
        harness.Db.ChangeTracker.Clear();

        // Full permissions: Link = bill A (source), Links = [bill A (source), bill B (attachment), invoice C (attachment)]
        var fullView = await harness.Service.GetAsync(captureId, CancellationToken.None);
        Assert.NotNull(fullView.Link);
        Assert.Equal("bill", fullView.Link!.TargetType);
        Assert.Equal(billA.PublicId, fullView.Link.TargetId);
        Assert.Equal("source", fullView.Link.Role);
        Assert.Equal(3, fullView.Links.Count);
        Assert.Equal((billA.PublicId, "source"), (fullView.Links[0].TargetId, fullView.Links[0].Role));
        Assert.Equal((billB.PublicId, "attachment"), (fullView.Links[1].TargetId, fullView.Links[1].Role));
        Assert.Equal((invoiceC.PublicId, "attachment"), (fullView.Links[2].TargetId, fullView.Links[2].Role));

        // Bytes remain accessible
        var bytesResult = await harness.Service.GetBytesAsync(captureId, CancellationToken.None);
        Assert.NotEmpty(bytesResult.Bytes);

        // GetLinked(bill A) returns the capture; GetLinked(bill B) ignores the attachment link
        var linkedToA = await harness.Service.GetLinkedAsync("bill", billA.PublicId, CancellationToken.None);
        var singleLinkedA = Assert.Single(linkedToA);
        Assert.Equal(uploaded.Id, singleLinkedA.Id);
        Assert.Equal(billA.PublicId, singleLinkedA.Link!.TargetId);
        Assert.Equal(3, singleLinkedA.Links.Count);

        var linkedToB = await harness.Service.GetLinkedAsync("bill", billB.PublicId, CancellationToken.None);
        Assert.Empty(linkedToB);

        // Permission filtering on Links: remove SalesInvoicesView -> invoice C omitted from Links, no 404
        harness.Tenant.SetPermissions(
            SystemPermissions.PurchasesBillsView,
            SystemPermissions.PurchasesBillsCreate,
            SystemPermissions.PurchasesVendorsView);
        var billsOnlyView = await harness.Service.GetAsync(captureId, CancellationToken.None);
        Assert.Equal(billA.PublicId, billsOnlyView.Link!.TargetId);
        Assert.Equal(2, billsOnlyView.Links.Count);
        Assert.DoesNotContain(billsOnlyView.Links, l => l.TargetType == "invoice");

        // TaxTreatment refresh applies to bill A (source) and ignores bill B (attachment)
        CaptureTaxTreatment.RefreshForTarget(harness.Db, harness.Tenant, "bill", billA.Id);
        CaptureTaxTreatment.RefreshForTarget(harness.Db, harness.Tenant, "bill", billB.Id);
        var linkARow = await harness.Db.DocumentCaptureLinks.AsNoTracking()
            .SingleAsync(l => l.CaptureId == captureId && l.TargetType == "bill" && l.TargetId == billA.Id);
        var linkBRow = await harness.Db.DocumentCaptureLinks.AsNoTracking()
            .SingleAsync(l => l.CaptureId == captureId && l.TargetType == "bill" && l.TargetId == billB.Id);
        Assert.Equal(CaptureTaxTreatment.InCost, linkARow.TaxTreatment);
        Assert.Null(linkBRow.TaxTreatment);

        // CaptureReviewReconciler checks source bill A (120 == 120 -> ok; 150 != 120 -> requires review) and ignores attachment bill B (999)
        Assert.Null(CaptureReviewReconciler.ValidateForDraft(
            harness.Db, harness.Tenant, BillPolicy, billA.Id, [new CaptureReviewContext { CaptureId = captureId }]));
        var commitAOk = CaptureReviewReconciler.ValidateForCommit(
            harness.Db, harness.Tenant, BillPolicy, billA.Id, 120.00m, null);
        Assert.True(commitAOk.Succeeded);

        var commitAMismatch = CaptureReviewReconciler.ValidateForCommit(
            harness.Db, harness.Tenant, BillPolicy, billA.Id, 150.00m, null);
        Assert.False(commitAMismatch.Succeeded);

        var commitBIgnored = CaptureReviewReconciler.ValidateForCommit(
            harness.Db, harness.Tenant, BillPolicy, billB.Id, 999.00m, null);
        Assert.True(commitBIgnored.Succeeded);

        // Idempotent replay of LinkAsync to bill A does not bump revision; linking to bill B conflicts
        await harness.Service.LinkAsync(captureId, "bill", billA.PublicId, CancellationToken.None);
        Assert.Equal(1L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        var conflict = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.LinkAsync(captureId, "bill", billB.PublicId, CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("DOCUMENT_LINK_CONFLICT", conflict.ErrorCode);
    }

    [Fact]
    public async Task MatchParty_EnforcesSourceTargetReadabilityAndIgnoresUnreadableAttachmentLinks()
    {
        await using var harness = await SliceHarness.CreateAsync();

        var vendor = new Vendor
        {
            CompanyId = harness.CompanyId,
            No = "VEND-1",
            Party = new Party { CompanyId = harness.CompanyId, Name = "Fixture Vendor", PartyType = PartyTypes.Vendor }
        };
        harness.Db.Vendors.Add(vendor);
        await harness.Db.SaveChangesAsync();

        var billA = harness.SeedBill("BILL-MP-A", 100.00m);
        var invoiceC = harness.SeedSalesInvoice("INV-MP-C", 250.00m);
        var invoiceD = harness.SeedSalesInvoice("INV-MP-D", 300.00m);

        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(11), "mp-r3", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, """{"date":"2026-11-20","vendorName":"Fixture Vendor"}""", CancellationToken.None);

        // Link source to billA and add an unreadable attachment link to invoiceC
        await harness.Service.LinkAsync(captureId, "bill", billA.PublicId, CancellationToken.None);
        harness.Db.DocumentCaptureLinks.Add(new DocumentCaptureLink
        {
            CompanyId = harness.CompanyId,
            CaptureId = captureId,
            TargetType = "invoice",
            TargetId = invoiceC.Id,
            Role = DocumentCaptureLinkRoles.Attachment,
            LinkedBy = "actor-1",
            LinkedAt = DateTime.UtcNow.AddSeconds(5)
        });
        await harness.Db.SaveChangesAsync();
        harness.Db.ChangeTracker.Clear();

        // User with bill-capture + vendor rights (no SalesInvoicesView) can still match-party when source billA is readable
        harness.Tenant.SetPermissions(
            SystemPermissions.PurchasesBillsView,
            SystemPermissions.PurchasesBillsCreate,
            SystemPermissions.PurchasesVendorsView);
        var matchesWithReadableSource = await harness.Service.MatchPartyAsync("vendor", captureId, CancellationToken.None);
        Assert.NotEmpty(matchesWithReadableSource);

        // User has bill-capture rights (PurchasesBillsView + PurchasesVendorsView), but the source target is an invoice
        // they lack view permission on -> 404 DOCUMENT_NOT_FOUND (R3: kind policy AND source target readable)
        await harness.Db.DocumentCaptureLinks
            .Where(l => l.CompanyId == harness.CompanyId && l.CaptureId == captureId && l.Role == DocumentCaptureLinkRoles.Source)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.TargetType, "invoice").SetProperty(l => l.TargetId, invoiceD.Id));
        var missingInvoicePerm = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.MatchPartyAsync("vendor", captureId, CancellationToken.None));
        Assert.Equal(404, missingInvoicePerm.StatusCode);
        Assert.Equal("DOCUMENT_NOT_FOUND", missingInvoicePerm.ErrorCode);

        // Likewise, if the source link points to a bill that is no longer readable/present in the workspace -> 404 DOCUMENT_NOT_FOUND
        await harness.Db.DocumentCaptureLinks
            .Where(l => l.CompanyId == harness.CompanyId && l.CaptureId == captureId && l.Role == DocumentCaptureLinkRoles.Source)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.TargetType, "bill").SetProperty(l => l.TargetId, 999_999));
        var missingBill = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.MatchPartyAsync("vendor", captureId, CancellationToken.None));
        Assert.Equal(404, missingBill.StatusCode);
        Assert.Equal("DOCUMENT_NOT_FOUND", missingBill.ErrorCode);
    }

    [Fact]
    public async Task LinkAsync_WhenNonSourceLinkExistsForSameTarget_Returns409Conflict_AndAllowsSourceLinkToDifferentTarget()
    {
        await using var harness = await SliceHarness.CreateAsync();
        var billSame = harness.SeedBill("BILL-SAME", 75.00m);
        var billOther = harness.SeedBill("BILL-OTHER", 75.00m);

        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(12), "link-a2", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        await harness.Service.AcceptAsync(captureId, """{"date":"2026-11-20","vendorName":"Conflict Vendor"}""", CancellationToken.None);

        // Pre-seed an attachment link to billSame (no source link exists yet)
        harness.Db.DocumentCaptureLinks.Add(new DocumentCaptureLink
        {
            CompanyId = harness.CompanyId,
            CaptureId = captureId,
            TargetType = "bill",
            TargetId = billSame.Id,
            Role = DocumentCaptureLinkRoles.Attachment,
            LinkedBy = "actor-1",
            LinkedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();
        harness.Db.ChangeTracker.Clear();

        // Attempting to source-link the capture to billSame returns 409 DOCUMENT_LINK_CONFLICT and keeps the attachment row untouched
        var sameTargetConflict = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            harness.Service.LinkAsync(captureId, "bill", billSame.PublicId, CancellationToken.None));
        Assert.Equal(409, sameTargetConflict.StatusCode);
        Assert.Equal("DOCUMENT_LINK_CONFLICT", sameTargetConflict.ErrorCode);
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        // Source-linking to a different target (billOther) succeeds, files the capture, and bumps revision
        var linked = await harness.Service.LinkAsync(captureId, "bill", billOther.PublicId, CancellationToken.None);
        Assert.Equal(billOther.PublicId, linked.Link?.TargetId);
        Assert.Equal(2, linked.Links.Count);
        Assert.Equal(1L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));
    }

    [Fact]
    public async Task Retention_NeverPurgesFiledCapture_AndBarrierRaceBetweenCandidateSelectionAndFilingPreservesBytes()
    {
        await using var harness = await SliceHarness.CreateAsync();
        var now = DateTime.UtcNow;

        // 1. A filed capture past ExpiresAt (even without AcceptedAt) is never selected or purged by retention.
        var filedExpired = new CaptureEntity
        {
            CompanyId = harness.CompanyId,
            PublicId = "cap_01filedexpired0000000001",
            Kind = "purchase-invoice",
            SchemaVersion = 1,
            Status = "ready",
            SourceHash = new string('1', 64),
            ContentHash = new string('2', 64),
            NormalizationVersion = "image-normalization-v2",
            ContentType = "image/png",
            ByteCount = 3,
            PageCount = 1,
            RequestFingerprint = new string('3', 64),
            ExtractionGeneration = 1,
            FieldsJson = """{"vendorName":"Kept"}""",
            RawText = "kept text",
            CreatedBy = "actor-1",
            CreatedAt = now.AddDays(-10),
            ExpiresAt = now.AddDays(-9),
            FiledAt = now.AddDays(-9),
            FiledBy = "actor-1"
        };
        harness.Db.DocumentCaptures.Add(filedExpired);
        await harness.Db.SaveChangesAsync();
        harness.Db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob
        {
            CompanyId = harness.CompanyId,
            CaptureId = filedExpired.Id,
            DocumentBytes = [10, 20, 30]
        });
        await harness.Db.SaveChangesAsync();

        var plainRetention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));
        await plainRetention.PurgeAsync(now, CancellationToken.None);

        Assert.True(await harness.Db.DocumentCaptureBlobs.AsNoTracking().AnyAsync(b => b.CaptureId == filedExpired.Id));
        Assert.Null((await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == filedExpired.Id)).BytesDeletedAt);

        // 2. Barrier test: candidate is unaccepted + expired when retention selects candidates, then AcceptAsync + LinkAsync
        // files it right BEFORE retention's per-item PurgeAbandonedContentAsync acquires the company lock.
        var bill = harness.SeedBill("BILL-RACE", 50.00m);
        var uploaded = await harness.Service.UploadAsync("purchase-invoice", 1, "image/png", CreatePng(2), "race-key", CancellationToken.None);
        var raceCaptureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();

        // Force ExpiresAt into the past so retention's candidate query picks it up as abandoned
        await harness.Db.DocumentCaptures.Where(c => c.Id == raceCaptureId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now.AddHours(-2)));

        var candidateSelectedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var filingFinishedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var retentionDb = harness.CreateSecondDbContext();
        var barrierLock = new BarrierAdmissionLock(
            new DocumentCaptureAdmissionLock(retentionDb),
            onBeforeAcquireCompany: async () =>
            {
                candidateSelectedGate.TrySetResult();
                await filingFinishedGate.Task.ConfigureAwait(false);
            });
        var racingRetention = new DocumentCaptureRetentionService(retentionDb, barrierLock);

        var purgeTask = racingRetention.PurgeAsync(now, CancellationToken.None);

        // Wait until retention has selected raceCaptureId as a purge candidate and reached AcquireCompanyAsync
        await candidateSelectedGate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Now file the capture via AcceptAsync + LinkAsync on the main context before releasing retention
        await harness.Service.AcceptAsync(raceCaptureId, """{"date":"2026-11-20","vendorName":"Saved By Link"}""", CancellationToken.None);
        await harness.Service.LinkAsync(raceCaptureId, "bill", bill.PublicId, CancellationToken.None);
        filingFinishedGate.TrySetResult();

        await purgeTask.WaitAsync(TimeSpan.FromSeconds(10));

        var survived = await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == raceCaptureId);
        Assert.NotNull(survived.FiledAt);
        Assert.Null(survived.BytesDeletedAt);
        Assert.Null(survived.FieldsWipedAt);
        Assert.True(await harness.Db.DocumentCaptureBlobs.AsNoTracking().AnyAsync(b => b.CaptureId == raceCaptureId));

        // 3. Second barrier check: even if ONLY FiledAt is set (AcceptedAt still null) right before per-item purge,
        // the conditional FiledAt == null recheck prevents purging.
        var storedLike = new CaptureEntity
        {
            CompanyId = harness.CompanyId,
            PublicId = "cap_01racefiledonly000000002",
            Kind = "expense-receipt",
            SchemaVersion = 1,
            Status = "ready",
            SourceHash = new string('4', 64),
            ContentHash = new string('5', 64),
            NormalizationVersion = "image-normalization-v2",
            ContentType = "image/png",
            ByteCount = 3,
            PageCount = 1,
            RequestFingerprint = new string('6', 64),
            ExtractionGeneration = 1,
            FieldsJson = """{"vendorName":"FiledOnly"}""",
            CreatedBy = "actor-1",
            CreatedAt = now.AddDays(-2),
            ExpiresAt = now.AddHours(-3)
        };
        harness.Db.DocumentCaptures.Add(storedLike);
        await harness.Db.SaveChangesAsync();
        harness.Db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob
        {
            CompanyId = harness.CompanyId,
            CaptureId = storedLike.Id,
            DocumentBytes = [7, 8, 9]
        });
        await harness.Db.SaveChangesAsync();

        var gate2Selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate2Proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var retentionDb2 = harness.CreateSecondDbContext();
        var barrierLock2 = new BarrierAdmissionLock(
            new DocumentCaptureAdmissionLock(retentionDb2),
            onBeforeAcquireCompany: async () =>
            {
                gate2Selected.TrySetResult();
                await gate2Proceed.Task.ConfigureAwait(false);
            });
        var purgeTask2 = new DocumentCaptureRetentionService(retentionDb2, barrierLock2).PurgeAsync(now, CancellationToken.None);
        await gate2Selected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await harness.Db.DocumentCaptures.Where(c => c.Id == storedLike.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.FiledAt, now).SetProperty(c => c.FiledBy, "actor-1"));
        gate2Proceed.TrySetResult();
        await purgeTask2.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await harness.Db.DocumentCaptureBlobs.AsNoTracking().AnyAsync(b => b.CaptureId == storedLike.Id));
        Assert.Null((await harness.Db.DocumentCaptures.AsNoTracking().SingleAsync(c => c.Id == storedLike.Id)).BytesDeletedAt);
    }

    [Fact]
    public async Task DocumentRevision_T1Enumeration_AccountsForAllMutatorsAndVerifiesBumpsAndSingleWriter()
    {
        // 1. Enumerate every public instance method on DocumentCaptureService and DocumentCaptureRetentionService
        //    so any new mutator added in S-A2 or later must be classified and tested here.
        var readOnlyMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.GetAsync)}",
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.GetBytesAsync)}",
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.GetLinkedAsync)}",
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.MatchPartyAsync)}"
        };

        var nonExportAffectingMutators = new HashSet<string>(StringComparer.Ordinal)
        {
            // S-A1: only scan/assistant/bank kinds exist and start unfiled; S-A2 reclassifies UploadAsync for 'document' kind.
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.UploadAsync)}",
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.AcceptAsync)}",
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.RejectAsync)}"
        };

        var exportAffectingMutators = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{nameof(DocumentCaptureService)}.{nameof(DocumentCaptureService.LinkAsync)}",
            $"{nameof(DocumentCaptureRetentionService)}.{nameof(DocumentCaptureRetentionService.PurgeAsync)}",
            $"{nameof(DocumentCaptureRetentionService)}.{nameof(DocumentCaptureRetentionService.PurgeDeletedCompanyAsync)}"
        };

        foreach (var serviceType in new[] { typeof(DocumentCaptureService), typeof(DocumentCaptureRetentionService) })
        {
            var methods = serviceType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => $"{serviceType.Name}.{m.Name}")
                .Distinct(StringComparer.Ordinal);

            foreach (var signature in methods)
            {
                Assert.True(
                    readOnlyMethods.Contains(signature)
                    || nonExportAffectingMutators.Contains(signature)
                    || exportAffectingMutators.Contains(signature),
                    $"Unclassified method '{signature}' must be added to the T1 revision enumeration test.");
            }
        }

        // 2. Verify DocumentRevision.cs is the sole writer to DocumentLibraryRevisions across src/
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var srcDir = Path.Combine(solutionRoot, "src");
        var allowedReferenceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DocumentCapture.cs",
            "ApiDbContext.DocumentCaptures.cs",
            "SqliteSchemaUpgrader.cs",
            "DocumentRevision.cs"
        };
        var unexpectedWriters = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => !allowedReferenceFiles.Contains(Path.GetFileName(f)))
            .Where(f => File.ReadAllText(f).Contains("DocumentLibraryRevision", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(unexpectedWriters);

        // 3. Behavioral verification: non-export mutators keep revision 0; LinkAsync and filed-row retention purge bump it.
        await using var harness = await SliceHarness.CreateAsync();
        var bill = harness.SeedBill("BILL-REV", 80.00m);

        var uploaded = await harness.Service.UploadAsync("expense-receipt", 1, "image/png", CreatePng(3), "rev-1", CancellationToken.None);
        var captureId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == uploaded.Id).Select(c => c.Id).SingleAsync();
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        await harness.Service.AcceptAsync(captureId, """{"date":"2026-11-20","vendorName":"Cafe"}""", CancellationToken.None);
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        var rejectedUpload = await harness.Service.UploadAsync("expense-receipt", 1, "image/png", CreatePng(4), "rev-2", CancellationToken.None);
        var rejectedId = await harness.Db.DocumentCaptures.Where(c => c.PublicId == rejectedUpload.Id).Select(c => c.Id).SingleAsync();
        await harness.Service.RejectAsync(rejectedId, CancellationToken.None);
        Assert.Equal(0L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        // LinkAsync files captureId and bumps revision 0 -> 1
        await harness.Service.LinkAsync(captureId, "bill", bill.PublicId, CancellationToken.None);
        Assert.Equal(1L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));

        // Soft-deleting the workspace and purging its filed rows bumps revision 1 -> 2
        var company = await harness.Db.Companies.SingleAsync(c => c.Id == harness.CompanyId);
        company.DeletedAt = DateTime.UtcNow;
        await harness.Db.SaveChangesAsync();

        var retention = new DocumentCaptureRetentionService(harness.Db, new DocumentCaptureAdmissionLock(harness.Db));
        var purged = await retention.PurgeAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.True(purged >= 1);
        Assert.Equal(2L, await DocumentRevision.GetCurrentAsync(harness.Db, harness.CompanyId));
    }

    private static byte[] CreatePng(byte seed)
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(new SKColor(seed, (byte)(seed + 40), (byte)(seed + 80)));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private sealed class BarrierAdmissionLock(
        IDocumentCaptureAdmissionLock inner,
        Func<Task> onBeforeAcquireCompany) : IDocumentCaptureAdmissionLock
    {
        private int _triggered;

        public async Task<IDbContextTransaction> AcquireCompanyAsync(int companyId, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _triggered, 1) == 0)
                await onBeforeAcquireCompany().ConfigureAwait(false);
            return await inner.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        }

        public Task<IDbContextTransaction> AcquirePurgeCompanyAsync(int companyId, CancellationToken cancellationToken) =>
            inner.AcquirePurgeCompanyAsync(companyId, cancellationToken);

        public Task<IAsyncDisposable?> TryAcquirePurgeWorkerSingletonAsync(CancellationToken cancellationToken) =>
            inner.TryAcquirePurgeWorkerSingletonAsync(cancellationToken);
    }

    private sealed class SliceHarness : IAsyncDisposable
    {
        private readonly string _dbPath;
        public ApiDbContext Db { get; }
        public MutableTenantContext Tenant { get; }
        public DocumentCaptureService Service { get; }
        public int CompanyId { get; }
        public int DefaultItemId { get; }
        public int DefaultMeasurementId { get; }
        private readonly DbContextOptions<ApiDbContext> _dbOptions;

        private SliceHarness(string dbPath, DbContextOptions<ApiDbContext> dbOptions, ApiDbContext db,
            MutableTenantContext tenant, DocumentCaptureService service, int companyId, int defaultItemId, int defaultMeasurementId)
        {
            _dbPath = dbPath;
            _dbOptions = dbOptions;
            Db = db;
            Tenant = tenant;
            Service = service;
            CompanyId = companyId;
            DefaultItemId = defaultItemId;
            DefaultMeasurementId = defaultMeasurementId;
        }

        public static async Task<SliceHarness> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"accountgo_sa1_slice_{Guid.NewGuid():N}.db");
            var tenant = new MutableTenantContext(1);
            var dbOptions = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;
            var db = new ApiDbContext(dbOptions, tenant) { SuppressTenantSecurityCheck = true };
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");

            var company = new Company { Name = "Slice Co", ShortName = "SLICE", CompanyCode = "SLC", Slug = "slice-co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            tenant.SetCompanyId(company.Id);

            var measurement = new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" };
            var category = new ItemCategory { CompanyId = company.Id, Name = "General", ItemType = ItemTypes.Service };
            db.Measurements.Add(measurement);
            db.ItemCategories.Add(category);
            await db.SaveChangesAsync();

            var item = new Item
            {
                CompanyId = company.Id,
                Code = "SVC-1",
                Description = "General Service",
                ItemCategoryId = category.Id,
                SmallestMeasurementId = measurement.Id,
                Price = 100m,
                Cost = 100m
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            db.SuppressTenantSecurityCheck = false;

            var snapshot = new EntitlementSnapshot(
                true,
                "community",
                "community",
                false,
                [],
                [],
                [],
                null,
                new HashSet<string>(StringComparer.Ordinal) { FeatureKeys.DocumentCapture },
                "community");
            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.Get(It.IsAny<int>())).Returns(snapshot);
            entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
                .Returns(new EntitlementDecision(true, null, null));

            var options = Options.Create(new DocumentCaptureOptions
            {
                Enabled = true,
                ExtractionHost = true,
                Extractor = "Fake",
                FakeFixtures = new Dictionary<string, string>
                {
                    ["purchase-invoice"] = """{"date":"2026-11-20","vendorName":"Fixture Vendor"}""",
                    ["expense-receipt"] = """{"date":"2026-11-20","vendorName":"Fixture Cafe"}""",
                    ["sales-invoice"] = """{"date":"2026-11-20","customerName":"Fixture Customer"}"""
                }
            });

            var extractor = new FakeExtractor();
            var publicIds = new Mock<IPublicIdResolver>();
            publicIds.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? publicId, string expectedPrefix, CancellationToken _) =>
                {
                    if (string.IsNullOrWhiteSpace(publicId) || !publicId.StartsWith(expectedPrefix + "_", StringComparison.Ordinal))
                        return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed, expectedPrefix);
                    int? found = expectedPrefix switch
                    {
                        "bill" => db.PurchaseInvoiceHeaders.AsNoTracking()
                            .Where(b => b.CompanyId == tenant.RequireCompanyId() && b.PublicId == publicId)
                            .Select(b => (int?)b.Id)
                            .SingleOrDefault(),
                        "inv" => db.SalesInvoiceHeaders.AsNoTracking()
                            .Where(i => i.CompanyId == tenant.RequireCompanyId() && i.PublicId == publicId)
                            .Select(i => (int?)i.Id)
                            .SingleOrDefault(),
                        _ => null
                    };
                    return found is > 0
                        ? PublicIdLookupResult.Success(found.Value)
                        : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, expectedPrefix);
                });
            var admissionLock = new DocumentCaptureAdmissionLock(db);
            var service = new DocumentCaptureService(
                db, admissionLock, extractor, entitlements.Object, publicIds.Object, tenant, options,
                NullLogger<DocumentCaptureService>.Instance);

            return new SliceHarness(dbPath, dbOptions, db, tenant, service, company.Id, item.Id, measurement.Id);
        }

        public ApiDbContext CreateSecondDbContext() => new(_dbOptions, Tenant);

        public PurchaseInvoiceHeader SeedBill(string no, decimal amount)
        {
            var bill = new PurchaseInvoiceHeader
            {
                CompanyId = CompanyId,
                No = no,
                VendorInvoiceNo = no,
                Date = new DateTime(2026, 11, 20),
                Status = PurchaseInvoiceStatus.Draft,
                PurchaseInvoiceLines =
                [
                    new PurchaseInvoiceLine
                    {
                        CompanyId = CompanyId,
                        ItemId = DefaultItemId,
                        Quantity = 1m,
                        Cost = amount,
                        Amount = amount,
                        Description = "Line"
                    }
                ]
            };
            Db.PurchaseInvoiceHeaders.Add(bill);
            Db.SaveChanges();
            return bill;
        }

        public SalesInvoiceHeader SeedSalesInvoice(string no, decimal amount)
        {
            var customer = new Customer
            {
                CompanyId = CompanyId,
                No = $"CUST-{no}",
                Party = new Party { CompanyId = CompanyId, Name = $"Customer {no}", PartyType = PartyTypes.Customer }
            };
            Db.Customers.Add(customer);
            Db.SaveChanges();

            var invoice = new SalesInvoiceHeader
            {
                CompanyId = CompanyId,
                CustomerId = customer.Id,
                No = no,
                Date = new DateTime(2026, 11, 20),
                Status = SalesInvoiceStatus.Draft,
                SalesInvoiceLines =
                [
                    new SalesInvoiceLine
                    {
                        CompanyId = CompanyId,
                        ItemId = DefaultItemId,
                        MeasurementId = DefaultMeasurementId,
                        Quantity = 1m,
                        Amount = amount,
                        Description = "Service"
                    }
                ]
            };
            Db.SalesInvoiceHeaders.Add(invoice);
            Db.SaveChanges();
            return invoice;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
        }
    }

    private sealed class FakeExtractor : IDocumentFieldExtractor
    {
        public bool IsConfigured => true;
        public string ProviderName => "fake";
        public Task<DocumentExtractionResult> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentExtractionResult(
                request.Kind == "sales-invoice"
                    ? """{"date":"2026-11-20","customerName":"Fixture Customer"}"""
                    : """{"date":"2026-11-20","vendorName":"Fixture Vendor"}""",
                request.Kind, 1, Array.Empty<string>(), "raw text", "fake", "1"));
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private int _companyId;
        private HashSet<string> _permissions;

        public MutableTenantContext(int companyId)
        {
            _companyId = companyId;
            _permissions = new HashSet<string>(StringComparer.Ordinal)
            {
                SystemPermissions.PurchasesBillsView,
                SystemPermissions.PurchasesBillsCreate,
                SystemPermissions.PurchasesVendorsView,
                SystemPermissions.SalesInvoicesView,
                SystemPermissions.SalesInvoicesCreate,
                SystemPermissions.SalesCustomersView
            };
        }

        public int? CompanyId => _companyId;
        public string? CompanySlug => "slice-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "actor-1";
        public IReadOnlySet<string> Permissions => _permissions;
        public bool IsResolved => true;
        public int RequireCompanyId() => _companyId;
        public void SetCompanyId(int companyId) => _companyId = companyId;
        public void SetPermissions(params string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }
}
