// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AccountGo.BackendTests.DocumentCapture;
using Api.Data;
using Api.DocumentCaptures;
using Api.Service;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using CaptureEntity = Core.Domain.DocumentCapture;

namespace AccountGo.BackendTests.Documents;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentExportServiceTests
{
    [Fact]
    public async Task Export_IncludesEveryFiledFile_IndexLast_AndFinishesOnlyWhenAllVerified()
    {
        await using var h = await Harness.CreateAsync();
        var active = await h.SeedAsync("document", "Office lease", "contract");
        var archived = await h.SeedAsync("document", "Old lease", "contract", archived: true);
        var trashed = await h.SeedAsync("document", "=HYPERLINK(\"x\")", null, trashed: true);
        var scan = await h.SeedAsync("purchase-invoice", null, null);
        await h.SeedAsync("assistant-attachment", "chat", null);
        await h.SeedAsync("document", "unfiled", null, filed: false);

        var run = await h.Exports.CreateAsync(default);
        Assert.Equal(("ready", 4), (run.Status, run.SelectedCount));
        var (zipBytes, context) = await h.StreamAsync(run.Id);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/zip", context.Response.ContentType);

        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal("index.csv", names[^1]);
        Assert.Contains($"contract/undated_Office_lease_{active.PublicId}.pdf", names);
        Assert.Contains($"contract/undated_Old_lease_{archived.PublicId}.pdf", names);
        Assert.Contains(names, n => n.StartsWith("_trash/uncategorised/", StringComparison.Ordinal) && n.EndsWith($"{trashed.PublicId}.pdf", StringComparison.Ordinal));
        Assert.Contains($"scans/undated_Scanned_bill_{scan.PublicId}.pdf", names);
        Assert.Equal(5, names.Count);
        foreach (var entry in zip.Entries.Where(e => e.FullName != "index.csv"))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            Assert.True(CompressionLevelNone(entry));
            Assert.Contains(Sha(buffer.ToArray()), ReadIndex(zip));
        }

        var index = ReadIndex(zip);
        Assert.EndsWith($"# complete,4,4,{run.Id}\n", index, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", index, StringComparison.Ordinal);
        Assert.Contains(",trash,", index, StringComparison.Ordinal);
        Assert.Contains(",archived,", index, StringComparison.Ordinal);

        var finished = await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default);
        Assert.Equal(("server_finished", 4, true), (finished.Status, finished.WrittenCount, finished.Current));

        // A document change after the selection makes the run stale for the delete check.
        await DocumentRevision.BumpAsync(h.Db, h.CompanyId);
        Assert.False((await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default)).Current);
    }

    [Fact]
    public async Task Export_IgnoresTheCallersOperationalPermissions()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("purchase-invoice", null, null);
        await h.SeedAsync("sales-invoice", null, null);
        h.Tenant.Perms = new HashSet<string>(StringComparer.Ordinal);
        var run = await h.Exports.CreateAsync(default);
        Assert.Equal(2, run.SelectedCount);
    }

    [Fact]
    public async Task MissingOrAlteredFiles_MarkTheIndexIncomplete_AndTheRunFailed()
    {
        await using var h = await Harness.CreateAsync();
        var gone = await h.SeedAsync("document", "gone", null);
        var altered = await h.SeedAsync("document", "altered", null);
        var run = await h.Exports.CreateAsync(default);
        await h.Db.DocumentCaptureBlobs.Where(b => b.CaptureId == gone.Id).ExecuteDeleteAsync();
        await h.Db.DocumentCaptureBlobs.Where(b => b.CaptureId == altered.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.DocumentBytes, new byte[] { 9, 9, 9 }));

        var (zipBytes, _) = await h.StreamAsync(run.Id);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var index = ReadIndex(zip);
        Assert.Contains(",missing,", index, StringComparison.Ordinal);
        Assert.Contains(",hash_mismatch,", index, StringComparison.Ordinal);
        Assert.Contains("# incomplete,missing-files,0/2", index, StringComparison.Ordinal);
        var failed = await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default);
        Assert.Equal(("failed", "missing-files", false), (failed.Status, failed.FailureReason, failed.Current));
        h.Audit.Verify(a => a.LogEventAsync("document.export.hash_mismatch", "failure", It.IsAny<string?>(), h.CompanyId, It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task ServerObservedDisconnect_FailsTheRun()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        await h.SeedAsync("document", "b", null);
        var run = await h.Exports.CreateAsync(default);
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Response.Body = new AbortAfterFirstWriteStream(aborted);
        await h.Exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "docs-co", aborted.Token);
        var failed = await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default);
        Assert.Equal(("failed", "client-disconnected"), (failed.Status, failed.FailureReason));
    }

    [Fact]
    public async Task ARunStreamsOnce_ANewRequestSupersedesAnUndownloadedRun_AndOneRunsAtATime()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var first = await h.Exports.CreateAsync(default);
        var second = await h.Exports.CreateAsync(default);
        Assert.Equal(("failed", "superseded"), ((await h.Exports.GetRunAsync(await h.RunIdOf(first.Id), default)).Status,
            (await h.Exports.GetRunAsync(await h.RunIdOf(first.Id), default)).FailureReason));

        await h.StreamAsync(second.Id);
        var again = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.StreamAsync(second.Id));
        Assert.Equal("DOCUMENT_EXPORT_NOT_READY", again.ErrorCode);

        var third = await h.Exports.CreateAsync(default);
        await h.Db.DocumentExportRuns.Where(r => r.PublicId == third.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "running").SetProperty(r => r.StreamStartedAt, DateTime.UtcNow)
                .SetProperty(r => r.LastHeartbeatAt, DateTime.UtcNow));
        var busy = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Exports.CreateAsync(default));
        Assert.Equal("DOCUMENT_EXPORT_RUNNING", busy.ErrorCode);

        // A stream without a heartbeat for longer than the lease is failed as stale, freeing the slot.
        await h.Db.DocumentExportRuns.Where(r => r.PublicId == third.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeatAt, DateTime.UtcNow.AddMinutes(-6)));
        Assert.Equal("ready", (await h.Exports.CreateAsync(default)).Status);
    }

    [Fact]
    public async Task DeleteRefusal_FollowsTheExportContract()
    {
        await using var h = await Harness.CreateAsync();
        Assert.Null(await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, false, false, default));
        await h.SeedAsync("document", "a", null);
        Assert.Equal("DOCUMENT_EXPORT_REQUIRED", await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, false, false, default));
        Assert.Null(await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, false, true, default));

        var run = await h.Exports.CreateAsync(default);
        Assert.Equal("DOCUMENT_EXPORT_REQUIRED", await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, true, false, default));
        await h.StreamAsync(run.Id);
        Assert.Equal("DOCUMENT_EXPORT_CONFIRMATION_REQUIRED", await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, false, false, default));
        Assert.Null(await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, true, false, default));
        await DocumentRevision.BumpAsync(h.Db, h.CompanyId);
        Assert.Equal("DOCUMENT_EXPORT_STALE", await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, true, false, default));
    }

    [Fact]
    public async Task UnexpectedErrorAfterStreamingStarts_FailsTheRun_AndWorkspaceDeleteStaysRefused()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var run = await h.Exports.CreateAsync(default);
        var context = new DefaultHttpContext();
        context.Response.Body = new ThrowingStream();
        await h.Exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);

        var failed = await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default);
        Assert.Equal(("failed", "server-error", false), (failed.Status, failed.FailureReason, failed.Current));
        Assert.Equal("DOCUMENT_EXPORT_REQUIRED", await DocumentExportService.DeleteRefusalAsync(h.Db, h.CompanyId, true, false, default));
    }

    [Fact]
    public async Task CreateWhileADownloadStreams_IsRefused_AndTheStreamStillFinishes()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var run = await h.Exports.CreateAsync(default);
        var gate = new BlockingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = gate;
        var streaming = h.Exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var otherDb = h.NewDb();
        var other = new DocumentExportService(otherDb, new DocumentCaptureAdmissionLock(otherDb), h.Tenant);
        var refused = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => other.CreateAsync(default));
        Assert.Equal("DOCUMENT_EXPORT_RUNNING", refused.ErrorCode);

        gate.Release.SetResult();
        await streaming.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("server_finished", (await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default)).Status);
    }

    private static readonly DocumentExportLeaseSettings FastLease =
        new(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(900));

    [Fact]
    public async Task ASlowSingleWriteLongerThanTheLease_KeepsItsLease_AndBlocksAConcurrentExport()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var exports = h.ExportsWith(new TestLease(h.NewDb), FastLease);
        var run = await exports.CreateAsync(default);
        var gate = new BlockingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = gate;
        var streaming = exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // One write blocks for twice the lease; renewals continue on their own connection, so the run is never expired.
        await Task.Delay(FastLease.Lease * 2);
        await using var otherDb = h.NewDb();
        var other = new DocumentExportService(otherDb, new DocumentCaptureAdmissionLock(otherDb), h.Tenant, leaseSettings: FastLease);
        Assert.Equal("DOCUMENT_EXPORT_RUNNING", (await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => other.CreateAsync(default))).ErrorCode);

        gate.Release.SetResult();
        await streaming.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("server_finished", (await exports.GetRunAsync(await h.RunIdOf(run.Id), default)).Status);
    }

    [Fact]
    public async Task WhenRenewalsFail_TheStreamFencesItselfBeforeTheLeaseEnds_ThenAnotherExportMayStart()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var exports = h.ExportsWith(new FailingLease(), FastLease);
        var run = await exports.CreateAsync(default);
        var gate = new BlockingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = gate;
        var streaming = exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Never released: the blocked write is cancelled by the self-fence, the output stops, the run fails.
        await streaming.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(gate.Cancelled, "The blocked write must be cancelled, so the old stream stops writing.");
        var failed = await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default);
        Assert.Equal(("failed", "lease-lost"), (failed.Status, failed.FailureReason));
        Assert.Equal("ready", (await exports.CreateAsync(default)).Status);
    }

    [Fact]
    public async Task AHungRenewalThatIgnoresCancellation_StillCancelsTheBlockedWriteBeforeTheLeaseEnds()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var exports = h.ExportsWith(new HungLease(), FastLease);
        var run = await exports.CreateAsync(default);
        var gate = new BlockingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = gate;
        var claimedAt = System.Diagnostics.Stopwatch.StartNew();
        var streaming = exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await streaming.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(gate.Cancelled, "The watchdog must cancel the blocked write even though renewal never returns.");
        Assert.True(claimedAt.Elapsed < FastLease.Lease + TimeSpan.FromMilliseconds(250),
            $"Output stopped after {claimedAt.Elapsed.TotalMilliseconds} ms; the lease is {FastLease.Lease.TotalMilliseconds} ms.");
        Assert.Equal(("failed", "lease-lost"), ((await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default)).Status,
            (await h.Exports.GetRunAsync(await h.RunIdOf(run.Id), default)).FailureReason));
        // Only now, with the old output stopped, can a successor start.
        Assert.Equal("ready", (await exports.CreateAsync(default)).Status);
    }

    [Fact]
    public async Task ARunFencedByAnotherRequest_StopsItsOutputAtTheNextRenewal_AndReportsNoSuccess()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var exports = h.ExportsWith(new TestLease(h.NewDb), FastLease);
        var run = await exports.CreateAsync(default);
        var gate = new BlockingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = gate;
        var streaming = exports.StreamAsync(await h.RunIdOf(run.Id), context.Response, "export-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using (var otherDb = h.NewDb())
            await otherDb.DocumentExportRuns.Where(r => r.PublicId == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "failed").SetProperty(r => r.FailureReason, "stale"));

        await streaming.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(gate.Cancelled);
        Assert.Equal(("failed", "stale"), ((await exports.GetRunAsync(await h.RunIdOf(run.Id), default)).Status,
            (await exports.GetRunAsync(await h.RunIdOf(run.Id), default)).FailureReason));
        h.Audit.Verify(a => a.LogEventAsync("document.export.server_finished", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<object?>()), Times.Never);
    }

    [Fact]
    public async Task DeletedWorkspace_CannotCreateOrStartAnExport()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var run = await h.Exports.CreateAsync(default);
        h.Db.SuppressTenantSecurityCheck = true;
        await h.Db.Companies.IgnoreQueryFilters().Where(c => c.Id == h.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, DateTime.UtcNow));
        h.Db.SuppressTenantSecurityCheck = false;

        Assert.Equal("DOCUMENT_NOT_FOUND", (await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.Exports.CreateAsync(default))).ErrorCode);
        Assert.Equal("DOCUMENT_NOT_FOUND", (await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => h.StreamAsync(run.Id))).ErrorCode);
        Assert.Equal("ready", (await h.Db.DocumentExportRuns.AsNoTracking().SingleAsync(r => r.PublicId == run.Id)).Status);
    }

    [Fact]
    public async Task Retention_FailsStaleRunsAndDropsOldOnes()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedAsync("document", "a", null);
        var stuck = await h.Exports.CreateAsync(default);
        await h.Db.DocumentExportRuns.Where(r => r.PublicId == stuck.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "running").SetProperty(r => r.LastHeartbeatAt, DateTime.UtcNow.AddMinutes(-6)));
        var old = await h.Exports.CreateAsync(default);
        await h.Db.DocumentExportRuns.Where(r => r.PublicId == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTime.UtcNow.AddDays(-31)));

        await new DocumentCaptureRetentionService(h.Db, new DocumentCaptureAdmissionLock(h.Db)).PurgeAsync(DateTime.UtcNow, default);

        Assert.Equal(("failed", "stale"), ((await h.Exports.GetRunAsync(await h.RunIdOf(stuck.Id), default)).Status,
            (await h.Exports.GetRunAsync(await h.RunIdOf(stuck.Id), default)).FailureReason));
        Assert.False(await h.Db.DocumentExportRuns.AnyAsync(r => r.PublicId == old.Id));
    }

    [Theory]
    [InlineData("=SUM(A1)", "\"'=SUM(A1)\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@x", "\"'@x\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("plain", "plain")]
    public void IndexCells_AreQuotedAndFormulaSafe(string value, string expected) =>
        Assert.Equal(expected, DocumentExportService.Cell(value));

    [Fact]
    public void EntryPaths_AreSanitisedAndUnique()
    {
        var capture = new CaptureEntity
        {
            PublicId = "cap_abc", Kind = "document", ContentType = "application/pdf", Title = "../../etc/CON", Category = "legal",
            DocumentDate = new DateTime(2026, 1, 15)
        };
        var path = DocumentExportService.EntryPath(capture);
        Assert.StartsWith("legal/2026-01-15_", path, StringComparison.Ordinal);
        Assert.EndsWith("etc_CON_cap_abc.pdf", path, StringComparison.Ordinal);
        Assert.Equal(1, path.Count(c => c == '/'));
        Assert.DoesNotContain("..", path, StringComparison.Ordinal);
        capture.Title = "CON";
        Assert.Equal("legal/2026-01-15_document_cap_abc.pdf", DocumentExportService.EntryPath(capture));
    }

    private static bool CompressionLevelNone(ZipArchiveEntry entry) => entry.CompressedLength == entry.Length;

    private static string ReadIndex(ZipArchive zip)
    {
        using var reader = new StreamReader(zip.GetEntry("index.csv")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ThrowingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new ArgumentOutOfRangeException(nameof(buffer), "Injected failure.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new ArgumentOutOfRangeException(nameof(buffer), "Injected failure.");
    }

    /// <summary>Holds the first write until released (honouring cancellation, like a real network write to a slow client).</summary>
    private sealed class BlockingStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        private async Task WaitAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class TestLease(Func<ApiDbContext> newDb) : IDocumentExportLease
    {
        public async Task<bool> RenewAsync(int companyId, int runId, DateTime now, CancellationToken cancellationToken)
        {
            await using var db = newDb();
            return await db.DocumentExportRuns.Where(r => r.CompanyId == companyId && r.Id == runId && r.Status == "running")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeatAt, now), cancellationToken) == 1;
        }
    }

    /// <summary>A renewal that never completes and ignores its cancellation token (a hung connection).</summary>
    private sealed class HungLease : IDocumentExportLease
    {
        public Task<bool> RenewAsync(int companyId, int runId, DateTime now, CancellationToken cancellationToken) =>
            new TaskCompletionSource<bool>().Task;
    }

    private sealed class FailingLease : IDocumentExportLease
    {
        public Task<bool> RenewAsync(int companyId, int runId, DateTime now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Database unavailable.");
    }

    private sealed class AbortAfterFirstWriteStream(CancellationTokenSource aborted) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            aborted.Cancel();
            throw new OperationCanceledException(aborted.Token);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            aborted.Cancel();
            throw new OperationCanceledException(aborted.Token);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            aborted.Cancel();
            throw new OperationCanceledException(aborted.Token);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _dbPath;
        public ApiDbContext Db { get; }
        public Tenant Tenant { get; }
        public DocumentExportService Exports { get; }
        public Mock<ISecurityAuditService> Audit { get; } = new();
        public int CompanyId { get; }

        private Harness(string dbPath, ApiDbContext db, Tenant tenant, int companyId)
        {
            _dbPath = dbPath;
            Db = db;
            Tenant = tenant;
            CompanyId = companyId;
            Exports = new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), tenant, Audit.Object);
        }

        public static async Task<Harness> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"accountgo_export_{Guid.NewGuid():N}.db");
            var tenant = new Tenant();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options;
            var db = new ApiDbContext(options, tenant) { SuppressTenantSecurityCheck = true };
            await db.Database.EnsureCreatedAsync();
            var company = new Company { Name = "Export Co", ShortName = "EXP", CompanyCode = "EXP", Slug = "export-co" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            tenant.Id = company.Id;
            db.SuppressTenantSecurityCheck = false;
            return new Harness(dbPath, db, tenant, company.Id);
        }

        public async Task<CaptureEntity> SeedAsync(string kind, string? title, string? category, bool archived = false, bool trashed = false,
            bool filed = true)
        {
            var now = DateTime.UtcNow;
            var bytes = Encoding.UTF8.GetBytes($"%PDF-1.4 {Guid.NewGuid():N}\n%%EOF");
            var capture = new CaptureEntity
            {
                CompanyId = CompanyId, Kind = kind, SchemaVersion = 1, Status = kind == "document" ? "stored" : "accepted",
                SourceHash = Sha(bytes), ContentHash = Sha(bytes), NormalizationVersion = "pdf-structure-v1", ContentType = "application/pdf",
                ByteCount = bytes.Length, PageCount = 1, IdempotencyKey = $"seed-{Guid.NewGuid():N}", RequestFingerprint = new string('b', 64),
                CreatedBy = "actor-1", CreatedAt = now, ExpiresAt = now, Title = title, Category = category,
                FiledAt = filed ? now : null, FiledBy = filed ? "actor-1" : null,
                ArchivedAt = archived ? now : null, DeletedAt = trashed ? now : null,
                Blob = new DocumentCaptureBlob { CompanyId = CompanyId, DocumentBytes = bytes }
            };
            Db.DocumentCaptures.Add(capture);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return capture;
        }

        public DocumentExportService ExportsWith(IDocumentExportLease lease, DocumentExportLeaseSettings settings) =>
            new(Db, new DocumentCaptureAdmissionLock(Db), Tenant, Audit.Object, lease: lease, leaseSettings: settings);

        public ApiDbContext NewDb() =>
            new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite($"Data Source={_dbPath};Pooling=False").Options, Tenant);

        public Task<int> RunIdOf(string publicId) =>
            Db.DocumentExportRuns.AsNoTracking().Where(r => r.PublicId == publicId).Select(r => r.Id).SingleAsync();

        public async Task<(byte[] Zip, DefaultHttpContext Context)> StreamAsync(string runPublicId)
        {
            var context = new DefaultHttpContext();
            var body = new MemoryStream();
            context.Response.Body = body;
            await Exports.StreamAsync(await RunIdOf(runPublicId), context.Response, "export-co", CancellationToken.None);
            return (body.ToArray(), context);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private sealed class Tenant : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int Id { get; set; } = 1;
        public HashSet<string> Perms { get; set; } = new(StringComparer.Ordinal) { SystemPermissions.DocumentsView };
        public int? CompanyId => Id;
        public string? CompanySlug => "export-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "owner-1";
        public IReadOnlySet<string> Permissions => Perms;
        public bool IsResolved => true;
        public int RequireCompanyId() => Id;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }
}
