// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Api.DocumentCaptures;

public sealed record DocumentExportRunView(
    string Id,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    int SelectedCount,
    long SelectedBytes,
    int WrittenCount,
    string? FailureReason,
    bool Current);

/// <summary>What the delete-workspace step needs: whether there is anything to export and whether the latest run still covers it.</summary>
public sealed record DocumentExportStatusView(int FiledCount, long FiledBytes, long CurrentRevision, DocumentExportRunView? Latest);

/// <summary>
/// "Download all documents": an Owner-level export of every filed document of the workspace (design 3.9 S2), independent of the
/// documents feature flag and of operational permissions. The selection and revision are fixed under the company lock; the ZIP
/// is streamed once, one blob in memory at a time, each verified against its stored hash, with <c>index.csv</c> written last.
/// </summary>
public sealed class DocumentExportService(
    ApiDbContext db,
    IDocumentCaptureAdmissionLock admissionLock,
    ITenantContext tenant,
    ISecurityAuditService? securityAudit = null,
    TimeProvider? timeProvider = null)
{
    public const string Ready = "ready";
    public const string Running = "running";
    public const string ServerFinished = "server_finished";
    public const string Failed = "failed";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    public static readonly TimeSpan KeepRunsFor = TimeSpan.FromDays(30);
    private static readonly string[] FilableKinds = ["document", "expense-receipt", "purchase-invoice", "sales-invoice"];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DocumentExportStatusView> GetStatusAsync(CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var filed = Filed(companyId);
        var count = await filed.CountAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await filed.SumAsync(c => (long?)c.ByteCount, cancellationToken).ConfigureAwait(false) ?? 0L;
        var revision = await DocumentRevision.GetCurrentAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        var latest = await db.DocumentExportRuns.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new DocumentExportStatusView(count, bytes, revision, latest is null ? null : ToView(latest, revision));
    }

    public async Task<DocumentExportRunView> GetRunAsync(int runId, CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var run = await db.DocumentExportRuns.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CompanyId == companyId && r.Id == runId, cancellationToken).ConfigureAwait(false)
            ?? throw new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        var revision = await DocumentRevision.GetCurrentAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        return ToView(run, revision);
    }

    /// <summary>Selects the export under the company lock. A newer request supersedes an earlier run not yet downloaded.</summary>
    public async Task<DocumentExportRunView> CreateAsync(CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var actor = string.IsNullOrWhiteSpace(tenant.UserId) ? throw new DocumentCaptureServiceException(401, "DOCUMENT_UNAUTHORIZED") : tenant.UserId!;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        DocumentExportRun run;
        long revision;
        await using (var transaction = await admissionLock.AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await ExpireStaleAsync(companyId, now, cancellationToken).ConfigureAwait(false);
            if (await db.DocumentExportRuns.AnyAsync(r => r.CompanyId == companyId && r.Status == Running, cancellationToken).ConfigureAwait(false))
                throw new DocumentCaptureServiceException(409, "DOCUMENT_EXPORT_RUNNING");
            await db.DocumentExportRuns.Where(r => r.CompanyId == companyId && r.Status == Ready)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, Failed).SetProperty(r => r.FailureReason, "superseded")
                    .SetProperty(r => r.CompletedAt, now), cancellationToken).ConfigureAwait(false);

            var inventory = await Filed(companyId)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.ContentHash, c.ByteCount })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            revision = await DocumentRevision.GetCurrentAsync(db, companyId, cancellationToken).ConfigureAwait(false);
            run = new DocumentExportRun
            {
                CompanyId = companyId,
                Status = Ready,
                RequestedBy = actor,
                CreatedAt = now,
                SelectedRevision = revision,
                SelectedCount = inventory.Count,
                SelectedBytes = inventory.Sum(i => i.ByteCount),
                InventoryJson = JsonSerializer.Serialize(inventory.Select(i => new object[] { i.Id, i.ContentHash }))
            };
            db.DocumentExportRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        db.ChangeTracker.Clear();
        await AuditAsync("document.export.started", companyId, new { exportId = run.PublicId, run.SelectedCount, run.SelectedBytes, run.SelectedRevision })
            .ConfigureAwait(false);
        return ToView(run, revision);
    }

    /// <summary>
    /// Streams the run's ZIP to <paramref name="response"/> once. Marks the run <c>server_finished</c> only when every selected
    /// entry was written and hash-verified, the index and the archive's central directory were written and the response
    /// completed without the client aborting; otherwise <c>failed</c> with the reason and the count written.
    /// </summary>
    public async Task StreamAsync(int runId, HttpResponse response, string companySlug, CancellationToken requestAborted)
    {
        var companyId = tenant.RequireCompanyId();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var claimed = await db.DocumentExportRuns
            .Where(r => r.CompanyId == companyId && r.Id == runId && r.Status == Ready)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, Running).SetProperty(r => r.StreamStartedAt, now), requestAborted)
            .ConfigureAwait(false);
        if (claimed != 1)
        {
            var exists = await db.DocumentExportRuns.AnyAsync(r => r.CompanyId == companyId && r.Id == runId, requestAborted).ConfigureAwait(false);
            throw exists
                ? new DocumentCaptureServiceException(409, "DOCUMENT_EXPORT_NOT_READY")
                : new DocumentCaptureServiceException(404, "DOCUMENT_NOT_FOUND");
        }
        var run = await db.DocumentExportRuns.AsNoTracking().SingleAsync(r => r.CompanyId == companyId && r.Id == runId, requestAborted)
            .ConfigureAwait(false);
        var inventory = JsonSerializer.Deserialize<JsonElement[][]>(run.InventoryJson) ?? [];

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/zip";
        response.Headers.CacheControl = "private, no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers.ContentDisposition =
            $"attachment; filename=\"documents-{DocumentLibraryService.SafeName(companySlug, 40)}-{now:yyyy-MM-dd}.zip\"";
        response.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        var rows = new List<IndexRow>(inventory.Length);
        var written = 0;
        string? failure = null;
        try
        {
            var body = new AsyncFlushingStream(response.Body);
            var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8,
                cancellationToken: requestAborted).ConfigureAwait(false);
            await using (zip.ConfigureAwait(false))
            {
                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in inventory)
                {
                    requestAborted.ThrowIfCancellationRequested();
                    var captureId = item[0].GetInt32();
                    var expectedHash = item[1].GetString() ?? string.Empty;
                    var row = await WriteEntryAsync(zip, companyId, captureId, expectedHash, usedPaths, requestAborted).ConfigureAwait(false);
                    rows.Add(row);
                    if (row.Verification == "ok")
                        written++;
                    else
                        failure ??= row.Verification == "missing" ? "missing-files" : "hash-mismatch";
                    db.ChangeTracker.Clear();
                }

                var complete = failure is null && written == inventory.Length;
                // Stored, not deflated: deflate flushes synchronously on dispose, which the server's response stream refuses.
                var index = zip.CreateEntry("index.csv", CompressionLevel.NoCompression);
                var indexStream = await index.OpenAsync(requestAborted).ConfigureAwait(false);
                await using (indexStream.ConfigureAwait(false))
                {
                    var bytes = Encoding.UTF8.GetBytes(BuildIndex(rows, complete, failure, written, inventory.Length, run.PublicId));
                    await indexStream.WriteAsync(bytes, requestAborted).ConfigureAwait(false);
                }
            }
            // Disposing the archive wrote the central directory; only now is the ZIP valid.
            await body.FlushAsync(requestAborted).ConfigureAwait(false);
            await response.CompleteAsync().ConfigureAwait(false);
            if (requestAborted.IsCancellationRequested)
                failure ??= "client-disconnected";
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            failure ??= "client-disconnected";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DbUpdateException)
        {
            failure ??= "server-error";
        }
        finally
        {
            var completedAt = _timeProvider.GetUtcNow().UtcDateTime;
            var finalStatus = failure is null ? ServerFinished : Failed;
            await db.DocumentExportRuns.Where(r => r.CompanyId == companyId && r.Id == runId && r.Status == Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, finalStatus)
                    .SetProperty(r => r.WrittenCount, written)
                    .SetProperty(r => r.FailureReason, failure)
                    .SetProperty(r => r.CompletedAt, completedAt), CancellationToken.None).ConfigureAwait(false);
            await AuditAsync(failure is null ? "document.export.server_finished" : "document.export.failed", companyId,
                new { exportId = run.PublicId, written, selected = inventory.Length, reason = failure }).ConfigureAwait(false);
        }
    }

    /// <summary>Whether a workspace may be deleted now: nothing filed, an explicit skip, or a confirmed export still current.</summary>
    public static async Task<string?> DeleteRefusalAsync(ApiDbContext db, int companyId, bool exportConfirmed, bool skipExport,
        CancellationToken cancellationToken)
    {
        var filed = await db.DocumentCaptures.AsNoTracking()
            .AnyAsync(c => c.CompanyId == companyId && c.FiledAt != null && c.BytesDeletedAt == null && FilableKinds.Contains(c.Kind),
                cancellationToken).ConfigureAwait(false);
        if (!filed || skipExport)
            return null;
        var latest = await db.DocumentExportRuns.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (latest is null || latest.Status != ServerFinished)
            return "DOCUMENT_EXPORT_REQUIRED";
        var revision = await DocumentRevision.GetCurrentAsync(db, companyId, cancellationToken).ConfigureAwait(false);
        if (latest.SelectedRevision != revision)
            return "DOCUMENT_EXPORT_STALE";
        return exportConfirmed ? null : "DOCUMENT_EXPORT_CONFIRMATION_REQUIRED";
    }

    private async Task ExpireStaleAsync(int companyId, DateTime now, CancellationToken cancellationToken)
    {
        var staleBefore = now - StaleAfter;
        await db.DocumentExportRuns
            .Where(r => r.CompanyId == companyId && r.Status == Running && r.StreamStartedAt <= staleBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, Failed).SetProperty(r => r.FailureReason, "stale")
                .SetProperty(r => r.CompletedAt, now), cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<DocumentCapture> Filed(int companyId) =>
        db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiledAt != null && c.BytesDeletedAt == null && FilableKinds.Contains(c.Kind));

    private async Task<IndexRow> WriteEntryAsync(ZipArchive zip, int companyId, int captureId, string expectedHash,
        HashSet<string> usedPaths, CancellationToken cancellationToken)
    {
        var capture = await db.DocumentCaptures.AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Id == captureId, cancellationToken).ConfigureAwait(false);
        var blob = capture is null ? null : await db.DocumentCaptureBlobs.AsNoTracking()
            .SingleOrDefaultAsync(b => b.CompanyId == companyId && b.CaptureId == captureId, cancellationToken).ConfigureAwait(false);
        if (capture is null || blob is null)
            return IndexRow.Missing(captureId, capture, expectedHash);

        var path = UniquePath(EntryPath(capture), usedPaths);
        var actualHash = Convert.ToHexString(SHA256.HashData(blob.DocumentBytes)).ToLowerInvariant();
        var verification = string.Equals(actualHash, expectedHash, StringComparison.Ordinal) ? "ok" : "hash_mismatch";
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(capture.FiledAt ?? capture.CreatedAt, DateTimeKind.Utc));
        var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (entryStream.ConfigureAwait(false))
            await entryStream.WriteAsync(blob.DocumentBytes, cancellationToken).ConfigureAwait(false);
        if (verification != "ok" && securityAudit is not null)
            await securityAudit.LogEventAsync("document.export.hash_mismatch", "failure", tenant.UserId, companyId,
                new { documentId = capture.PublicId }).ConfigureAwait(false);
        var links = await LinkSummaryAsync(companyId, captureId, cancellationToken).ConfigureAwait(false);
        return new IndexRow(capture.PublicId, capture.Kind, Title(capture), capture.Category, capture.DocumentDate, capture.FileName,
            capture.ContentType, capture.ByteCount, capture.ContentHash, capture.SourceHash,
            capture.DeletedAt is not null ? "trash" : capture.ArchivedAt is not null ? "archived" : "active",
            links, capture.FiledBy ?? capture.CreatedBy, capture.FiledAt ?? capture.CreatedAt, verification, path);
    }

    private async Task<string> LinkSummaryAsync(int companyId, int captureId, CancellationToken cancellationToken)
    {
        var links = await db.DocumentCaptureLinks.AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.CaptureId == captureId)
            .OrderBy(l => l.LinkedAt)
            .Select(l => new { l.TargetType, l.TargetId, l.Role })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var parts = new List<string>(links.Count);
        foreach (var link in links)
        {
            var label = link.TargetType switch
            {
                "bill" => await db.PurchaseInvoiceHeaders.AsNoTracking().Where(h => h.CompanyId == companyId && h.Id == link.TargetId)
                    .Select(h => h.PublicId + (h.No != null ? " " + h.No : "")).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false),
                "invoice" => await db.SalesInvoiceHeaders.AsNoTracking().Where(h => h.CompanyId == companyId && h.Id == link.TargetId)
                    .Select(h => h.PublicId + (h.No != null ? " " + h.No : "")).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false),
                "customer" => await db.Customers.AsNoTracking().Where(h => h.CompanyId == companyId && h.Id == link.TargetId)
                    .Select(h => h.PublicId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false),
                "vendor" => await db.Vendors.AsNoTracking().Where(h => h.CompanyId == companyId && h.Id == link.TargetId)
                    .Select(h => h.PublicId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false),
                _ => null
            };
            parts.Add($"{link.TargetType}:{label ?? "deleted"} ({link.Role})");
        }
        return string.Join("; ", parts);
    }

    internal static string EntryPath(DocumentCapture capture)
    {
        var folder = DocumentLibraryService.SafeName(capture.Category ?? (capture.Kind == "document" ? "uncategorised" : "scans"), 40)
            .Replace("..", "_", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(folder))
            folder = "uncategorised";
        var date = capture.DocumentDate is { } d ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "undated";
        var title = DocumentLibraryService.SafeName(Title(capture), 80).Replace(' ', '_').Replace("..", "_", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(title) || IsReservedWindowsName(title))
            title = "document";
        var extension = capture.ContentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".bin"
        };
        var prefix = capture.DeletedAt is not null ? "_trash/" : string.Empty;
        return $"{prefix}{folder}/{date}_{title}_{capture.PublicId}{extension}";
    }

    private static string UniquePath(string path, HashSet<string> used)
    {
        // Public ids already make paths unique; this guards against a case-insensitive collision anyway.
        var candidate = path;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = Path.ChangeExtension(path, null) + $"_{n}" + Path.GetExtension(path);
        return candidate;
    }

    private static bool IsReservedWindowsName(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]));
    }

    private static string Title(DocumentCapture capture) => capture.Title ?? capture.Kind switch
    {
        "expense-receipt" => "Scanned receipt",
        "purchase-invoice" => "Scanned bill",
        "sales-invoice" => "Scanned invoice",
        _ => capture.FileName is { Length: > 0 } name ? Path.GetFileNameWithoutExtension(name) : "Document"
    };

    internal static string BuildIndex(IReadOnlyList<IndexRow> rows, bool complete, string? failure, int written, int selected, string exportId)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', "id", "kind", "title", "category", "document_date", "original_file_name", "content_type",
            "size_bytes", "stored_sha256", "uploaded_sha256", "status", "linked_records", "added_by", "added_at", "verification", "zip_path"));
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',',
                Cell(row.Id), Cell(row.Kind), Cell(row.Title), Cell(row.Category),
                Cell(row.DocumentDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.FileName), Cell(row.ContentType), Cell(row.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.StoredSha256), Cell(row.UploadedSha256), Cell(row.Status), Cell(row.LinkedRecords), Cell(row.AddedBy),
                Cell(row.AddedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.Verification), Cell(row.ZipPath)));
        }
        // Asserts the selected entries above, not the ZIP's own validity, which exists only once its central directory is written.
        csv.Append(complete
            ? $"# complete,{written},{selected},{exportId}\n"
            : $"# incomplete,{failure ?? "unknown"},{written}/{selected},{exportId}\n");
        return csv.ToString();
    }

    /// <summary>RFC 4180 quoting; a leading = + - @ tab or CR gets an apostrophe so spreadsheets never run it as a formula.</summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith('\'')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static DocumentExportRunView ToView(DocumentExportRun run, long currentRevision) => new(
        run.PublicId,
        run.Status,
        DateTime.SpecifyKind(run.CreatedAt, DateTimeKind.Utc),
        run.CompletedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
        run.SelectedCount,
        run.SelectedBytes,
        run.WrittenCount,
        run.FailureReason,
        run.Status == ServerFinished && run.SelectedRevision == currentRevision);

    private Task AuditAsync(string eventType, int companyId, object details) => securityAudit is null
        ? Task.CompletedTask
        : securityAudit.LogEventAsync(eventType, "success", tenant.UserId, companyId, details);

    /// <summary>
    /// The ZIP writer still writes some small records (entry data descriptors) synchronously, which the server's response stream
    /// refuses. This wrapper collects every write and sends it on asynchronously once 64 KiB are pending, on an async write, or
    /// on flush. Synchronous writes only append, so they never block on the network.
    /// </summary>
    private sealed class AsyncFlushingStream(Stream inner) : Stream
    {
        private const int FlushThreshold = 64 * 1024;
        private readonly MemoryStream _pending = new();
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _pending.Write(buffer);
            _written += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _written += buffer.Length;
            if (buffer.Length >= FlushThreshold)
            {
                // File contents go straight through, so memory stays at one file, not a second buffered copy.
                await SendPendingAsync(cancellationToken).ConfigureAwait(false);
                await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                return;
            }
            _pending.Write(buffer.Span);
            if (_pending.Length >= FlushThreshold)
                await SendPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SendPendingAsync(cancellationToken).ConfigureAwait(false);
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task SendPendingAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length == 0)
                return;
            await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken).ConfigureAwait(false);
            _pending.SetLength(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _pending.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed record IndexRow(
        string Id, string? Kind, string? Title, string? Category, DateTime? DocumentDate, string? FileName, string? ContentType,
        long? SizeBytes, string? StoredSha256, string? UploadedSha256, string? Status, string? LinkedRecords, string? AddedBy,
        DateTime? AddedAt, string Verification, string? ZipPath)
    {
        public static IndexRow Missing(int captureId, DocumentCapture? capture, string expectedHash) => new(
            capture?.PublicId ?? $"#{captureId}", capture?.Kind, capture?.Title, capture?.Category, capture?.DocumentDate, capture?.FileName,
            capture?.ContentType, capture?.ByteCount, expectedHash, capture?.SourceHash, null, null, null, null, "missing", null);
    }
}
